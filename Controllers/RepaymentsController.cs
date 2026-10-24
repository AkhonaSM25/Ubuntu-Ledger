using Microsoft.AspNetCore.Mvc;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

[Route("circles/{circleId:int}/repayments")]
public class RepaymentsController(CircleAccessService access, LoanService loans, LedgerClock clock) : CircleControllerBase(access)
{
    [HttpGet("new")]
    public async Task<IActionResult> Create(int? loanId)
    {
        if (!Access.IsTreasurer) return NotFound();
        var open = await OpenLoansAsync();
        if (open.Count == 0)
        {
            Toast("There are no open loans to record a repayment against.");
            return RedirectToAction("Index", "Loans", new { circleId = Access.Circle.Id });
        }
        // Default to the loan asked for, else the most overdue one.
        var selected = open.FirstOrDefault(l => l.Loan.Id == loanId) ?? open[0];
        return View(new RepaymentViewModel { OpenLoans = open, Selected = selected, LoanId = selected.Loan.Id, Amount = selected.NextAmount, Date = clock.Today });
    }

    // "pick-N" switches loan, "review" opens the confirmation, "confirm" records it.
    [HttpPost("new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RepaymentViewModel model, string? command)
    {
        if (!Access.IsTreasurer) return NotFound();
        model.OpenLoans = await OpenLoansAsync();
        if (command?.StartsWith("pick-") == true && int.TryParse(command[5..], out var pick)) model.LoanId = pick;
        model.Selected = model.OpenLoans.FirstOrDefault(l => l.Loan.Id == model.LoanId);
        if (model.Selected is null) return RedirectToAction(nameof(Create));

        if (command?.StartsWith("pick-") == true)
        {
            ModelState.Clear();
            model.Amount = model.Selected.NextAmount;
            return View(model);
        }

        model.Error = LoanService.ValidateRepayment(model.Selected, model.Amount ?? 0);
        if (model.Error is not null || !ModelState.IsValid) return View(model);
        if (command == "edit") return View(model);
        if (command != "confirm")
        {
            model.Confirming = true;
            return View(model);
        }

        var result = await loans.RecordRepaymentAsync(Access, new NewRepayment(model.LoanId, model.Amount!.Value, model.Date, model.Method, model.Note));
        if (!result.Succeeded)
        {
            model.Error = result.Error;
            return View(model);
        }
        Toast($"Repayment of {Format.Zar(model.Amount.Value)} recorded. New balance {Format.Zar(model.NewBalance)}.");
        return RedirectToAction("Details", "Loans", new { circleId = Access.Circle.Id, id = model.LoanId });
    }

    private async Task<List<LoanSummary>> OpenLoansAsync() =>
        (await loans.VisibleAsync(Access)).Where(l => l.IsOpen)
            .OrderByDescending(l => l.Status == LoanStatus.Overdue).ThenByDescending(l => l.Days)
            .ToList();
}
