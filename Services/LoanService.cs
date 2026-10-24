using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

public record NewLoan(int BorrowerId, decimal Principal, decimal RatePercent, InterestType Type, DateOnly StartDate, int Term);
public record NewRepayment(int LoanId, decimal Amount, DateOnly Date, PaymentMethod Method, string? Note);

// Loans and repayments. Only a circle's treasurer can record either.
public class LoanService(LedgerDbContext db, LedgerClock clock)
{
    public static readonly int[] Terms = [2, 3, 4, 5, 6, 8, 10, 12];

    // Loans the person may see in this circle, worked out as of today.
    public async Task<List<LoanSummary>> VisibleAsync(CircleAccess access)
    {
        var q = db.Loans.Where(l => l.CircleId == access.Circle.Id);
        if (!access.IsTreasurer) q = q.Where(l => l.BorrowerId == access.Me.Id);
        var loans = await q.Include(l => l.Borrower).Include(l => l.Repayments).AsNoTracking().ToListAsync();
        return loans.Select(l => LoanCalculator.Summarize(l, clock.Today)).ToList();
    }

    public async Task<LoanSummary?> FindAsync(CircleAccess access, int loanId)
    {
        var loan = await db.Loans.Include(l => l.Borrower).Include(l => l.Repayments).AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == loanId && l.CircleId == access.Circle.Id);
        return loan is not null && access.CanSee(loan) ? LoanCalculator.Summarize(loan, clock.Today) : null;
    }

    public Task<List<Person>> BorrowersAsync(int circleId) =>
        db.Memberships.Where(m => m.CircleId == circleId && m.Role == CircleRole.Member)
            .Select(m => m.Person).OrderBy(p => p.Name).ToListAsync();

    public async Task<Dictionary<string, string>> ValidateAsync(CircleAccess access, NewLoan input)
    {
        var errors = new Dictionary<string, string>();
        if (!access.IsTreasurer) errors[""] = "Only the treasurer can record loans.";
        if (!await db.Memberships.AnyAsync(m => m.CircleId == access.Circle.Id && m.PersonId == input.BorrowerId && m.Role == CircleRole.Member))
            errors["BorrowerId"] = "Choose a member of this circle.";
        if (input.Principal <= 0) errors["Principal"] = "Enter an amount more than R 0.00";
        if (input.RatePercent < 0 || input.RatePercent > 100) errors["RatePercent"] = "Enter a rate between 0% and 100%.";
        if (!Terms.Contains(input.Term)) errors["Term"] = "Choose how many monthly payments.";
        return errors;
    }

    public async Task<OperationResult> CreateAsync(CircleAccess access, NewLoan input)
    {
        var errors = await ValidateAsync(access, input);
        if (errors.Count > 0) return OperationResult.Fail(errors.Values.First());

        var loan = new Loan
        {
            Reference = await NextReferenceAsync(),
            CircleId = access.Circle.Id,
            BorrowerId = input.BorrowerId,
            Principal = Math.Round(input.Principal, 2),
            RatePercent = input.RatePercent,
            InterestType = input.Type,
            StartDate = input.StartDate,
            Term = input.Term,
            CreatedById = access.Me.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        return OperationResult.Ok(loan.Id);
    }

    // Returns an error message, or null when the repayment can be recorded.
    public static string? ValidateRepayment(LoanSummary loan, decimal amount) =>
        amount <= 0 ? "Enter the amount you received."
        : amount > loan.Balance ? $"That is more than the {Format.Zar(loan.Balance)} still owing."
        : null;

    public async Task<OperationResult> RecordRepaymentAsync(CircleAccess access, NewRepayment input)
    {
        if (!access.IsTreasurer) return OperationResult.Fail("Only the treasurer can record repayments.");
        var loan = await FindAsync(access, input.LoanId);
        if (loan is null) return OperationResult.Fail("That loan was not found.");
        var error = ValidateRepayment(loan, input.Amount);
        if (error is not null) return OperationResult.Fail(error);

        db.Repayments.Add(new Repayment
        {
            LoanId = loan.Loan.Id,
            Amount = Math.Round(input.Amount, 2),
            Date = input.Date,
            Method = input.Method,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            RecordedById = access.Me.Id,
            RecordedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return OperationResult.Ok(loan.Loan.Id);
    }

    private async Task<string> NextReferenceAsync()
    {
        var refs = await db.Loans.Select(l => l.Reference).ToListAsync();
        var max = refs.Select(r => int.TryParse(r.Replace("UL-", ""), out var n) ? n : 0).DefaultIfEmpty(100).Max();
        return $"UL-{max + 1:0000}";
    }
}
