using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.ViewModels;

public class DashboardViewModel
{
    public required IReadOnlyList<StatCard> Stats { get; init; }
    public required IReadOnlyList<LoanSummary> Overdue { get; init; }
    public required IReadOnlyList<LoanSummary> DueSoon { get; init; }
    public required bool HasLoans { get; init; }
    public required bool HasMembers { get; init; }
    // The signed-in person's own loan with something left to pay (earliest due first), for the "I made a payment" button.
    public int? OwnLoanId { get; init; }
    public required IReadOnlyList<PaymentRow> Awaiting { get; init; }
    public required DateOnly Today { get; init; }
}

public class MembersViewModel
{
    public required IReadOnlyList<MemberSummary> Rows { get; init; }
    public required IReadOnlyList<Invitation> Pending { get; init; }
    public required IReadOnlyDictionary<string, int> Counts { get; init; }
    public required int MemberCount { get; init; }
    public required int InvitedCount { get; init; }
    public string? Query { get; init; }
    public string Filter { get; init; } = "all";
}

public record TimelineEntry(DateOnly Date, bool IsPayment, decimal Amount, string Title, string Sub, decimal Balance);

public class MemberDetailViewModel
{
    public required MemberSummary Member { get; init; }
    public required DateOnly MemberSince { get; init; }
    public required IReadOnlyList<StatCard> Stats { get; init; }
    public required IReadOnlyList<TimelineEntry> Timeline { get; init; }
}

public class LoansViewModel
{
    public required IReadOnlyList<LoanSummary> Loans { get; init; }
}

public record LedgerRow(DateOnly Date, string Title, string Sub, decimal Amount, bool IsCredit, decimal Balance);

public class LoanDetailViewModel
{
    public required LoanSummary Summary { get; init; }
    public required IReadOnlyList<LedgerRow> Rows { get; init; }
    public required DateOnly Today { get; init; }
}

public class NewLoanViewModel
{
    public int BorrowerId { get; set; }
    public decimal? Principal { get; set; } = 3000;
    public decimal? RatePercent { get; set; } = 10;
    public InterestType Type { get; set; } = InterestType.Simple;
    public DateOnly StartDate { get; set; }
    public int Term { get; set; } = 6;

    public IReadOnlyList<Person> Borrowers { get; set; } = [];
    public bool Confirming { get; set; }

    public decimal Interest => LoanCalculator.InterestFor(Principal ?? 0, RatePercent ?? 0, Type, Term);
    public decimal Total => (Principal ?? 0) + Interest;
    public decimal Instalment => LoanCalculator.InstalmentFor(Total, Term);
    public IReadOnlyList<DateOnly> DueDates => LoanCalculator.DueDatesFor(StartDate, Term);
    public string BorrowerName => Borrowers.FirstOrDefault(b => b.Id == BorrowerId)?.Name ?? "";
}

public record ReportMonth(DateOnly Month, decimal Lent, decimal Repaid, decimal Interest);

public class ReportsViewModel
{
    public required int Period { get; init; }
    public required IReadOnlyList<ReportMonth> Months { get; init; }
    public required IReadOnlyList<StatCard> Stats { get; init; }
    public required decimal AxisStep { get; init; }
    public string Range => $"{Format.MonthYear(Months[0].Month)} to {Format.MonthYear(Months[^1].Month)}";
    public decimal AxisTop => AxisStep * 4;
}
