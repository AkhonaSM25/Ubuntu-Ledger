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

    public int RecordedById { get; set; }
    public DateTime RecordedAt { get; set; }
}
