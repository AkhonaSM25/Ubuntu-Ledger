using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

// Members and invites. Treasurer only: members never see each other's balances.
[Route("circles/{circleId:int}/members")]
public class MembersController(CircleAccessService access, LoanService loans, CircleService circles, AccountService accounts, LedgerDbContext db) : CircleControllerBase(access)
{
    private static readonly string[] Filters = ["all", "ok", "soon", "overdue"];

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? filter)
    {
        if (!Access.IsTreasurer) return NotFound();
        filter = Filters.Contains(filter) ? filter! : "all";

        var all = await loans.VisibleAsync(Access);
        var people = await loans.BorrowersAsync(Access.Circle.Id);
        var summaries = people.Select(p => MemberSummary.From(p, all)).ToList();
        var pending = await circles.PendingForCircleAsync(Access.Circle.Id);

        bool Match(string name) => string.IsNullOrWhiteSpace(q) || name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase);
        bool InFilter(MemberSummary m, string f) => f == "all" || StatusKey(m.Status) == f;

        var current = people.Select(p => p.Id).ToHashSet();
        var former = all.Where(l => !current.Contains(l.Loan.BorrowerId))
            .GroupBy(l => l.Loan.BorrowerId)
            .Select(g => MemberSummary.From(g.First().Loan.Borrower, all))
            .Where(m => Match(m.Person.Name))
            .OrderBy(m => m.Person.Name)
            .ToList();

        return View(new MembersViewModel
        {
            Former = filter == "all" ? former : [],
            Rows = summaries.Where(m => InFilter(m, filter) && Match(m.Person.Name)).ToList(),
            Pending = filter == "all" ? pending.Where(p => Match(p.Name)).ToList() : [],
            Counts = Filters.ToDictionary(f => f, f => f == "all" ? summaries.Count + pending.Count : summaries.Count(m => InFilter(m, f))),
            MemberCount = summaries.Count,
            InvitedCount = pending.Count,
            Query = q,
            Filter = filter,
        });
    }

    [HttpGet("{personId:int}")]
    public async Task<IActionResult> Details(int personId)
    {
        if (!Access.IsTreasurer) return NotFound();
        var membership = await db.Memberships.Include(m => m.Person)
            .FirstOrDefaultAsync(m => m.CircleId == Access.Circle.Id && m.PersonId == personId);
        if (membership is null) return NotFound();

        var summary = MemberSummary.From(membership.Person, await loans.VisibleAsync(Access));
        var nextDue = summary.Active.Select(l => l.NextDue!.Value).OrderBy(d => d).Cast<DateOnly?>().FirstOrDefault();
        IReadOnlyList<StatCard> stats =
        [
            new("Balance owing", Format.Zar(summary.Owed), Format.Plural(summary.Active.Count, "active loan"), "wallet", Featured: true),
            new("Total borrowed", Format.Zar(summary.Loans.Sum(l => l.Loan.Principal)), "Across " + Format.Plural(summary.Loans.Count, "loan"), "coin"),
            new("Total repaid", Format.Zar(summary.Loans.Sum(l => l.Paid)), Format.Plural(summary.Loans.Sum(l => l.Confirmed.Count()), "payment"), "down"),
            new("Next payment", nextDue is { } d ? Format.Date(d) : "None", nextDue is null ? "All loans paid" : "Earliest due date", "cal"),
        ];

        // Every loan (principal plus interest) and payment in date order, with the running balance owed after each.
        var events = summary.Loans
            .Select(l => (Date: l.Loan.StartDate, Order: 0, Pay: false, Amount: l.Total, Title: "Loan of " + Format.Zar(l.Loan.Principal), Sub: l.Loan.Reference + " · interest added"))
            .Concat(summary.Loans.SelectMany(l => l.Confirmed.Select(r =>
                (Date: r.Date, Order: 1, Pay: true, Amount: -r.Amount, Title: "Repayment received", Sub: l.Loan.Reference + " · " + r.Method))))
            .OrderBy(e => e.Date).ThenBy(e => e.Order)
            .ToList();
        var running = 0m;
        var timeline = events.Select(e => new TimelineEntry(e.Date, e.Pay, e.Amount, e.Title, e.Sub, running += e.Amount)).Reverse().ToList();

        // The reset link is handed over once: it is in TempData only for the page that follows the reset.
        var resetLink = TempData["ResetLink"] as string;
        var canReset = membership.PersonId != Access.Me.Id && await db.Memberships.CountAsync(m => m.PersonId == personId) == 1;
        return View(new MemberDetailViewModel
        {
            Member = summary, MemberSince = membership.JoinedOn, Stats = stats, Timeline = timeline,
            CanResetPin = canReset,
            IsBackup = membership.Role == CircleRole.Backup,
            CanManage = membership.Role != CircleRole.Treasurer && membership.PersonId != Access.Me.Id,
            ResetLink = resetLink,
            ResetWhatsAppUrl = resetLink is null ? null : WhatsApp.Link(membership.Person.Phone,
                $"Hi {membership.Person.FirstName}, here is your link to set a new PIN for Ubuntu Ledger: {resetLink} It works once and expires in {AccountService.ResetLinkLife.TotalHours:0} hours."),
        });
    }

    // Forgot PIN: the treasurer makes a one-time link and sends it to the member, who picks the new PIN themselves.
    [HttpPost("{personId:int}/reset-pin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPin(int personId)
    {
        if (!Access.IsTreasurer) return NotFound();
        var (token, error) = await accounts.CreatePinResetAsync(Access, personId);
        if (token is null) Toast(error!);
        else TempData["ResetLink"] = Url.Action("ResetPin", "Account", new { token }, Request.Scheme);
        return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, personId });
    }

    // The treasurer chooses a backup treasurer, or takes the role away again.
    [HttpPost("{personId:int}/backup"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Backup(int personId, bool appoint)
    {
        if (!Access.IsTreasurer) return NotFound();
        var result = await circles.SetBackupAsync(Access, personId, appoint);
        Toast(result.Succeeded ? (appoint ? "Backup treasurer chosen." : "Backup treasurer removed.") : result.Error!);
        return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, personId });
    }

    // The treasurer removes a member. Their loans and payments stay in the history.
    [HttpPost("{personId:int}/remove"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int personId)
    {
        if (!Access.IsTreasurer) return NotFound();
        var result = await circles.RemoveMemberAsync(Access, personId);
        Toast(result.Succeeded ? "Member removed. Their past loans and payments stay in the history." : result.Error!);
        return result.Succeeded
            ? RedirectToAction(nameof(Index), new { circleId = Access.Circle.Id })
            : RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, personId });
    }

    // Any member can leave once they owe nothing.
    [HttpPost("leave"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Leave()
    {
        var result = await circles.RemoveMemberAsync(Access, Access.Me.Id);
        Toast(result.Succeeded ? $"You left {Access.Circle.Name}." : result.Error!);
        return result.Succeeded
            ? RedirectToAction("Index", "Circles")
            : RedirectToAction("Index", "Dashboard", new { circleId = Access.Circle.Id });
    }

    // Backup treasurer only: a reset link for the treasurer who forgot their PIN.
    [HttpGet("treasurer-recovery")]
    public async Task<IActionResult> TreasurerRecovery()
    {
        if (!Access.IsBackup) return NotFound();
        var treasurer = await db.People.AsNoTracking().FirstAsync(p => p.Id == Access.TreasurerId);
        var link = TempData["ResetLink"] as string;
        return View(new TreasurerRecoveryViewModel
        {
            TreasurerName = treasurer.Name,
            ResetLink = link,
            ResetWhatsAppUrl = link is null ? null : WhatsApp.Link(treasurer.Phone,
                $"Hi {treasurer.FirstName}, here is your link to set a new PIN for Ubuntu Ledger: {link} It works once and expires in {AccountService.ResetLinkLife.TotalHours:0} hours."),
        });
    }

    [HttpPost("treasurer-recovery"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTreasurerRecovery()
    {
        if (!Access.IsBackup) return NotFound();
        var (token, error) = await accounts.CreatePinResetAsync(Access, Access.TreasurerId);
        if (token is null) Toast(error!);
        else TempData["ResetLink"] = Url.Action("ResetPin", "Account", new { token }, Request.Scheme);
        return RedirectToAction(nameof(TreasurerRecovery), new { circleId = Access.Circle.Id });
    }

    [HttpGet("invite")]
    public async Task<IActionResult> Invite()
    {
        if (!Access.IsTreasurer) return NotFound();
        return View(await InviteModelAsync(new InviteViewModel()));
    }

    [HttpPost("invite"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(InviteViewModel model)
    {
        if (!Access.IsTreasurer) return NotFound();
        if (ModelState.IsValid)
        {
            var result = await circles.InviteAsync(Access, model.Name, model.Phone);
            if (result.Succeeded)
            {
                var who = string.IsNullOrWhiteSpace(model.Name) ? model.Phone.Trim() : model.Name.Trim();
                Toast($"Invite sent to {who}. They can join once they accept.");
                return RedirectToAction(nameof(Invite));
            }
            ModelState.AddModelError(nameof(model.Phone), result.Error!);
        }
        return View(await InviteModelAsync(model));
    }

    [HttpPost("invite/{id:int}/cancel"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelInvite(int id)
    {
        var result = await circles.CancelInviteAsync(Access, id);
        if (!result.Succeeded) Toast(result.Error!);
        return RedirectToAction(nameof(Invite));
    }

    // The waiting invites, each with a "send on WhatsApp" link that carries the join link.
    private async Task<InviteViewModel> InviteModelAsync(InviteViewModel model)
    {
        model.Pending = await circles.PendingForCircleAsync(Access.Circle.Id);
        model.ShareLinks = model.Pending.Where(i => i.Token is not null).ToDictionary(i => i.Id, i =>
        {
            var join = Url.Action("Join", "Account", new { token = i.Token }, Request.Scheme);
            var greeting = i.Name.Any(char.IsLetter) ? $"Hi {i.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]}, " : "Hi, ";
            return WhatsApp.Link(i.Phone,
                $"{greeting}{Access.Me.FirstName} invited you to join \"{Access.Circle.Name}\" on Ubuntu Ledger. Open this link to set your PIN and join: {join}");
        });
        return model;
    }

    public static string StatusKey(LoanStatus s) => s switch
    {
        LoanStatus.Overdue => "overdue",
        LoanStatus.DueSoon => "soon",
        _ => "ok",
    };
}
