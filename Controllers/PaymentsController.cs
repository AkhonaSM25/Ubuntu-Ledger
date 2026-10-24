using Microsoft.AspNetCore.Mvc;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

// Two-party payments. The borrower can say "I made a payment" and the treasurer can say "I received a payment";
// either way the other person has to confirm before the balance changes.
[Route("circles/{circleId:int}/payments")]
public class PaymentsController(CircleAccessService access, LoanService loans, ProofStore proofs, LedgerClock clock) : CircleControllerBase(access)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? tab)
    {
        var rows = (await loans.PaymentsAsync(Access)).Select(e => PaymentRow.From(Access, e)).ToList();
        var open = rows.Where(r => r.Payment.Status is PaymentStatus.Pending or PaymentStatus.Disputed).ToList();
        return View(new ConfirmationsViewModel
        {
            Mine = open.Where(r => r.NeedsMe).ToList(),
            Others = open.Where(r => !r.NeedsMe).ToList(),
            Notices = ConfirmationsViewModel.BuildNotices(rows),
            Tab = tab == "others" ? "others" : "mine",
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var entry = await loans.PaymentEntryAsync(Access, id);
        if (entry is null) return NotFound();
        var summary = await loans.FindAsync(Access, entry.Payment.LoanId);   // null when confirming the treasurer's payment

        var row = PaymentRow.From(Access, entry);
        var p = entry.Payment;
        var waiting = p.Status is PaymentStatus.Pending or PaymentStatus.Disputed;
        return View(new PaymentDetailViewModel
        {
            Row = row,
            Summary = summary,
            CanConfirm = waiting && LoanService.IsReviewer(Access, entry.Loan.BorrowerId, p),
            CanDispute = p.Status == PaymentStatus.Pending && LoanService.IsReviewer(Access, entry.Loan.BorrowerId, p),
            CanCancel = waiting && LoanService.IsSubmitter(Access, entry.Loan.BorrowerId, p),
        });
    }

    // Proof files are never public: only someone who can see the loan can open them.
    [HttpGet("{id:int}/proof")]
    public async Task<IActionResult> Proof(int id)
    {
        var p = await loans.LoadPaymentAsync(Access, id);
        if (p?.ProofPath is null) return NotFound();
        var stream = proofs.Open(p.ProofPath);
        if (stream is null) return NotFound();
        return File(stream, p.ProofContentType ?? "application/octet-stream");
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create(int? loanId)
    {
        var open = await OpenLoansAsync();
        if (open.Count == 0)
        {
            Toast("There are no loans with a payment left to submit.");
            return RedirectToAction("Index", "Loans", new { circleId = Access.Circle.Id });
        }
        var selected = open.FirstOrDefault(l => l.Loan.Id == loanId) ?? open[0];
        return View(new PaymentFormViewModel
        {
            OpenLoans = open, Selected = selected, LoanId = selected.Loan.Id,
            Amount = DefaultAmount(selected), Date = clock.Today,
        });
    }

    // "pick-N" switches loan; anything else submits the payment. The file is only saved once everything else is valid.
    [HttpPost("new"), ValidateAntiForgeryToken, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Create(PaymentFormViewModel model, string? command)
    {
        model.OpenLoans = await OpenLoansAsync();
        var picking = command?.StartsWith("pick-") == true;
        if (picking && int.TryParse(command![5..], out var pick)) model.LoanId = pick;
        model.Selected = model.OpenLoans.FirstOrDefault(l => l.Loan.Id == model.LoanId);
        if (model.Selected is null) return RedirectToAction(nameof(Create), new { circleId = Access.Circle.Id });

        if (picking)
        {
            ModelState.Clear();
            model.Amount = DefaultAmount(model.Selected);
            return View(model);
        }

        model.Error = LoanService.ValidatePayment(model.Selected, model.Amount ?? 0, model.Date, clock.Today, model.Reference);
        if (model.Error is not null || !ModelState.IsValid) return View(model);

        StoredProof? proof = null;
        if (model.Proof is { Length: > 0 })
        {
            var (saved, error) = await proofs.SaveAsync(model.Proof);
            if (error is not null)
            {
                model.Error = error;
                return View(model);
            }
            proof = saved;
        }

        var result = await loans.SubmitPaymentAsync(Access,
            new NewPayment(model.LoanId, model.Amount!.Value, model.Date, model.Method, model.Reference, proof));
        if (!result.Succeeded)
        {
            if (proof is not null) proofs.Delete(proof.Path);
            model.Error = result.Error;
            return View(model);
        }

        var iPaid = model.Selected.Loan.BorrowerId == Access.Me.Id;
        Toast(!iPaid ? "Payment recorded. It counts once the member confirms it."
            : Access.Me.Id == Access.TreasurerId ? "Payment sent. It counts once another member confirms it."
            : "Payment sent. It counts once the treasurer confirms it.");
        return RedirectToAction(nameof(Index), new { circleId = Access.Circle.Id, tab = "others" });
    }

    [HttpPost("{id:int}/confirm"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id)
    {
        var result = await loans.ConfirmPaymentAsync(Access, id);
        Toast(result.Succeeded ? "Payment confirmed. It now counts toward the balance." : result.Error!);
        return RedirectToAction(nameof(Index), new { circleId = Access.Circle.Id });
    }

    [HttpPost("{id:int}/dispute"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispute(int id, string? reason)
    {
        var result = await loans.DisputePaymentAsync(Access, id, reason);
        if (!result.Succeeded)
        {
            Toast(result.Error!);
            return RedirectToAction(nameof(Details), new { circleId = Access.Circle.Id, id });
        }
        Toast("Marked as disputed. The balance has not changed.");
        return RedirectToAction(nameof(Index), new { circleId = Access.Circle.Id });
    }

    [HttpPost("{id:int}/cancel"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var result = await loans.CancelPaymentAsync(Access, id);
        Toast(result.Succeeded ? "Payment cancelled." : result.Error!);
        return RedirectToAction(nameof(Index), new { circleId = Access.Circle.Id, tab = "others" });
    }

    // Loans that still have something left to submit, most overdue first.
    private async Task<List<LoanSummary>> OpenLoansAsync() =>
        (await loans.VisibleAsync(Access)).Where(l => l.IsOpen && l.Available > 0)
            .OrderByDescending(l => l.Status == LoanStatus.Overdue).ThenByDescending(l => l.Days)
            .ToList();

    private static decimal DefaultAmount(LoanSummary l) => Math.Min(l.Instalment, l.Available);
}
