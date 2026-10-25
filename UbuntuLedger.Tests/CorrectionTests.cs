using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

public class CorrectionTests
{
    private static NewPayment Pay(int loanId, decimal amount) =>
        new(loanId, amount, TestDb.Today, PaymentMethod.EFT, null, null);

    [Fact]
    public async Task Voiding_a_confirmed_payment_restores_the_balance_and_keeps_the_record()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        var sent = await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 1100));
        await t.Loans.ConfirmPaymentAsync(zanele, sent.Id!.Value);
        Assert.Equal(2200m, (await t.Loans.FindAsync(zanele, loan.Id))!.Balance);

        Assert.False((await t.Loans.VoidPaymentAsync(thandi, sent.Id.Value, "oops")).Succeeded);   // borrower cannot
        Assert.False((await t.Loans.VoidPaymentAsync(zanele, sent.Id.Value, "  ")).Succeeded);     // reason required
        Assert.True((await t.Loans.VoidPaymentAsync(zanele, sent.Id.Value, "Counted twice")).Succeeded);

        Assert.Equal(3300m, (await t.Loans.FindAsync(zanele, loan.Id))!.Balance);
        var row = await t.Db.Repayments.SingleAsync(r => r.Id == sent.Id);
        Assert.Equal(PaymentStatus.Voided, row.Status);
        Assert.Equal("Counted twice", row.VoidReason);
        Assert.Equal(zanele.Me.Id, row.VoidedById);
        Assert.False((await t.Loans.VoidPaymentAsync(zanele, sent.Id.Value, "again")).Succeeded);  // only once
    }

    [Fact]
    public async Task Only_confirmed_payments_can_be_voided()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        var sent = await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 500));
        Assert.False((await t.Loans.VoidPaymentAsync(zanele, sent.Id!.Value, "still pending")).Succeeded);
    }

    [Fact]
    public async Task A_loan_with_confirmed_payments_must_have_them_reversed_before_it_is_voided()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        var sent = await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 1100));
        await t.Loans.ConfirmPaymentAsync(zanele, sent.Id!.Value);

        Assert.False((await t.Loans.VoidLoanAsync(thandi, loan.Id, "x")).Succeeded);               // not the treasurer
        Assert.False((await t.Loans.VoidLoanAsync(zanele, loan.Id, "Wrong person")).Succeeded);    // payment still counts
        await t.Loans.VoidPaymentAsync(zanele, sent.Id.Value, "Wrong loan");
        Assert.True((await t.Loans.VoidLoanAsync(zanele, loan.Id, "Wrong person")).Succeeded);
        Assert.False((await t.Loans.VoidLoanAsync(zanele, loan.Id, "twice")).Succeeded);

        Assert.DoesNotContain(await t.Loans.VisibleAsync(zanele), l => l.Loan.Id == loan.Id);
        Assert.Contains(await t.Loans.VoidedAsync(zanele), l => l.Loan.Id == loan.Id);
        Assert.False((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 10))).Succeeded);       // no payments on a voided loan
    }

    [Fact]
    public async Task Voiding_a_loan_cancels_the_payments_still_waiting()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        var sent = await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 100));

        Assert.True((await t.Loans.VoidLoanAsync(zanele, loan.Id, "Entered by mistake")).Succeeded);
        Assert.Equal(PaymentStatus.Cancelled, (await t.Db.Repayments.SingleAsync(r => r.Id == sent.Id)).Status);
    }

    [Fact]
    public async Task The_same_payment_cannot_be_submitted_twice_while_the_first_is_waiting()
    {
        using var t = await TestDb.SeededAsync();
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        Assert.True((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 500))).Succeeded);
        Assert.False((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 500))).Succeeded);
    }

    [Fact]
    public async Task Loan_references_keep_counting_past_the_four_digit_mark()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var nomsa = await t.PersonAsync("Nomsa Khumalo");
        var first = await t.Loans.CreateAsync(zanele, new NewLoan(nomsa.Id, 100, 10, InterestType.Simple, TestDb.Today, 2));
        var second = await t.Loans.CreateAsync(zanele, new NewLoan(nomsa.Id, 100, 10, InterestType.Simple, TestDb.Today, 2));
        var refs = await t.Db.Loans.Where(l => l.Id == first.Id || l.Id == second.Id).Select(l => l.Reference).ToListAsync();
        Assert.Equal(2, refs.Distinct().Count());
    }

    [Fact]
    public async Task Dashboard_totals_ignore_voided_loans()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var before = await t.Loans.SnapshotAsync(zanele);
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        await t.Loans.VoidLoanAsync(zanele, loan.Id, "Mistake");
        var after = await t.Loans.SnapshotAsync(zanele);
        Assert.Equal(before.Outstanding - 3300m, after.Outstanding);
    }
}
