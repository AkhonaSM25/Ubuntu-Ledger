using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.ViewModels;

// One payment as the signed-in person should read it: who says what, and whose move it is.
public class PaymentRow
{
    public required Repayment Payment { get; init; }
    public required Loan Loan { get; init; }
    public required string Headline { get; init; }
    public required string Detail { get; init; }
    public required string Chip { get; init; }
    public string? ChipLabel { get; init; }
    public required bool NeedsMe { get; init; }
    public required bool IsMine { get; init; }

    public static PaymentRow From(CircleAccess access, PaymentEntry entry)
    {
        var p = entry.Payment;
        var loan = entry.Loan;
        var mine = LoanService.IsSubmitter(access, loan.BorrowerId, p);
        var borrower = loan.Borrower.FirstName;
        var amount = Format.Zar(p.Amount);

        var headline = (p.SubmittedBy, mine) switch
        {
            (PaymentSide.Payer, true) => $"You say you paid {amount}",
            (PaymentSide.Payer, false) => $"{borrower} says they paid {amount}",
            (PaymentSide.Collector, true) => $"{amount} received from {borrower}",
            _ => $"{FirstName(entry.SubmittedByName)} says they received {amount} from you",
        };

        var detail = $"{loan.Reference} · {p.Method} · {Format.Date(p.Date)}"
                     + (string.IsNullOrEmpty(p.Reference) ? "" : " · Ref " + p.Reference)
                     + (p.ProofPath is null ? "" : " · Proof attached");
        if (p.Status == PaymentStatus.Disputed && !string.IsNullOrEmpty(p.DisputeReason)) detail += " · " + p.DisputeReason;

        var needsMe = LoanService.NeedsMyAction(access, loan.BorrowerId, p);
        var (chip, label) = p.Status switch
        {
            PaymentStatus.Disputed => ("disputed", (string?)null),
            PaymentStatus.Confirmed => ("paid", "Confirmed"),
            _ when needsMe => ("awaiting", (string?)null),
            _ => ("waiting", "Awaiting " + (p.SubmittedBy == PaymentSide.Payer ? (loan.BorrowerId == access.TreasurerId ? "a member" : "treasurer") : borrower)),
        };

        return new PaymentRow
        {
            Payment = p, Loan = loan, Headline = headline, Detail = detail,
            Chip = chip, ChipLabel = label, NeedsMe = needsMe, IsMine = mine,
        };
    }

    private static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
}

public record PaymentNotice(string Title, string Text, string Tone, int PaymentId);

public class ConfirmationsViewModel
{
    public required IReadOnlyList<PaymentRow> Mine { get; init; }
    public required IReadOnlyList<PaymentRow> Others { get; init; }
    public required IReadOnlyList<PaymentNotice> Notices { get; init; }
    public required string Tab { get; init; }

    // Things worth telling the person about: payments waiting on them, and their own that were just confirmed.
    public static IReadOnlyList<PaymentNotice> BuildNotices(IEnumerable<PaymentRow> rows) =>
        rows.Where(r => r.NeedsMe || (r.IsMine && r.Payment.Status == PaymentStatus.Confirmed))
            .Select(r => r.Payment.Status switch
            {
                PaymentStatus.Confirmed => new PaymentNotice(
                    r.Payment.SubmittedBy == PaymentSide.Payer ? "Your payment was confirmed" : "The payment you recorded was confirmed",
                    $"{Format.Zar(r.Payment.Amount)} now counts toward loan {r.Loan.Reference}.", "ok", r.Payment.Id),
                PaymentStatus.Disputed => new PaymentNotice(r.Headline,
                    "It was disputed. Fix it by cancelling and submitting again.", "bad", r.Payment.Id),
                _ => new PaymentNotice(r.Headline,
                    r.Payment.SubmittedBy == PaymentSide.Payer ? "Check the proof and confirm you received it." : "Check the amount and confirm you paid it.",
                    "warn", r.Payment.Id),
            })
            .Take(5).ToList();
}

public class PaymentFormViewModel
{
    public int LoanId { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly Date { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.EFT;

    [System.ComponentModel.DataAnnotations.StringLength(60, ErrorMessage = "The reference can be at most 60 characters.")]
    public string? Reference { get; set; }

    public IFormFile? Proof { get; set; }

    public IReadOnlyList<LoanSummary> OpenLoans { get; set; } = [];
    public LoanSummary? Selected { get; set; }
    public string? Error { get; set; }
}

public class PaymentDetailViewModel
{
    public required PaymentRow Row { get; init; }
    // Null for a member confirming one of the treasurer's payments: they see the payment, never the loan's balance.
    public LoanSummary? Summary { get; init; }
    public required bool CanConfirm { get; init; }
    public required bool CanDispute { get; init; }
    public required bool CanCancel { get; init; }
}
