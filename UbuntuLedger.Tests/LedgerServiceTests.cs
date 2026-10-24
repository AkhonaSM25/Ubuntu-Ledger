using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

public class LedgerServiceTests
{
    [Fact]
    public async Task Seed_matches_the_design_dashboard()
    {
        using var t = await TestDb.SeededAsync();
        var loans = await t.Loans.VisibleAsync(await t.AccessAsync("Zanele Maseko"));
        Assert.Equal(8, loans.Count);
        Assert.Equal(27700m, loans.Sum(l => l.Loan.Principal));
        Assert.Equal(18670m, loans.Sum(l => l.Balance));
        Assert.Equal(2, loans.Count(l => l.Status == LoanStatus.Overdue));
        Assert.Equal(3, loans.Count(l => l.Status == LoanStatus.DueSoon));
    }

    [Fact]
    public async Task Members_see_only_their_own_loans()
    {
        using var t = await TestDb.SeededAsync();
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var mine = await t.Loans.VisibleAsync(sipho);
        Assert.Equal(["UL-0103", "UL-0108"], mine.Select(l => l.Loan.Reference).Order());

        var thandisLoan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        Assert.Null(await t.Loans.FindAsync(sipho, thandisLoan.Id));
    }

    [Fact]
    public async Task Treasurer_creates_loan_with_next_reference()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var nomsa = await t.PersonAsync("Nomsa Khumalo");

        var result = await t.Loans.CreateAsync(zanele, new NewLoan(nomsa.Id, 2000, 10, InterestType.Simple, TestDb.Today, 4));

        Assert.True(result.Succeeded);
        var loan = await t.Db.Loans.FindAsync(result.Id);
        Assert.Equal("UL-0109", loan!.Reference);
    }

    [Fact]
    public async Task Loan_validation_rejects_bad_input_and_non_members()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var errors = await t.Loans.ValidateAsync(zanele, new NewLoan(BorrowerId: 9999, Principal: 0, RatePercent: -1, InterestType.Simple, TestDb.Today, Term: 7));
        Assert.Equal(["BorrowerId", "Principal", "RatePercent", "Term"], errors.Keys.Order());
    }

    [Fact]
    public async Task Members_cannot_record_loans_or_repayments()
    {
        using var t = await TestDb.SeededAsync();
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var own = (await t.Loans.VisibleAsync(sipho)).First();

        Assert.False((await t.Loans.CreateAsync(sipho, new NewLoan(sipho.Me.Id, 100, 10, InterestType.Simple, TestDb.Today, 2))).Succeeded);
        Assert.False((await t.Loans.RecordRepaymentAsync(sipho, new NewRepayment(own.Loan.Id, 10, TestDb.Today, PaymentMethod.Cash, null))).Succeeded);
    }

    [Fact]
    public async Task Repayment_reduces_balance_and_cannot_exceed_it()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");

        var tooMuch = await t.Loans.RecordRepaymentAsync(zanele, new NewRepayment(loan.Id, 3300.01m, TestDb.Today, PaymentMethod.EFT, null));
        Assert.False(tooMuch.Succeeded);

        var ok = await t.Loans.RecordRepaymentAsync(zanele, new NewRepayment(loan.Id, 1100, TestDb.Today, PaymentMethod.EFT, "October"));
        Assert.True(ok.Succeeded);
        var after = await t.Loans.FindAsync(zanele, loan.Id);
        Assert.Equal(2200m, after!.Balance);
    }

    [Fact]
    public async Task Invites_reject_existing_members_and_duplicates()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");

        Assert.False((await t.Circles.InviteAsync(zanele, null, "071 445 1992")).Succeeded); // Sipho is already a member
        Assert.False((await t.Circles.InviteAsync(zanele, null, "0761184420")).Succeeded);   // Ayanda already invited
        Assert.True((await t.Circles.InviteAsync(zanele, "Lindiwe Zulu", "082 771 5530")).Succeeded);
    }

    [Fact]
    public async Task Accepting_an_invite_makes_you_a_member()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.PersonAsync("Zanele Maseko");
        var invite = Assert.Single(await t.Circles.InvitationsForAsync(zanele));

        var result = await t.Circles.RespondAsync(zanele, invite.Id, accept: true);

        Assert.True(result.Succeeded);
        var access = await t.AccessAsync("Zanele Maseko", "Mokoena Savers");
        Assert.Equal(CircleRole.Member, access.Role);
        Assert.Empty(await t.Circles.InvitationsForAsync(zanele));
    }

    [Fact]
    public async Task Starting_a_circle_makes_you_treasurer_and_invites_people()
    {
        using var t = await TestDb.SeededAsync();
        var sipho = await t.PersonAsync("Sipho Dlamini");

        var result = await t.Circles.CreateAsync(sipho, "Dlamini Stokvel", [("Thabo", "071 334 9082"), ("Self", "071 445 1992")]);

        Assert.True(result.Succeeded);
        var access = (await t.Access.GetAsync(result.Id!.Value, sipho.Id))!;
        Assert.True(access.IsTreasurer);
        var pending = Assert.Single(await t.Circles.PendingForCircleAsync(result.Id.Value)); // own number is skipped
        Assert.Equal("0713349082", pending.Phone);
    }
}
