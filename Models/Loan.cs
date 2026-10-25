using System.ComponentModel.DataAnnotations;

namespace UbuntuLedger.Models;

public class Loan
{
    public int Id { get; set; }

    // Human reference shown everywhere, e.g. UL-0101.
    [Required, StringLength(16)]
    public string Reference { get; set; } = "";

    public int CircleId { get; set; }
    public Circle Circle { get; set; } = null!;

    public int BorrowerId { get; set; }
    public Person Borrower { get; set; } = null!;

    public decimal Principal { get; set; }
    public decimal RatePercent { get; set; }
    public InterestType InterestType { get; set; }
    public DateOnly StartDate { get; set; }

    // Number of monthly payments.
    public int Term { get; set; }

    // A loan the treasurer records waits here until the borrower has seen the full cost and accepted it. While it waits
    // it owes nothing, takes no payments and counts toward no total. False is the default, so every loan that existed
    // before acceptance was added stays live (those have no AcceptedAt: nobody was asked at the time).
    public bool AwaitingAcceptance { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public int? AcceptedById { get; set; }

    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }

    // Corrections never delete anything. A voided loan stays in the history, marked, and is left out of every total.
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }

    [StringLength(300)]
    public string? VoidReason { get; set; }

    public bool IsVoided => VoidedAt is not null;

    public ICollection<Repayment> Repayments { get; set; } = new List<Repayment>();
}

public class Repayment
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public Loan Loan { get; set; } = null!;
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }

    // Who submitted the payment and when. Submitting is the first of the two confirmations.
    public int RecordedById { get; set; }
    public DateTime RecordedAt { get; set; }
    public PaymentSide SubmittedBy { get; set; }

    // Only Confirmed payments count toward the balance.
    public PaymentStatus Status { get; set; }

    [StringLength(60)]
    public string? Reference { get; set; }

    // Proof of payment: a file kept outside wwwroot and served only to people who can see the loan.
    [StringLength(100)]
    public string? ProofPath { get; set; }

    [StringLength(100)]
    public string? ProofName { get; set; }

    [StringLength(50)]
    public string? ProofContentType { get; set; }

    // The other person's answer: who confirmed, disputed or cancelled it, and when.
    public int? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }

    [StringLength(300)]
    public string? DisputeReason { get; set; }

    // Set when a confirmed payment is reversed. The payment stays in the history with its reason.
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }

    [StringLength(300)]
    public string? VoidReason { get; set; }
}
