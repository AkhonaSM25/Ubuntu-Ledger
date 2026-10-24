using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

[Route("circles/{circleId:int}/reports")]
public class ReportsController(CircleAccessService access, LoanService loans, LedgerClock clock) : CircleControllerBase(access)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int period = 6) => View(await BuildAsync(period));

    [HttpGet("print")]
    public async Task<IActionResult> Print(int period = 6) => View(await BuildAsync(period));

    [HttpGet("export")]
    public async Task<IActionResult> Export(int period = 6)
    {
        var model = await BuildAsync(period);
        var csv = new StringBuilder("Month,Lent,Repaid,Interest\n");
        foreach (var m in model.Months)
            csv.AppendLine(string.Join(",", m.Month.ToString("yyyy-MM"), N(m.Lent), N(m.Repaid), N(m.Interest)));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"{Access.Circle.Name}-report.csv");
    }

    private async Task<ReportsViewModel> BuildAsync(int period)
    {
        period = period == 3 ? 3 : 6;
        var visible = await loans.VisibleAsync(Access);
        var first = new DateOnly(clock.Today.Year, clock.Today.Month, 1).AddMonths(1 - period);
        var months = Enumerable.Range(0, period).Select(i => first.AddMonths(i)).Select(m =>
        {
            bool In(DateOnly d) => d.Year == m.Year && d.Month == m.Month;
            return new ReportMonth(m,
                visible.Where(l => In(l.Loan.StartDate)).Sum(l => l.Loan.Principal),
                visible.Sum(l => l.Loan.Repayments.Where(r => In(r.Date)).Sum(r => r.Amount)),
                visible.Sum(l => l.Loan.Repayments.Where(r => In(r.Date)).Sum(r => LoanCalculator.InterestShare(l, r.Amount))));
        }).ToList();

        // Y axis in whole thousands with four gridlines.
        var max = Math.Max(1000, months.Max(m => Math.Max(m.Lent, m.Repaid)));
        var step = Math.Max(1000, Math.Ceiling(max / 4 / 1000) * 1000);
        var admin = Access.IsTreasurer;
        return new ReportsViewModel
        {
            Period = period,
            Months = months,
            AxisStep = step,
            Stats =
            [
                new(admin ? "Lent out" : "Borrowed", Format.Zar(months.Sum(m => m.Lent)), $"In the last {period} months", "wallet"),
                new("Repaid", Format.Zar(months.Sum(m => m.Repaid)), "Received in the period", "down"),
                new("Interest earned", Format.Zar(months.Sum(m => m.Interest)), "Part of repayments", "coin"),
                new(admin ? "Outstanding now" : "You still owe", Format.Zar(visible.Sum(l => l.Balance)), Format.Plural(visible.Count(l => l.IsOpen), "loan") + " open", "wallet", Featured: true),
            ],
        };
    }

    private static string N(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
}
