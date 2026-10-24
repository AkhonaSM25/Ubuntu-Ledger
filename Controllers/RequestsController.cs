using Microsoft.AspNetCore.Mvc;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

// Loan requests. A member asks for a loan; the treasurer approves it (which creates the loan) or declines it.
[Route("circles/{circleId:int}/requests")]
public class RequestsController(CircleAccessService access, LoanRequestService requests, LoanService loans, LedgerClock clock) : CircleControllerBase(access)
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var (open, recent) = await requests.ListAsync(Access);
        return View(new RequestListViewModel { Open = open, Recent = recent, BlockedMessage = await requests.WhyNotAsync(Access) });
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create()
    {
        var blocked = await requests.WhyNotAsync(Access);
        if (blocked is not null)
        {
            Toast(blocked);
            return RedirectToAction(nameof(Index), new { circleId = Access.Circle.Id });
        }
        return View(new NewLoanRequestViewModel());
    }

    [HttpPost("new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NewLoanRequestViewModel model)
    {
        var input = new NewLoanRequest(model.Amount ?? 0, model.Term, model.Reason);
        foreach (var (key, error) in LoanRequestService.Validate(input))
            ModelState.AddModelError(key, error);
        if (!ModelState.IsValid) return View(model);

        var result = await requests.SubmitAsync(Access, input);
        if (!result.Succeeded)
        {
            Toast(result.Error!);
            return RedirectToAction(nameof(Index), new { circleId = Access.Circle.Id });
        }
        Toast("Request sent. The treasurer will answer it here.");
        return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id = result.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var request = await requests.FindAsync(Access, id);
        if (request is null) return NotFound();
        var pending = request.Status == LoanRequestStatus.Pending;

        var model = new RequestDetailViewModel
        {
            Request = request,
            CanDecide = pending && Access.IsTreasurer,
            CanCancel = pending && request.RequesterId == Access.Me.Id,
            Terms = new NewLoanViewModel { Principal = request.Amount, Term = request.Term, StartDate = clock.Today, BorrowerId = request.RequesterId },
        };
        if (Access.IsTreasurer)
        {
            var theirs = (await loans.VisibleAsync(Access)).Where(l => l.Loan.BorrowerId == request.RequesterId && l.IsOpen).ToList();
            model = new RequestDetailViewModel
            {
                Request = model.Request, CanDecide = model.CanDecide, CanCancel = model.CanCancel, Terms = model.Terms,
                OpenLoans = theirs.Count,
                OwedAlready = theirs.Sum(l => l.Balance),
                OverdueLoans = theirs.Count(l => l.Status == LoanStatus.Overdue),
            };
        }
        return View(model);
    }

    [HttpPost("{id:int}/approve"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, decimal? ratePercent, InterestType type, DateOnly? startDate)
    {
        var result = await requests.ApproveAsync(Access, id, new ApprovalTerms(ratePercent ?? 0, type, startDate ?? clock.Today));
        if (!result.Succeeded)
        {
            Toast(result.Error!);
            return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id });
        }
        Toast("Request approved. The loan has been created.");
        return RedirectToAction("Details", "Loans", new { circleId = Access.Circle.Id, id = result.Id });
    }

    [HttpPost("{id:int}/decline"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(int id, string? reason)
    {
        var result = await requests.DeclineAsync(Access, id, reason);
        Toast(result.Succeeded ? "Request declined." : result.Error!);
        return RedirectToAction(result.Succeeded ? nameof(Index) : nameof(Details), new { circleId = Access.Circle.Id, id });
    }

    [HttpPost("{id:int}/cancel"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var result = await requests.CancelAsync(Access, id);
        Toast(result.Succeeded ? "Request cancelled." : result.Error!);
        return RedirectToAction(nameof(Index), new { circleId = Access.Circle.Id });
    }
}
