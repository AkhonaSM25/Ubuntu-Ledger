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

    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }

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
}
