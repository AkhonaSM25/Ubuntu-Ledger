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
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var visible = await loans.VisibleAsync(Access);
        // Most urgent first: overdue (longest first), due soon, up to date, then paid.
        int Severity(LoanStatus s) => s switch { LoanStatus.Overdue => 0, LoanStatus.DueSoon => 1, LoanStatus.UpToDate => 2, _ => 3 };
        return View(new LoansViewModel { Loans = visible.OrderBy(l => Severity(l.Status)).ThenByDescending(l => l.Days).ToList() });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var loan = await loans.FindAsync(Access, id);
        if (loan is null) return NotFound();
        return View(new LoanDetailViewModel { Summary = loan, Rows = LedgerRows(loan), Today = clock.Today });
    }

    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id)
    {
        var loan = await loans.FindAsync(Access, id);
        if (loan is null) return NotFound();
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
        if (borrowers.Count == 0)
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
        Toast($"Loan created for {model.BorrowerName}. Total payable {Format.Zar(model.Total)}.");
        return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id = result.Id });
    }

    // Oldest first with a running balance, returned newest first.
    private static List<LedgerRow> LedgerRows(LoanSummary s)
    {
        var l = s.Loan;
        var rows = new List<LedgerRow>();
        var run = l.Principal;
        rows.Add(new(l.StartDate, "Loan issued", "Amount lent", l.Principal, false, run));
        run += s.Interest;
        var how = l.InterestType == InterestType.Simple ? $"{l.RatePercent:0.##}% simple interest" : $"{l.RatePercent:0.##}% a month for {Format.Plural(l.Term, "month")}";
        rows.Add(new(l.StartDate, "Interest added", how, s.Interest, false, run));
        foreach (var r in l.Repayments.OrderBy(r => r.Date).ThenBy(r => r.Id))
        {
            run -= r.Amount;
            rows.Add(new(r.Date, "Repayment", r.Method + (string.IsNullOrEmpty(r.Note) ? "" : " · " + r.Note), r.Amount, true, run));
        }
        rows.Reverse();
        return rows;
    }

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
