using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

public class CoreFeatureTests
{
    private static NewPayment Pay(int loanId, decimal amount) =>
        new(loanId, amount, TestDb.Today, PaymentMethod.EFT, null, null);

    // A new member of The Circle with no loans.
    private static async Task<Person> AddMemberAsync(TestDb t, string name, string phone)
    {
        var circle = await t.CircleAsync("The Circle");
        var person = new Person { Name = name, Phone = phone, CreatedOn = TestDb.Today };
        t.Db.People.Add(person);
        t.Db.Memberships.Add(new Membership { CircleId = circle.Id, Person = person, Role = CircleRole.Member, JoinedOn = TestDb.Today });
        await t.Db.SaveChangesAsync();
        return person;
    }

    // ---- Backup treasurer ----

    [Fact]
    public async Task Only_the_treasurer_chooses_a_backup_and_there_is_only_one()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.PersonAsync("Sipho Dlamini");
        var thandi = await t.PersonAsync("Thandi Mokoena");

        Assert.False((await t.Circles.SetBackupAsync(await t.AccessAsync("Sipho Dlamini"), thandi.Id, true)).Succeeded);   // a member cannot
        Assert.False((await t.Circles.SetBackupAsync(zanele, zanele.Me.Id, true)).Succeeded);                              // not yourself

        Assert.True((await t.Circles.SetBackupAsync(zanele, sipho.Id, true)).Succeeded);
        Assert.True((await t.Circles.SetBackupAsync(zanele, thandi.Id, true)).Succeeded);   // replaces Sipho
        Assert.Equal(CircleRole.Member, (await t.AccessAsync("Sipho Dlamini")).Role);
        Assert.Equal(CircleRole.Backup, (await t.AccessAsync("Thandi Mokoena")).Role);

        Assert.False((await t.Circles.SetBackupAsync(zanele, sipho.Id, false)).Succeeded);  // Sipho is not the backup any more
        Assert.True((await t.Circles.SetBackupAsync(zanele, thandi.Id, false)).Succeeded);
        Assert.Equal(CircleRole.Member, (await t.AccessAsync("Thandi Mokoena")).Role);
    }

    [Fact]
    public async Task The_backup_can_reset_the_treasurers_pin_and_nobody_elses()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var siphoPerson = await t.PersonAsync("Sipho Dlamini");
        await t.Circles.SetBackupAsync(zanele, siphoPerson.Id, true);
        var sipho = await t.AccessAsync("Sipho Dlamini");
        Assert.True(sipho.IsBackup);
        Assert.False(sipho.IsTreasurer);   // no treasurer powers

        var (token, error) = await t.Accounts.CreatePinResetAsync(sipho, zanele.Me.Id);
        Assert.Null(error);
        Assert.True((await t.Accounts.ResetPinAsync(token, "482915", "482915")).Succeeded);
        Assert.NotNull((await t.Accounts.SignInAsync(zanele.Me.Phone, "482915")).Person);

        var nomsa = await t.PersonAsync("Nomsa Khumalo");
        Assert.NotNull((await t.Accounts.CreatePinResetAsync(sipho, nomsa.Id)).Error);   // not an ordinary member
    }

    [Fact]
    public async Task An_ordinary_member_cannot_reset_the_treasurers_pin()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        Assert.NotNull((await t.Accounts.CreatePinResetAsync(sipho, zanele.Me.Id)).Error);
    }

    [Fact]
    public async Task My_circles_list_the_ones_I_run_first()
    {
        using var t = await TestDb.SeededAsync();
        var thandi = await t.PersonAsync("Thandi Mokoena");   // member of The Circle, treasurer of Mokoena Savers
        var mine = await t.Circles.MyCirclesAsync(thandi.Id);
        Assert.Equal("Mokoena Savers", mine[0].Circle.Name);
    }

    // ---- Leaving and removing ----

    [Fact]
    public async Task A_member_with_nothing_owing_can_leave_and_the_history_stays()
    {
        using var t = await TestDb.SeededAsync();
        var person = await AddMemberAsync(t, "Lindiwe Zulu", "0827715530");
        var access = await t.AccessAsync("Lindiwe Zulu");

        Assert.True((await t.Circles.RemoveMemberAsync(access, person.Id)).Succeeded);
        Assert.Null(await t.Access.GetAsync(access.Circle.Id, person.Id));
    }

    [Fact]
    public async Task Nobody_can_leave_or_be_removed_while_they_owe_money_and_the_treasurer_cannot_go()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");   // has open loans in the demo data

        Assert.False((await t.Circles.RemoveMemberAsync(sipho, sipho.Me.Id)).Succeeded);   // leaving
        Assert.False((await t.Circles.RemoveMemberAsync(zanele, sipho.Me.Id)).Succeeded);  // removed
        Assert.False((await t.Circles.RemoveMemberAsync(zanele, zanele.Me.Id)).Succeeded); // the treasurer
        Assert.False((await t.Circles.RemoveMemberAsync(sipho, zanele.Me.Id)).Succeeded);  // a member removing the treasurer
    }

    [Fact]
    public async Task A_member_cannot_remove_someone_else()
    {
        using var t = await TestDb.SeededAsync();
        var person = await AddMemberAsync(t, "Lindiwe Zulu", "0827715530");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        Assert.False((await t.Circles.RemoveMemberAsync(sipho, person.Id)).Succeeded);
    }

    [Fact]
    public async Task Removing_a_member_waits_for_their_loan_to_be_settled_and_withdraws_waiting_requests()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var person = await AddMemberAsync(t, "Lindiwe Zulu", "0827715530");
        var made = await t.CreateAcceptedLoanAsync(zanele, new NewLoan(person.Id, 500, 10, InterestType.Simple, TestDb.Today.AddDays(-5), 2));
        t.Db.LoanRequests.Add(new LoanRequest { CircleId = zanele.Circle.Id, RequesterId = person.Id, Amount = 200, Term = 2, Status = LoanRequestStatus.Pending, CreatedAt = DateTime.UtcNow });
        await t.Db.SaveChangesAsync();

        Assert.False((await t.Circles.RemoveMemberAsync(zanele, person.Id)).Succeeded);   // still owes
        Assert.True((await t.Loans.VoidLoanAsync(zanele, made.Id!.Value, "Entered by mistake")).Succeeded);
        Assert.True((await t.Circles.RemoveMemberAsync(zanele, person.Id)).Succeeded);

        Assert.Equal(LoanRequestStatus.Cancelled, (await t.Db.LoanRequests.SingleAsync(r => r.RequesterId == person.Id)).Status);
        Assert.Contains(await t.Loans.VoidedAsync(zanele), l => l.Loan.Id == made.Id);   // history kept
    }

    // ---- Receipts ----

    [Fact]
    public async Task A_receipt_exists_only_for_a_confirmed_payment_and_shows_the_balance_after_it()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");   // R 3 300.00 owing
        var sent = await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 1100));

        Assert.Null(await t.Loans.ReceiptAsync(zanele, sent.Id!.Value));   // still waiting
        await t.Loans.ConfirmPaymentAsync(zanele, sent.Id.Value);

        var receipt = (await t.Loans.ReceiptAsync(thandi, sent.Id.Value))!;
        Assert.Equal(1100m, receipt.Payment.Amount);
        Assert.Equal("Zanele Maseko", receipt.ConfirmedByName);
        Assert.Equal(2200m, receipt.BalanceAfter);

        await t.Loans.VoidPaymentAsync(zanele, sent.Id.Value, "Counted twice");
        Assert.Null(await t.Loans.ReceiptAsync(zanele, sent.Id.Value));    // reversed: no receipt
    }

    [Fact]
    public async Task A_receipt_hides_the_balance_from_a_member_who_may_not_see_the_loan()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var made = await t.CreateAcceptedLoanAsync(zanele, new NewLoan(zanele.Me.Id, 1000, 10, InterestType.Simple, TestDb.Today.AddDays(-30), 2));
        var sent = await t.Loans.SubmitPaymentAsync(zanele, Pay(made.Id!.Value, 550));
        await t.Loans.ConfirmPaymentAsync(sipho, sent.Id!.Value);

        Assert.Equal(550m, (await t.Loans.ReceiptAsync(zanele, sent.Id.Value))!.BalanceAfter);
        Assert.Null((await t.Loans.ReceiptAsync(sipho, sent.Id.Value))!.BalanceAfter);
    }
}
