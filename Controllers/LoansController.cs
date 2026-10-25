using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

[Route("circles/{circleId:int}/loans")]
public class LoansController(CircleAccessService access, LoanService loans, LedgerClock clock) : CircleControllerBase(access)
{
    private static readonly string[] Filters = ["all", "overdue", "soon", "ok", "paid"];

    // Most urgent first: overdue (longest first), due soon, up to date, then paid.
    private static int Severity(LoanStatus s) => s switch { LoanStatus.Overdue => 0, LoanStatus.DueSoon => 1, LoanStatus.UpToDate => 2, _ => 3 };

    private static string StatusKey(LoanStatus s) => s switch
    {
        LoanStatus.Overdue => "overdue",
        LoanStatus.DueSoon => "soon",
        LoanStatus.Paid => "paid",
        _ => "ok",
    };

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? filter)
    {
        filter = Filters.Contains(filter) ? filter! : "all";
        var visible = await loans.VisibleAsync(Access);

        bool Match(LoanSummary l) => string.IsNullOrWhiteSpace(q)
            || l.Loan.Borrower.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
            || l.Loan.Reference.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase);
        bool InFilter(LoanSummary l, string f) => f == "all" || StatusKey(l.Status) == f;

        return View(new LoansViewModel
        {
            Loans = visible.Where(l => InFilter(l, filter) && Match(l)).OrderBy(l => Severity(l.Status)).ThenByDescending(l => l.Days).ToList(),
            Voided = await loans.VoidedAsync(Access),
            Awaiting = await loans.AwaitingAsync(Access),
            Query = q,
            Filter = filter,
            Counts = Filters.ToDictionary(f => f, f => visible.Count(l => InFilter(l, f))),
            OpenCount = visible.Count(l => l.IsOpen),
            Outstanding = visible.Sum(l => l.Balance),
        });
    }

    // Treasurer only: who is overdue or due soon, each with a ready-written WhatsApp reminder.
    [HttpGet("reminders")]
    public async Task<IActionResult> Reminders()
    {
        if (!Access.IsTreasurer) return NotFound();
        var due = (await loans.VisibleAsync(Access))
            .Where(l => l.Status is LoanStatus.Overdue or LoanStatus.DueSoon && l.Loan.BorrowerId != Access.Me.Id)   // not a reminder to yourself
            .OrderBy(l => Severity(l.Status)).ThenByDescending(l => l.Days)
            .ToList();
        return View(new RemindersViewModel { Rows = due.Select(l => new ReminderRow(l, ReminderLink(l))).ToList() });
    }

    private string ReminderLink(LoanSummary l)
    {
        var borrower = l.Loan.Borrower;
        var pay = Url.Action("Create", "Payments", new { circleId = Access.Circle.Id, loanId = l.Loan.Id }, Request.Scheme);
        var when = l.Status == LoanStatus.Overdue
            ? $"was due on {Format.Date(l.NextDue!.Value)} ({Format.Plural(l.Days, "day")} ago)"
            : $"is due on {Format.Date(l.NextDue!.Value)}";
        return WhatsApp.Link(borrower.Phone,
            $"Hi {borrower.FirstName}, a friendly reminder from {Access.Me.FirstName} at {Access.Circle.Name}: your payment of {Format.Zar(l.NextAmount)} " +
            $"for loan {l.Loan.Reference} {when}. You still owe {Format.Zar(l.Balance)}. After you pay, record it here: {pay}");
    }

    // A printable statement of one loan, oldest entry first. The browser's print dialog saves it as a PDF.
    [HttpGet("{id:int}/statement")]
    public async Task<IActionResult> Statement(int id)
    {
        var loan = await loans.FindAsync(Access, id);
        if (loan is null) return NotFound();
        if (loan.IsAwaiting) return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id });
        var rows = LedgerRows(loan);
        rows.Reverse();   // LedgerRows is newest first; a statement reads from the top
        return View(new LoanDetailViewModel { Summary = loan, Rows = rows, Today = clock.Today });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var loan = await loans.FindAsync(Access, id);
        if (loan is null) return NotFound();
        var nudge = loan.IsAwaiting && Access.IsTreasurer && loan.Loan.BorrowerId != Access.Me.Id ? AcceptanceLink(loan) : null;
        return View(new LoanDetailViewModel { Summary = loan, Rows = LedgerRows(loan), Today = clock.Today, NudgeLink = nudge });
    }

    // The borrower has seen the principal, interest and total to repay, and agrees. Only the borrower can do this.
    [HttpPost("{id:int}/accept"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        var result = await loans.AcceptAsync(Access, id);
        Toast(result.Succeeded ? "You accepted the terms. The loan is now active." : result.Error!);
        return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id });
    }

    // A ready-written WhatsApp message: the amounts, and a link to the loan where the borrower accepts.
    private string AcceptanceLink(LoanSummary l)
    {
        var borrower = l.Loan.Borrower;
        var link = Url.Action(nameof(Details), "Loans", new { circleId = Access.Circle.Id, id = l.Loan.Id }, Request.Scheme);
        return WhatsApp.Link(borrower.Phone,
            $"Hi {borrower.FirstName}, {Access.Me.FirstName} at {Access.Circle.Name} has recorded loan {l.Loan.Reference} for you: " +
            $"{Format.Zar(l.Loan.Principal)} plus {Format.Zar(l.Interest)} interest, so {Format.Zar(l.Total)} to repay in {Format.Plural(l.Loan.Term, "monthly payment")}. " +
            $"Please check the amounts and accept here: {link}");
    }

    // Voids a loan recorded by mistake. It stays on record with the reason and stops counting.
    [HttpPost("{id:int}/void"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Void(int id, string? reason)
    {
        if (!Access.IsTreasurer) return NotFound();
        var result = await loans.VoidLoanAsync(Access, id, reason);
        Toast(result.Succeeded ? "Loan voided. It stays in the history but no longer counts." : result.Error!);
        return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id });
    }

    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id)
    {
        var loan = await loans.FindAsync(Access, id);
        if (loan is null) return NotFound();
        if (loan.IsAwaiting) return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id });
        var csv = new StringBuilder("Date,Transaction,Detail,Amount,Balance owing\n");
        foreach (var r in LedgerRows(loan))
            csv.AppendLine(string.Join(",", r.Date.ToString("yyyy-MM-dd"), Csv(r.Title), Csv(r.Sub),
                ((r.IsCredit ? -1 : 1) * r.Amount).ToString("0.00", CultureInfo.InvariantCulture), r.Balance.ToString("0.00", CultureInfo.InvariantCulture)));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"{loan.Loan.Reference}-ledger.csv");
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create(int? borrowerId)
    {
        if (!Access.IsTreasurer) return NotFound();
        var borrowers = await loans.BorrowersAsync(Access.Circle.Id);
        if (!borrowers.Any(b => b.Id != Access.Me.Id))
        {
            Toast("Invite someone first. Loans can be recorded once they accept.");
            return RedirectToAction("Invite", "Members", new { circleId = Access.Circle.Id });
        }
        var model = new NewLoanViewModel { Borrowers = borrowers, StartDate = clock.Today, BorrowerId = borrowerId ?? borrowers[0].Id };
        if (borrowerId is not null) { model.Principal = 2000; model.Term = 4; }
        return View(model);
    }

    // "review" opens the confirmation dialog; "confirm" (from the dialog) records the loan.
    [HttpPost("new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NewLoanViewModel model, string? command)
    {
        if (!Access.IsTreasurer) return NotFound();
        model.Borrowers = await loans.BorrowersAsync(Access.Circle.Id);
        var input = new NewLoan(model.BorrowerId, model.Principal ?? 0, model.RatePercent ?? 0, model.Type, model.StartDate, model.Term);
        foreach (var (key, error) in await loans.ValidateAsync(Access, input))
            ModelState.AddModelError(key, error);
        if (!ModelState.IsValid) return View(model);

        if (command == "edit") return View(model);
        if (command != "confirm")
        {
            model.Confirming = true;
            return View(model);
        }
        var result = await loans.CreateAsync(Access, input);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error!);
            return View(model);
        }
        Toast($"Loan recorded for {model.BorrowerName}. Total payable {Format.Zar(model.Total)}. It starts once they accept the terms.");
        return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id = result.Id });
    }

    // Oldest first with a running balance, returned newest first. Nothing is hidden: a reversed payment shows twice,
    // as the payment and as its reversal, and a voided loan ends with a "Loan voided" entry.
    private static List<LedgerRow> LedgerRows(LoanSummary s)
    {
        var l = s.Loan;
        var rows = new List<LedgerRow>();
        var run = l.Principal;
        rows.Add(new(l.StartDate, "Loan issued", "Amount lent", l.Principal, false, run));
        run += s.Interest;
        var how = l.InterestType == InterestType.Simple ? $"{l.RatePercent:0.##}% simple interest" : $"{l.RatePercent:0.##}% a month for {Format.Plural(l.Term, "month")}";
        rows.Add(new(l.StartDate, "Interest added", how, s.Interest, false, run));

        var events = new List<(DateOnly Date, int Order, int Id, Repayment Payment, bool Reversal)>();
        foreach (var r in l.Repayments.Where(r => r.Status is PaymentStatus.Confirmed or PaymentStatus.Voided))
        {
            events.Add((r.Date, 0, r.Id, r, false));
            if (r.Status == PaymentStatus.Voided)
                events.Add((Max(DateOnly.FromDateTime(r.VoidedAt ?? DateTime.UtcNow), r.Date), 1, r.Id, r, true));
        }
        foreach (var (date, _, _, r, reversal) in events.OrderBy(e => e.Date).ThenBy(e => e.Order).ThenBy(e => e.Id))
        {
            if (reversal)
            {
                run += r.Amount;
                rows.Add(new(date, "Repayment reversed", "Reason: " + r.VoidReason, r.Amount, false, run));
            }
            else
            {
                run -= r.Amount;
                rows.Add(new(date, "Repayment", r.Method + (string.IsNullOrEmpty(r.Note) ? "" : " · " + r.Note), r.Amount, true, run));
            }
        }

        if (l.IsVoided)
        {
            var when = DateOnly.FromDateTime(l.VoidedAt!.Value);
            rows.Add(new(when, "Loan voided", "Reason: " + l.VoidReason, run, true, 0));
        }
        rows.Reverse();
        return rows;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;

    private static string Csv(string s) => CsvSafe.Text(s);
}
