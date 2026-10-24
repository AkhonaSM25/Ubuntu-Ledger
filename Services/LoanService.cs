using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

public record NewLoan(int BorrowerId, decimal Principal, decimal RatePercent, InterestType Type, DateOnly StartDate, int Term);
public record NewPayment(int LoanId, decimal Amount, DateOnly Date, PaymentMethod Method, string? Reference, StoredProof? Proof);

// A payment together with its loan and the name of whoever submitted it.
public record PaymentEntry(Repayment Payment, Loan Loan, string SubmittedByName);

// What the sidebar and nav badge need: payments waiting on me, what is owed, and how much of that is pending.
public record PaymentSnapshot(int NeedsMyAction, decimal Outstanding, decimal Pending);

// Loans and payments. Only the treasurer records loans. A payment is submitted by one side (the borrower says they
// paid, or the treasurer says they received) and only counts toward the balance once the other side confirms it.
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

    // The loan's figures with no permission check. Only for use after the caller has already been authorised.
    private async Task<LoanSummary?> SummaryOfAsync(int loanId)
    {
        var loan = await db.Loans.Include(l => l.Borrower).Include(l => l.Repayments).AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == loanId);
        return loan is null ? null : LoanCalculator.Summarize(loan, clock.Today);
    }

    public Task<List<Person>> BorrowersAsync(int circleId) =>
        db.Memberships.Where(m => m.CircleId == circleId)
            .Select(m => m.Person).OrderBy(p => p.Name).ToListAsync();

    public async Task<Dictionary<string, string>> ValidateAsync(CircleAccess access, NewLoan input)
    {
        var errors = new Dictionary<string, string>();
        if (!access.IsTreasurer) errors[""] = "Only the treasurer can record loans.";
        if (!await db.Memberships.AnyAsync(m => m.CircleId == access.Circle.Id && m.PersonId == input.BorrowerId))
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

    // ---- Two-party payments ----

    // Who is who on a payment. The payer is always the borrower. The collector is the treasurer, except on the
    // treasurer's own loans, where nobody can confirm their own payment, so any other member stands in.
    public static bool IsPayer(CircleAccess access, int borrowerId) => access.Me.Id == borrowerId;

    public static bool IsCollector(CircleAccess access, int borrowerId) =>
        borrowerId == access.TreasurerId ? access.Me.Id != borrowerId : access.IsTreasurer;

    // The side that did not submit a payment is the one that confirms it.
    public static bool IsReviewer(CircleAccess access, int borrowerId, PaymentSide submittedBy) =>
        submittedBy == PaymentSide.Payer ? IsCollector(access, borrowerId) : IsPayer(access, borrowerId);

    public static bool IsSubmitter(CircleAccess access, int borrowerId, PaymentSide submittedBy) =>
        submittedBy == PaymentSide.Payer ? IsPayer(access, borrowerId) : IsCollector(access, borrowerId);

    public static bool IsReviewer(CircleAccess access, int borrowerId, Repayment p) => IsReviewer(access, borrowerId, p.SubmittedBy);
    public static bool IsSubmitter(CircleAccess access, int borrowerId, Repayment p) => IsSubmitter(access, borrowerId, p.SubmittedBy);

    // Pending payments wait on the other side; disputed ones wait on whoever submitted them to fix or cancel.
    public static bool NeedsMyAction(CircleAccess access, int borrowerId, PaymentSide submittedBy, PaymentStatus status) =>
        status == PaymentStatus.Pending ? IsReviewer(access, borrowerId, submittedBy)
        : status == PaymentStatus.Disputed && IsSubmitter(access, borrowerId, submittedBy);

    public static bool NeedsMyAction(CircleAccess access, int borrowerId, Repayment p) =>
        NeedsMyAction(access, borrowerId, p.SubmittedBy, p.Status);

    // Returns an error message, or null when the payment can be submitted.
    public static string? ValidatePayment(LoanSummary loan, decimal amount, DateOnly date, DateOnly today, string? reference)
    {
        if (amount <= 0) return "Enter the amount.";
        if (amount > loan.Available)
            return loan.Pending > 0
                ? $"That is more than the {Format.Zar(loan.Available)} still owing once the {Format.Zar(loan.Pending)} waiting for confirmation is counted."
                : $"That is more than the {Format.Zar(loan.Balance)} still owing.";
        if (date > today) return "The payment date cannot be in the future.";
        if (date < loan.Loan.StartDate) return "The payment date cannot be before the loan started.";
        if (reference is { Length: > 60 }) return "The reference can be at most 60 characters.";
        return null;
    }

    // Step one: one side says a payment happened. The balance does not change until the other side confirms.
    public async Task<OperationResult> SubmitPaymentAsync(CircleAccess access, NewPayment input)
    {
        var loan = await FindAsync(access, input.LoanId);
        if (loan is null) return OperationResult.Fail("That loan was not found.");
        if (!loan.IsOpen) return OperationResult.Fail("That loan is already paid off.");
        var error = ValidatePayment(loan, input.Amount, input.Date, clock.Today, input.Reference);
        if (error is not null) return OperationResult.Fail(error);

        var payment = new Repayment
        {
            LoanId = loan.Loan.Id,
            Amount = Math.Round(input.Amount, 2),
            Date = input.Date,
            Method = input.Method,
            Reference = string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim(),
            ProofPath = input.Proof?.Path,
            ProofName = input.Proof?.FileName,
            ProofContentType = input.Proof?.ContentType,
            SubmittedBy = loan.Loan.BorrowerId == access.Me.Id ? PaymentSide.Payer : PaymentSide.Collector,
            Status = PaymentStatus.Pending,
            RecordedById = access.Me.Id,
            RecordedAt = DateTime.UtcNow,
        };
        db.Repayments.Add(payment);
        await db.SaveChangesAsync();
        return OperationResult.Ok(payment.Id);
    }

    // Step two: the other side agrees. Only now does the payment reduce the balance.
    public async Task<OperationResult> ConfirmPaymentAsync(CircleAccess access, int paymentId)
    {
        var p = await LoadPaymentAsync(access, paymentId);
        if (p is null) return OperationResult.Fail("That payment was not found.");
        if (!IsReviewer(access, p.Loan.BorrowerId, p)) return OperationResult.Fail("Only the other person can confirm this payment.");
        if (p.Status is not (PaymentStatus.Pending or PaymentStatus.Disputed))
            return OperationResult.Fail("This payment is no longer waiting for confirmation.");

        var loan = await SummaryOfAsync(p.LoanId);
        if (loan is null || p.Amount > loan.Balance)
            return OperationResult.Fail("This is more than the balance still owing. Ask for it to be cancelled and submitted again.");

        p.Status = PaymentStatus.Confirmed;
        p.ReviewedById = access.Me.Id;
        p.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return OperationResult.Ok(p.LoanId);
    }

    // The other side says it does not match. The balance stays as it is until it is fixed or cancelled.
    public async Task<OperationResult> DisputePaymentAsync(CircleAccess access, int paymentId, string? reason)
    {
        reason = reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return OperationResult.Fail("Say what does not match so it can be fixed.");
        if (reason.Length > 300) return OperationResult.Fail("Keep the reason under 300 characters.");

        var p = await LoadPaymentAsync(access, paymentId);
        if (p is null) return OperationResult.Fail("That payment was not found.");
        if (!IsReviewer(access, p.Loan.BorrowerId, p)) return OperationResult.Fail("Only the other person can dispute this payment.");
        if (p.Status != PaymentStatus.Pending) return OperationResult.Fail("This payment is no longer waiting for confirmation.");

        p.Status = PaymentStatus.Disputed;
        p.DisputeReason = reason;
        p.ReviewedById = access.Me.Id;
        p.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return OperationResult.Ok(p.LoanId);
    }

    // Whoever submitted a payment can withdraw it until it is confirmed (for example to resubmit the right amount).
    public async Task<OperationResult> CancelPaymentAsync(CircleAccess access, int paymentId)
    {
        var p = await LoadPaymentAsync(access, paymentId);
        if (p is null) return OperationResult.Fail("That payment was not found.");
        if (!IsSubmitter(access, p.Loan.BorrowerId, p)) return OperationResult.Fail("Only the person who submitted this payment can cancel it.");
        if (p.Status is not (PaymentStatus.Pending or PaymentStatus.Disputed))
            return OperationResult.Fail("This payment can no longer be cancelled.");

        p.Status = PaymentStatus.Cancelled;
        p.ReviewedById = access.Me.Id;
        p.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return OperationResult.Ok(p.LoanId);
    }

    // The payment, if it belongs to this circle and the person may see it: the borrower and treasurer, or a member
    // asked to confirm one of the treasurer's own payments.
    public async Task<Repayment?> LoadPaymentAsync(CircleAccess access, int paymentId)
    {
        var p = await db.Repayments.Include(r => r.Loan).ThenInclude(l => l.Borrower)
            .FirstOrDefaultAsync(r => r.Id == paymentId && r.Loan.CircleId == access.Circle.Id);
        return p is not null && access.CanSeePayment(p.Loan) ? p : null;
    }

    // Payments that are waiting or disputed, plus ones confirmed in the last two weeks (for notifications), newest first.
    public async Task<List<PaymentEntry>> PaymentsAsync(CircleAccess access)
    {
        var since = DateTime.UtcNow.AddDays(-14);
        var q = db.Repayments.Where(r => r.Loan.CircleId == access.Circle.Id);
        if (!access.IsTreasurer) q = q.Where(r => r.Loan.BorrowerId == access.Me.Id || r.Loan.BorrowerId == access.TreasurerId);
        var rows = await q
            .Where(r => r.Status == PaymentStatus.Pending || r.Status == PaymentStatus.Disputed
                        || (r.Status == PaymentStatus.Confirmed && r.ReviewedAt != null && r.ReviewedAt >= since))
            .Include(r => r.Loan).ThenInclude(l => l.Borrower)
            .OrderByDescending(r => r.RecordedAt)
            .AsNoTracking().ToListAsync();

        var ids = rows.Select(r => r.RecordedById).Distinct().ToList();
        var names = await db.People.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        return rows.Select(r => new PaymentEntry(r, r.Loan, names.GetValueOrDefault(r.RecordedById, "Someone"))).ToList();
    }

    public async Task<PaymentEntry?> PaymentEntryAsync(CircleAccess access, int paymentId)
    {
        var p = await LoadPaymentAsync(access, paymentId);
        if (p is null) return null;
        var name = (await db.People.FindAsync(p.RecordedById))?.Name ?? "Someone";
        return new PaymentEntry(p, p.Loan, name);
    }

    public async Task<PaymentSnapshot> SnapshotAsync(CircleAccess access)
    {
        var visible = await VisibleAsync(access);

        var waiting = db.Repayments.Where(r => r.Loan.CircleId == access.Circle.Id
                                               && (r.Status == PaymentStatus.Pending || r.Status == PaymentStatus.Disputed));
        if (!access.IsTreasurer) waiting = waiting.Where(r => r.Loan.BorrowerId == access.Me.Id || r.Loan.BorrowerId == access.TreasurerId);
        var rows = await waiting.Select(r => new { r.SubmittedBy, r.Status, r.Loan.BorrowerId }).ToListAsync();
        var needs = rows.Count(r => NeedsMyAction(access, r.BorrowerId, r.SubmittedBy, r.Status));

        return new PaymentSnapshot(needs, visible.Sum(l => l.Balance), visible.Sum(l => l.Pending));
    }

    private async Task<string> NextReferenceAsync()
    {
        var refs = await db.Loans.Select(l => l.Reference).ToListAsync();
        var max = refs.Select(r => int.TryParse(r.Replace("UL-", ""), out var n) ? n : 0).DefaultIfEmpty(100).Max();
        return $"UL-{max + 1:0000}";
    }
}
