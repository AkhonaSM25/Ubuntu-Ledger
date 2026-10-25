using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

[Route("circles/{circleId:int}")]
public class DashboardController(CircleAccessService access, LoanService loans, LoanRequestService requests, LedgerDbContext db, LedgerClock clock) : CircleControllerBase(access)
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var visible = await loans.VisibleAsync(Access);
        var today = clock.Today;
        var lent = visible.Sum(l => l.Loan.Principal);
        var repaid = visible.Sum(l => l.Paid);
        var outstanding = visible.Sum(l => l.Balance);
        var thisMonth = visible.Sum(l => l.Confirmed
            .Where(r => r.Date.Year == today.Year && r.Date.Month == today.Month)
            .Sum(r => LoanCalculator.InterestShare(l, r.Amount)));
        var open = visible.Count(l => l.IsOpen);
        var payments = visible.Sum(l => l.Confirmed.Count());
        var pending = visible.Sum(l => l.Pending);
        var month = today.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture);

        IReadOnlyList<StatCard> stats = Access.IsTreasurer
            ?
            [
                new("Total lent out", Format.Zar(lent), "Across " + Format.Plural(visible.Count, "loan"), "wallet"),
                new("Total repaid", Format.Zar(repaid), Format.Plural(payments, "payment") + " received" + (pending > 0 ? " · " + Format.Zar(pending) + " pending" : ""), "down"),
                new("Interest earned this month", Format.Zar(thisMonth), $"From {month} repayments", "coin"),
                new("Outstanding balance", Format.Zar(outstanding), Format.Plural(open, "loan") + " still open", "wallet", Featured: true),
            ]
            :
            [
                new("Total borrowed", Format.Zar(lent), "Across " + Format.Plural(visible.Count, "loan"), "wallet"),
                new("Total repaid", Format.Zar(repaid), Format.Plural(payments, "payment") + " made" + (pending > 0 ? " · " + Format.Zar(pending) + " pending" : ""), "down"),
                new("Interest paid this month", Format.Zar(thisMonth), $"From {month} payments", "coin"),
                new("You still owe", Format.Zar(outstanding), Format.Plural(open, "open loan"), "wallet", Featured: true),
            ];

        var entries = await loans.PaymentsAsync(Access);
        var awaiting = entries.Select(e => PaymentRow.From(Access, e))
            .Where(r => r.Payment.Status is PaymentStatus.Pending or PaymentStatus.Disputed)
            .OrderByDescending(r => r.NeedsMe).Take(3).ToList();

        var (openRequests, recentRequests) = await requests.ListAsync(Access);
        var shownRequests = Access.IsTreasurer ? openRequests : openRequests.Concat(recentRequests).Take(2).ToList();

        var hasMembers = await db.Memberships.AnyAsync(m => m.CircleId == Access.Circle.Id && m.Role != CircleRole.Treasurer);
        return View(new DashboardViewModel
        {
            Stats = stats,
            Overdue = visible.Where(l => l.Status == LoanStatus.Overdue).OrderByDescending(l => l.Days).ToList(),
            DueSoon = visible.Where(l => l.Status == LoanStatus.DueSoon).OrderBy(l => l.Days).ToList(),
            HasLoans = visible.Count > 0,
            HasMembers = hasMembers,
            OwnLoanId = visible.Where(l => l.IsOpen && l.Available > 0 && l.Loan.BorrowerId == Access.Me.Id)
                .OrderBy(l => l.NextDue).Select(l => (int?)l.Loan.Id).FirstOrDefault(),
            Awaiting = awaiting,
            Requests = shownRequests,
            Today = today,
        });
    }
}
