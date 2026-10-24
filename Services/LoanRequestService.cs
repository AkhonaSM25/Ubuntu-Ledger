using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

public record NewLoanRequest(decimal Amount, int Term, string? Reason);
public record ApprovalTerms(decimal RatePercent, InterestType Type, DateOnly StartDate);

// Members ask, the treasurer decides. The treasurer records loans directly, so only members make requests.
// Approving a request creates the loan through LoanService, so a loan made this way is exactly like any other.
public class LoanRequestService(LedgerDbContext db, LoanService loans)
{
    public const decimal MaxAmount = 100_000m;

    // How long a decided request stays in the "recent" list.
    private const int RecentDays = 14;

    // Null when the person may make a request now; otherwise the reason they may not.
    public async Task<string?> WhyNotAsync(CircleAccess access)
    {
        if (access.IsTreasurer) return "The treasurer records loans directly. Use New loan.";
        if (await db.LoanRequests.AnyAsync(r => r.CircleId == access.Circle.Id && r.RequesterId == access.Me.Id && r.Status == LoanRequestStatus.Pending))
            return "You already have a request waiting for the treasurer. Cancel it first if you want to change it.";
        var mine = await loans.VisibleAsync(access);
        if (mine.Any(l => l.Status == LoanStatus.Overdue))
            return "You have an overdue loan. Pay it first, then you can ask for another.";
        return null;
    }

    public static Dictionary<string, string> Validate(NewLoanRequest input)
    {
        var errors = new Dictionary<string, string>();
        if (input.Amount <= 0) errors["Amount"] = "Enter an amount more than R 0.00";
        else if (input.Amount > MaxAmount) errors["Amount"] = $"The most you can ask for is {Format.Zar(MaxAmount)}.";
        if (!LoanService.Terms.Contains(input.Term)) errors["Term"] = "Choose how many monthly payments.";
        if (input.Reason is { Length: > 200 }) errors["Reason"] = "Keep the reason under 200 characters.";
        return errors;
    }

    public async Task<OperationResult> SubmitAsync(CircleAccess access, NewLoanRequest input)
    {
        var blocked = await WhyNotAsync(access);
        if (blocked is not null) return OperationResult.Fail(blocked);
        var errors = Validate(input);
        if (errors.Count > 0) return OperationResult.Fail(errors.Values.First());

        var request = new LoanRequest
        {
            CircleId = access.Circle.Id,
            RequesterId = access.Me.Id,
            Amount = Math.Round(input.Amount, 2),
            Term = input.Term,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim(),
            Status = LoanRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        db.LoanRequests.Add(request);
        await db.SaveChangesAsync();
        return OperationResult.Ok(request.Id);
    }

    // The treasurer sees every request in the circle; a member only sees their own.
    public async Task<LoanRequest?> FindAsync(CircleAccess access, int id)
    {
        var r = await db.LoanRequests.Include(x => x.Requester)
            .FirstOrDefaultAsync(x => x.Id == id && x.CircleId == access.Circle.Id);
        return r is not null && (access.IsTreasurer || r.RequesterId == access.Me.Id) ? r : null;
    }

    // Waiting requests first (oldest first, so nobody is forgotten), then ones decided in the last two weeks.
    public async Task<(List<LoanRequest> Open, List<LoanRequest> Recent)> ListAsync(CircleAccess access)
    {
        var q = db.LoanRequests.Where(r => r.CircleId == access.Circle.Id);
        if (!access.IsTreasurer) q = q.Where(r => r.RequesterId == access.Me.Id);
        var since = DateTime.UtcNow.AddDays(-RecentDays);
        var rows = await q.Where(r => r.Status == LoanRequestStatus.Pending || (r.DecidedAt != null && r.DecidedAt >= since) || r.CreatedAt >= since)
            .Include(r => r.Requester).AsNoTracking().ToListAsync();
        var open = rows.Where(r => r.Status == LoanRequestStatus.Pending).OrderBy(r => r.CreatedAt).ToList();
        var recent = rows.Where(r => r.Status != LoanRequestStatus.Pending).OrderByDescending(r => r.DecidedAt ?? r.CreatedAt).ToList();
        return (open, recent);
    }

    public async Task<OperationResult> ApproveAsync(CircleAccess access, int id, ApprovalTerms terms)
    {
        if (!access.IsTreasurer) return OperationResult.Fail("Only the treasurer can approve a loan request.");
        var request = await FindAsync(access, id);
        if (request is null) return OperationResult.Fail("That request was not found.");
        if (request.Status != LoanRequestStatus.Pending) return OperationResult.Fail("This request has already been answered.");

        var input = new NewLoan(request.RequesterId, request.Amount, terms.RatePercent, terms.Type, terms.StartDate, request.Term);
        var errors = await loans.ValidateAsync(access, input);
        if (errors.Count > 0) return OperationResult.Fail(errors.Values.First());

        // Mark the request first: creating the loan saves in the same context, so both are stored together.
        request.Status = LoanRequestStatus.Approved;
        request.DecidedById = access.Me.Id;
        request.DecidedAt = DateTime.UtcNow;
        var created = await loans.CreateAsync(access, input);
        if (!created.Succeeded) return OperationResult.Fail(created.Error!);

        request.LoanId = created.Id;
        await db.SaveChangesAsync();
        return OperationResult.Ok(created.Id);
    }

    public async Task<OperationResult> DeclineAsync(CircleAccess access, int id, string? reason)
    {
        if (!access.IsTreasurer) return OperationResult.Fail("Only the treasurer can decline a loan request.");
        if (reason is { Length: > 200 }) return OperationResult.Fail("Keep the reason under 200 characters.");
        var request = await FindAsync(access, id);
        if (request is null) return OperationResult.Fail("That request was not found.");
        if (request.Status != LoanRequestStatus.Pending) return OperationResult.Fail("This request has already been answered.");

        request.Status = LoanRequestStatus.Declined;
        request.DecidedById = access.Me.Id;
        request.DecidedAt = DateTime.UtcNow;
        request.DeclineReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await db.SaveChangesAsync();
        return OperationResult.Ok(request.Id);
    }

    public async Task<OperationResult> CancelAsync(CircleAccess access, int id)
    {
        var request = await FindAsync(access, id);
        if (request is null || request.RequesterId != access.Me.Id) return OperationResult.Fail("Only the person who asked can cancel a request.");
        if (request.Status != LoanRequestStatus.Pending) return OperationResult.Fail("This request has already been answered.");

        request.Status = LoanRequestStatus.Cancelled;
        request.DecidedById = access.Me.Id;
        request.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return OperationResult.Ok(request.Id);
    }
}
