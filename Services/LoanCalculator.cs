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
    int PercentRepaid)
{
    // What the borrower should pay next: one instalment, or less if that clears the loan.
    public decimal NextAmount => Math.Min(Instalment, Balance);
    public bool IsOpen => Balance > 0;
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
        var paid = loan.Repayments.Sum(r => r.Amount);
        var balance = Math.Max(0, Math.Round(total - paid, 2));
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
        var pct = total > 0 ? (int)Math.Min(100, Math.Round(paid / total * 100, MidpointRounding.AwayFromZero)) : 0;
        return new LoanSummary(loan, interest, total, instalment, paid, balance, dues, next, status, days, pct);
    }

    // Share of a repayment that is interest, in proportion to the loan's interest/total.
    public static decimal InterestShare(LoanSummary s, decimal amount) => s.Total > 0 ? amount * s.Interest / s.Total : 0;
}
