using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

// Everything derived from a loan and its repayments on a given day.
public record LoanSummary(
    Loan Loan,
    decimal Interest,
    decimal Total,
    decimal Instalment,
    decimal Paid,
    decimal Balance,
    IReadOnlyList<DateOnly> DueDates,
    DateOnly? NextDue,
    LoanStatus Status,
    int Days,
    int PercentRepaid,
    decimal Pending)
{
    // Payments both people have agreed on. Everything else (pending, disputed, cancelled) leaves the balance alone.
    public IEnumerable<Repayment> Confirmed => Loan.Repayments.Where(r => r.Status == PaymentStatus.Confirmed);

    // What can still be submitted: the balance less payments already waiting for confirmation.
    public decimal Available => Loan.IsVoided ? 0 : Math.Max(0, Balance - Pending);

    // What the borrower should pay next: one instalment, or less if that clears the loan.
    public decimal NextAmount => Math.Min(Instalment, Balance);
    public bool IsOpen => Balance > 0 && !Loan.IsVoided;
    public bool IsVoided => Loan.IsVoided;
}

public static class LoanCalculator
{
    public const int DueSoonDays = 7;

    public static decimal InterestFor(decimal principal, decimal ratePercent, InterestType type, int term) =>
        type == InterestType.Simple ? principal * ratePercent / 100m : principal * ratePercent / 100m * term;

    public static decimal InstalmentFor(decimal total, int term) => Math.Round(total / Math.Max(term, 1), 2, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<DateOnly> DueDatesFor(DateOnly start, int term) =>
        Enumerable.Range(1, term).Select(start.AddMonths).ToList();

    public static LoanSummary Summarize(Loan loan, DateOnly today)
    {
        var interest = InterestFor(loan.Principal, loan.RatePercent, loan.InterestType, loan.Term);
        var total = loan.Principal + interest;
        var instalment = InstalmentFor(total, loan.Term);
        var paid = loan.Repayments.Where(r => r.Status == PaymentStatus.Confirmed).Sum(r => r.Amount);
        var pending = loan.Repayments.Where(r => r.Status == PaymentStatus.Pending).Sum(r => r.Amount);
        // A voided loan owes nothing; it stays on record, marked as voided.
        var balance = loan.IsVoided ? 0 : Math.Max(0, Math.Round(total - paid, 2));
        var dues = DueDatesFor(loan.StartDate, loan.Term);

        // Payments cover instalments in order; the first uncovered one is the next due date.
        var covered = instalment > 0 ? (int)Math.Floor((paid + 0.005m) / instalment) : 0;
        var status = LoanStatus.Paid;
        DateOnly? next = null;
        var days = 0;
        if (balance > 0)
        {
            next = dues[Math.Min(covered, loan.Term - 1)];
            var diff = next.Value.DayNumber - today.DayNumber;
            (status, days) = diff switch
            {
                < 0 => (LoanStatus.Overdue, -diff),
                <= DueSoonDays => (LoanStatus.DueSoon, diff),
                _ => (LoanStatus.UpToDate, diff),
            };
        }
        if (loan.IsVoided) status = LoanStatus.Voided;
        var pct = total > 0 ? (int)Math.Min(100, Math.Round(paid / total * 100, MidpointRounding.AwayFromZero)) : 0;
        return new LoanSummary(loan, interest, total, instalment, paid, balance, dues, next, status, days, pct, pending);
    }

    // Share of a repayment that is interest, in proportion to the loan's interest/total.
    public static decimal InterestShare(LoanSummary s, decimal amount) => s.Total > 0 ? amount * s.Interest / s.Total : 0;
}
