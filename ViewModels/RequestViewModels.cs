using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.ViewModels;

public class NewLoanRequestViewModel
{
    public decimal? Amount { get; set; }
    public int Term { get; set; } = 4;
    public string? Reason { get; set; }
}

public class RequestListViewModel
{
    public required IReadOnlyList<LoanRequest> Open { get; init; }
    public required IReadOnlyList<LoanRequest> Recent { get; init; }
    // Why the signed-in person cannot ask for a loan right now; null when they can (and always null for the treasurer).
    public string? BlockedMessage { get; init; }
}

public class RequestDetailViewModel
{
    public required LoanRequest Request { get; init; }
    public required bool CanDecide { get; init; }
    public required bool CanCancel { get; init; }
    // What the treasurer needs to know before answering: what the member already owes and whether they are overdue.
    public decimal OwedAlready { get; init; }
    public int OverdueLoans { get; init; }
    public int OpenLoans { get; init; }
    // Starting values for the approval form, and a preview of what they would mean.
    public NewLoanViewModel Terms { get; init; } = new();
}

public static class RequestUi
{
    public static (string Kind, string Label) Chip(LoanRequestStatus s) => s switch
    {
        LoanRequestStatus.Approved => ("ok", "Approved"),
        LoanRequestStatus.Declined => ("overdue", "Declined"),
        LoanRequestStatus.Cancelled => ("waiting", "Cancelled"),
        _ => ("waiting", "Waiting"),
    };
}
