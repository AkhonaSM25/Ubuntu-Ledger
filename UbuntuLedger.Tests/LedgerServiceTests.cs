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
    public async Task Members_cannot_record_loans_or_pay_someone_elses_loan()
    {
        using var t = await TestDb.SeededAsync();
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var thandisLoan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");

        Assert.False((await t.Loans.CreateAsync(sipho, new NewLoan(sipho.Me.Id, 100, 10, InterestType.Simple, TestDb.Today, 2))).Succeeded);
        Assert.False((await t.Loans.SubmitPaymentAsync(sipho, Pay(thandisLoan.Id, 10))).Succeeded);
    }

    private static NewPayment Pay(int loanId, decimal amount, DateOnly? date = null) =>
        new(loanId, amount, date ?? TestDb.Today, PaymentMethod.EFT, null, null);

    [Fact]
    public async Task Borrowers_payment_only_counts_once_the_treasurer_confirms()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");

        var sent = await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 1100));
        Assert.True(sent.Succeeded);
        var waiting = await t.Loans.FindAsync(zanele, loan.Id);
        Assert.Equal(3300m, waiting!.Balance);   // unchanged until confirmed
        Assert.Equal(1100m, waiting.Pending);

        Assert.True((await t.Loans.ConfirmPaymentAsync(zanele, sent.Id!.Value)).Succeeded);
        var after = await t.Loans.FindAsync(zanele, loan.Id);
        Assert.Equal(2200m, after!.Balance);
        Assert.Equal(0m, after.Pending);
    }

    [Fact]
    public async Task Treasurers_payment_has_to_be_confirmed_by_the_borrower()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");

        var sent = await t.Loans.SubmitPaymentAsync(zanele, Pay(loan.Id, 1100));
        Assert.True(sent.Succeeded);
        Assert.False((await t.Loans.ConfirmPaymentAsync(zanele, sent.Id!.Value)).Succeeded); // cannot confirm your own
        Assert.True((await t.Loans.ConfirmPaymentAsync(thandi, sent.Id.Value)).Succeeded);
        Assert.Equal(2200m, (await t.Loans.FindAsync(zanele, loan.Id))!.Balance);
    }

    [Fact]
    public async Task Borrowers_cannot_confirm_their_own_payment_or_see_other_peoples()
    {
        using var t = await TestDb.SeededAsync();
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        var sent = await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 500));

        Assert.False((await t.Loans.ConfirmPaymentAsync(thandi, sent.Id!.Value)).Succeeded);
        Assert.False((await t.Loans.ConfirmPaymentAsync(sipho, sent.Id.Value)).Succeeded);   // not Sipho's loan
        Assert.False((await t.Loans.DisputePaymentAsync(sipho, sent.Id.Value, "no")).Succeeded);
        Assert.False((await t.Loans.CancelPaymentAsync(sipho, sent.Id.Value)).Succeeded);
        Assert.Equal(3300m, (await t.Loans.FindAsync(thandi, loan.Id))!.Balance);
    }

    [Fact]
    public async Task Disputed_payments_do_not_count_and_the_submitter_can_cancel_them()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        var sent = await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 1100));

        Assert.False((await t.Loans.DisputePaymentAsync(zanele, sent.Id!.Value, "  ")).Succeeded);   // a reason is required
        Assert.False((await t.Loans.DisputePaymentAsync(thandi, sent.Id.Value, "wrong")).Succeeded); // only the other side can dispute
        Assert.True((await t.Loans.DisputePaymentAsync(zanele, sent.Id.Value, "I only got R 900.00")).Succeeded);
        Assert.Equal(3300m, (await t.Loans.FindAsync(zanele, loan.Id))!.Balance);

        Assert.False((await t.Loans.CancelPaymentAsync(zanele, sent.Id.Value)).Succeeded);            // only the submitter can cancel
        Assert.True((await t.Loans.CancelPaymentAsync(thandi, sent.Id.Value)).Succeeded);
        Assert.False((await t.Loans.ConfirmPaymentAsync(zanele, sent.Id.Value)).Succeeded);           // cancelled is final
        Assert.Equal(0m, (await t.Loans.FindAsync(zanele, loan.Id))!.Pending);
    }

    [Fact]
    public async Task Payments_are_checked_against_the_balance_dates_and_what_is_already_pending()
    {
        using var t = await TestDb.SeededAsync();
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");   // R 3 300.00 still owing

        Assert.False((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 3300.01m))).Succeeded);
        Assert.False((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 0))).Succeeded);
        Assert.False((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 100, TestDb.Today.AddDays(1)))).Succeeded);   // future
        Assert.False((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 100, loan.StartDate.AddDays(-1)))).Succeeded); // before the loan

        Assert.True((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 3000))).Succeeded);
        Assert.False((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 400))).Succeeded);   // only R 300.00 left once 3 000 is pending
        Assert.True((await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 300))).Succeeded);
    }

    [Fact]
    public async Task The_treasurer_can_borrow_and_another_member_confirms_the_repayment()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var thandi = await t.AccessAsync("Thandi Mokoena");

        var made = await t.CreateAcceptedLoanAsync(zanele, new NewLoan(zanele.Me.Id, 1000, 10, InterestType.Simple, TestDb.Today.AddDays(-30), 2));
        Assert.True(made.Succeeded);
        var loanId = made.Id!.Value;   // R 1 100.00 in total

        var sent = await t.Loans.SubmitPaymentAsync(zanele, Pay(loanId, 550));
        Assert.True(sent.Succeeded);
        var payment = await t.Db.Repayments.SingleAsync(r => r.Id == sent.Id);
        Assert.Equal(PaymentSide.Payer, payment.SubmittedBy);   // paying your own loan makes you the payer, even as treasurer

        Assert.False((await t.Loans.ConfirmPaymentAsync(zanele, sent.Id!.Value)).Succeeded);   // nobody confirms their own payment

        // Members never see the treasurer's loan or its balance...
        Assert.Null(await t.Loans.FindAsync(sipho, loanId));
        Assert.DoesNotContain(await t.Loans.VisibleAsync(sipho), l => l.Loan.Id == loanId);
        // ...but they see the payment and are asked to confirm it.
        Assert.Contains(await t.Loans.PaymentsAsync(sipho), e => e.Payment.Id == sent.Id.Value);
        Assert.Equal(2, (await t.Loans.SnapshotAsync(sipho)).NeedsMyAction);   // one from the demo data, one from this

        Assert.True((await t.Loans.ConfirmPaymentAsync(sipho, sent.Id.Value)).Succeeded);
        Assert.Equal(550m, (await t.Loans.FindAsync(zanele, loanId))!.Balance);
        Assert.False((await t.Loans.ConfirmPaymentAsync(thandi, sent.Id.Value)).Succeeded);    // already confirmed
    }

    [Fact]
    public async Task A_member_can_dispute_the_treasurers_payment_and_the_treasurer_can_cancel_it()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var made = await t.CreateAcceptedLoanAsync(zanele, new NewLoan(zanele.Me.Id, 1000, 10, InterestType.Simple, TestDb.Today.AddDays(-30), 2));
        var sent = await t.Loans.SubmitPaymentAsync(zanele, Pay(made.Id!.Value, 550));

        Assert.False((await t.Loans.DisputePaymentAsync(zanele, sent.Id!.Value, "no")).Succeeded);   // not hers to dispute
        Assert.True((await t.Loans.DisputePaymentAsync(sipho, sent.Id.Value, "I did not see this")).Succeeded);
        Assert.Equal(1100m, (await t.Loans.FindAsync(zanele, made.Id.Value))!.Balance);
        Assert.False((await t.Loans.CancelPaymentAsync(sipho, sent.Id.Value)).Succeeded);            // only she can cancel it
        Assert.True((await t.Loans.CancelPaymentAsync(zanele, sent.Id.Value)).Succeeded);
    }

    [Fact]
    public async Task Demo_data_has_a_payment_waiting_on_each_of_the_two_demo_accounts()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.Loans.SnapshotAsync(await t.AccessAsync("Zanele Maseko"));
        var sipho = await t.Loans.SnapshotAsync(await t.AccessAsync("Sipho Dlamini"));

        Assert.Equal(1, zanele.NeedsMyAction);   // Sipho says he paid
        Assert.Equal(1, sipho.NeedsMyAction);    // Zanele says she received
        Assert.Equal(2200m, zanele.Pending);
        Assert.Equal(2200m, sipho.Pending);
        Assert.Equal(18670m, zanele.Outstanding); // pending payments leave balances alone
    }

    [Fact]
    public async Task Members_only_see_their_own_payments()
    {
        using var t = await TestDb.SeededAsync();
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        await t.Loans.SubmitPaymentAsync(thandi, Pay(loan.Id, 100));

        var siphos = await t.Loans.PaymentsAsync(await t.AccessAsync("Sipho Dlamini"));
        Assert.All(siphos, e => Assert.Equal("Sipho Dlamini", e.Loan.Borrower.Name));
        Assert.Equal(2, siphos.Count);
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
