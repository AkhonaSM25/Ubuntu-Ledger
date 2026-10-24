using System.ComponentModel.DataAnnotations;

namespace UbuntuLedger.Models;

public enum LoanRequestStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2,
    Cancelled = 3
}

// A member asking the treasurer for a loan. The member chooses the amount and the number of monthly payments;
// the treasurer sets the interest when approving, and approving is what creates the Loan.
public class LoanRequest
{
    public int Id { get; set; }

    public int CircleId { get; set; }
    public Circle Circle { get; set; } = null!;

    public int RequesterId { get; set; }
    public Person Requester { get; set; } = null!;

    public decimal Amount { get; set; }
    public int Term { get; set; }

    [StringLength(200)]
    public string? Reason { get; set; }

    public LoanRequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    // The treasurer's answer.
    public int? DecidedById { get; set; }
    public DateTime? DecidedAt { get; set; }

    [StringLength(200)]
    public string? DeclineReason { get; set; }

    // The loan that was created when the request was approved.
    public int? LoanId { get; set; }
}
