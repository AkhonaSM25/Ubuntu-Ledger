using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

// A loan the treasurer records only starts once the borrower has seen the full cost and accepted it.
public class LoanAcceptanceTests
{
    // R 1 000.00 at 10% flat over 2 payments: R 100.00 interest, R 1 100.00 to repay, R 550.00 each.
    private static NewLoan Terms(int borrowerId) => new(borrowerId, 1000, 10, InterestType.Simple, TestDb.Today.AddDays(-10), 2);

    private static NewPayment Pay(int loanId, decimal amount) => new(loanId, amount, TestDb.Today, PaymentMethod.EFT, null, null);

    private static async Task<(TestDb t, CircleAccess zanele, CircleAccess nomsa, CircleAccess sipho, int loanId)> RecordedAsync()
    {
        var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var nomsa = await t.AccessAsync("Nomsa Khumalo");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var made = await t.Loans.CreateAsync(zanele, Terms(nomsa.Me.Id));
        Assert.True(made.Succeeded);
        return (t, zanele, nomsa, sipho, made.Id!.Value);
    }

    [Fact]
    public async Task The_demo_loans_that_existed_before_acceptance_are_live()
    {
        using var t = await TestDb.SeededAsync();
        Assert.DoesNotContain(await t.Db.Loans.ToListAsync(), l => l.AwaitingAcceptance);
    }

    [Fact]
    public async Task A_new_loan_waits_for_the_borrower_and_owes_nothing_yet()
    {
        var (t, zanele, nomsa, _, loanId) = await RecordedAsync();
        using var _t = t;

        var loan = await t.Loans.FindAsync(zanele, loanId);
        Assert.NotNull(loan);
        Assert.True(loan.IsAwaiting);
        Assert.Equal(LoanStatus.AwaitingAcceptance, loan.Status);
        Assert.Equal(0m, loan.Balance);
        Assert.False(loan.IsOpen);

        // The borrower is shown the whole cost before agreeing.
        Assert.Equal(100m, loan.Interest);
        Assert.Equal(1100m, loan.Total);
        Assert.Equal(550m, loan.Instalment);

        Assert.DoesNotContain(await t.Loans.VisibleAsync(zanele), l => l.Loan.Id == loanId);
        Assert.DoesNotContain(await t.Loans.VisibleAsync(nomsa), l => l.Loan.Id == loanId);
        Assert.Contains(await t.Loans.AwaitingAsync(zanele), l => l.Loan.Id == loanId);
        Assert.Contains(await t.Loans.AwaitingAsync(nomsa), l => l.Loan.Id == loanId);
    }

    [Fact]
    public async Task A_loan_waiting_for_acceptance_is_left_out_of_the_totals()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var nomsa = await t.AccessAsync("Nomsa Khumalo");
        var before = await t.Loans.SnapshotAsync(zanele);

        var made = await t.Loans.CreateAsync(zanele, Terms(nomsa.Me.Id));
        Assert.Equal(before.Outstanding, (await t.Loans.SnapshotAsync(zanele)).Outstanding);

        Assert.True((await t.Loans.AcceptAsync(nomsa, made.Id!.Value)).Succeeded);
        Assert.Equal(before.Outstanding + 1100m, (await t.Loans.SnapshotAsync(zanele)).Outstanding);
    }

    [Fact]
    public async Task Only_the_borrower_can_accept()
    {
        var (t, zanele, nomsa, sipho, loanId) = await RecordedAsync();
        using var _t = t;

        Assert.False((await t.Loans.AcceptAsync(zanele, loanId)).Succeeded);   // not even the treasurer, for someone else
        Assert.False((await t.Loans.AcceptAsync(sipho, loanId)).Succeeded);    // another member cannot even see it
        Assert.True((await t.Db.Loans.SingleAsync(l => l.Id == loanId)).AwaitingAcceptance);

        var accepted = await t.Loans.AcceptAsync(nomsa, loanId);
        Assert.True(accepted.Succeeded);

        var loan = await t.Db.Loans.SingleAsync(l => l.Id == loanId);
        Assert.False(loan.AwaitingAcceptance);
        Assert.Equal(nomsa.Me.Id, loan.AcceptedById);
        Assert.NotNull(loan.AcceptedAt);

        Assert.False((await t.Loans.AcceptAsync(nomsa, loanId)).Succeeded);    // only once
    }

    [Fact]
    public async Task Accepting_makes_the_loan_live_with_the_agreed_total()
    {
        var (t, _, nomsa, _, loanId) = await RecordedAsync();
        using var _t = t;

        await t.Loans.AcceptAsync(nomsa, loanId);

        var loan = await t.Loans.FindAsync(nomsa, loanId);
        Assert.False(loan!.IsAwaiting);
        Assert.True(loan.IsOpen);
        Assert.Equal(1100m, loan.Balance);
        Assert.Contains(await t.Loans.VisibleAsync(nomsa), l => l.Loan.Id == loanId);
        Assert.DoesNotContain(await t.Loans.AwaitingAsync(nomsa), l => l.Loan.Id == loanId);
    }

    [Fact]
    public async Task A_loan_cannot_take_payments_until_it_is_accepted()
    {
        var (t, zanele, nomsa, _, loanId) = await RecordedAsync();
        using var _t = t;

        var early = await t.Loans.SubmitPaymentAsync(nomsa, Pay(loanId, 550));
        Assert.False(early.Succeeded);
        Assert.Contains("accept", early.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.False((await t.Loans.SubmitPaymentAsync(zanele, Pay(loanId, 550))).Succeeded);

        await t.Loans.AcceptAsync(nomsa, loanId);
        Assert.True((await t.Loans.SubmitPaymentAsync(nomsa, Pay(loanId, 550))).Succeeded);
    }

    [Fact]
    public async Task A_loan_nobody_accepted_can_be_voided_and_then_cannot_be_accepted()
    {
        var (t, zanele, nomsa, _, loanId) = await RecordedAsync();
        using var _t = t;

        Assert.True((await t.Loans.VoidLoanAsync(zanele, loanId, "Wrong amount entered")).Succeeded);

        Assert.DoesNotContain(await t.Loans.AwaitingAsync(zanele), l => l.Loan.Id == loanId);
        Assert.Contains(await t.Loans.VoidedAsync(zanele), l => l.Loan.Id == loanId);
        Assert.False((await t.Loans.AcceptAsync(nomsa, loanId)).Succeeded);
    }

    [Fact]
    public async Task Approving_a_request_records_a_loan_that_waits_for_the_member()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");

        var sent = await t.Requests.SubmitAsync(sipho, new NewLoanRequest(1000, 2, "Stock"));
        var approved = await t.Requests.ApproveAsync(zanele, sent.Id!.Value, new ApprovalTerms(10, InterestType.Simple, TestDb.Today));
        Assert.True(approved.Succeeded);

        Assert.True((await t.Db.Loans.SingleAsync(l => l.Id == approved.Id)).AwaitingAcceptance);
        Assert.True((await t.Loans.AcceptAsync(sipho, approved.Id!.Value)).Succeeded);
    }

    [Fact]
    public async Task A_member_with_a_loan_waiting_to_be_accepted_cannot_be_removed_until_it_is_voided()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");

        // A new member with no other loans, so the only thing in the way is the loan waiting to be accepted.
        var circle = await t.CircleAsync("The Circle");
        var person = new Person { Name = "Lindiwe Zulu", Phone = "0827715530", CreatedOn = TestDb.Today };
        t.Db.People.Add(person);
        t.Db.Memberships.Add(new Membership { CircleId = circle.Id, Person = person, Role = CircleRole.Member, JoinedOn = TestDb.Today });
        await t.Db.SaveChangesAsync();

        var made = await t.Loans.CreateAsync(zanele, Terms(person.Id));
        Assert.False((await t.Circles.RemoveMemberAsync(zanele, person.Id)).Succeeded);

        Assert.True((await t.Loans.VoidLoanAsync(zanele, made.Id!.Value, "No longer needed")).Succeeded);
        Assert.True((await t.Circles.RemoveMemberAsync(zanele, person.Id)).Succeeded);
    }
}
