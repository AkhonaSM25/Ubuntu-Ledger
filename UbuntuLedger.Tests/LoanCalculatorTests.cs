using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

public class LoanCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static Loan NewLoan(decimal principal, decimal rate, InterestType type, string start, int term, params decimal[] payments)
    {
        var loan = new Loan { Principal = principal, RatePercent = rate, InterestType = type, StartDate = DateOnly.Parse(start), Term = term };
        var d = loan.StartDate;
        foreach (var p in payments) loan.Repayments.Add(new Repayment { Amount = p, Date = d = d.AddMonths(1) });
        return loan;
    }

    [Fact]
    public void Simple_interest_is_one_flat_charge()
    {
        var s = LoanCalculator.Summarize(NewLoan(6000, 10, InterestType.Simple, "2026-05-10", 6), Today);
        Assert.Equal(600m, s.Interest);
        Assert.Equal(6600m, s.Total);
        Assert.Equal(1100m, s.Instalment);
    }

    [Fact]
    public void Monthly_interest_is_charged_every_month_of_the_term()
    {
        var s = LoanCalculator.Summarize(NewLoan(1200, 5, InterestType.Monthly, "2026-07-15", 4), Today);
        Assert.Equal(240m, s.Interest);
        Assert.Equal(360m, s.Instalment);
    }

    [Fact]
    public void Instalments_round_to_cents()
    {
        Assert.Equal(366.67m, LoanCalculator.InstalmentFor(1100, 3));
    }

    [Fact]
    public void Missed_instalment_is_overdue_by_days_since_it_was_due()
    {
        // Three of six paid; the fourth fell due on 10 Sep.
        var s = LoanCalculator.Summarize(NewLoan(6000, 10, InterestType.Simple, "2026-05-10", 6, 1100, 1100, 1100), Today);
        Assert.Equal(LoanStatus.Overdue, s.Status);
        Assert.Equal(new DateOnly(2026, 9, 10), s.NextDue);
        Assert.Equal(35, s.Days);
        Assert.Equal(3300m, s.Balance);
        Assert.Equal(50, s.PercentRepaid);
    }

    [Theory]
    [InlineData("2026-09-15", LoanStatus.DueSoon, 0)]   // due today
    [InlineData("2026-09-22", LoanStatus.DueSoon, 7)]   // 7 days is still "due soon"
    [InlineData("2026-09-23", LoanStatus.UpToDate, 8)]
    public void Status_depends_on_days_until_next_due_date(string start, LoanStatus expected, int days)
    {
        var s = LoanCalculator.Summarize(NewLoan(1000, 10, InterestType.Simple, start, 3), Today);
        Assert.Equal(expected, s.Status);
        Assert.Equal(days, s.Days);
    }

    [Fact]
    public void Fully_repaid_loan_is_paid_with_nothing_due()
    {
        var s = LoanCalculator.Summarize(NewLoan(1500, 10, InterestType.Simple, "2026-06-01", 3, 550, 550, 550), Today);
        Assert.Equal(LoanStatus.Paid, s.Status);
        Assert.Equal(0m, s.Balance);
        Assert.Null(s.NextDue);
        Assert.Equal(100, s.PercentRepaid);
    }

    [Fact]
    public void Due_dates_clamp_to_the_end_of_short_months()
    {
        var dues = LoanCalculator.DueDatesFor(new DateOnly(2026, 1, 31), 3);
        Assert.Equal([new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30)], dues);
    }

    [Fact]
    public void Next_amount_never_exceeds_the_balance()
    {
        var s = LoanCalculator.Summarize(NewLoan(1000, 0, InterestType.Simple, "2026-09-01", 2, 900), Today);
        Assert.Equal(100m, s.NextAmount);
    }

    [Fact]
    public void Rand_amounts_use_non_breaking_thousand_separators()
    {
        Assert.Equal("R 12 500.00", Format.Zar(12500));
        Assert.Equal("−R 1.50", Format.Zar(-1.5m));
    }
}
