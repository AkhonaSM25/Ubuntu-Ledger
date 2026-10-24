using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

public class LoanRequestTests
{
    private static NewLoanRequest Ask(decimal amount = 2000, int term = 4, string? reason = "Stock") => new(amount, term, reason);
    private static ApprovalTerms Terms(decimal rate = 10) => new(rate, InterestType.Simple, TestDb.Today);

    [Fact]
    public async Task Demo_data_has_one_request_waiting_on_the_treasurer()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var (open, _) = await t.Requests.ListAsync(zanele);
        Assert.Single(open);
        Assert.Equal(1, (await t.Loans.SnapshotAsync(zanele)).Requests);
        Assert.Equal(0, (await t.Loans.SnapshotAsync(sipho)).Requests);   // members get no badge
        Assert.Empty((await t.Requests.ListAsync(sipho)).Open);           // and never see other members' requests
    }

    [Fact]
    public async Task Approving_a_request_creates_the_loan_for_that_member()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var thandi = await t.AccessAsync("Thandi Mokoena");

        var sent = await t.Requests.SubmitAsync(sipho, Ask(1000, 2));
        Assert.True(sent.Succeeded);
        Assert.Null(await t.Requests.FindAsync(thandi, sent.Id!.Value));      // another member cannot open it
        Assert.NotNull(await t.Requests.FindAsync(zanele, sent.Id.Value));
        Assert.Equal(2, (await t.Loans.SnapshotAsync(zanele)).Requests);

        Assert.False((await t.Requests.ApproveAsync(sipho, sent.Id.Value, Terms())).Succeeded);   // members cannot approve their own
        Assert.False((await t.Requests.ApproveAsync(zanele, sent.Id.Value, Terms(150))).Succeeded); // bad rate leaves it waiting
        Assert.Equal(LoanRequestStatus.Pending, (await t.Db.LoanRequests.FindAsync(sent.Id.Value))!.Status);

        var approved = await t.Requests.ApproveAsync(zanele, sent.Id.Value, Terms());
        Assert.True(approved.Succeeded);
        var loan = await t.Db.Loans.SingleAsync(l => l.Id == approved.Id);
        Assert.Equal(sipho.Me.Id, loan.BorrowerId);
        Assert.Equal(1000m, loan.Principal);
        Assert.Equal(2, loan.Term);
        Assert.Equal(10m, loan.RatePercent);
        var request = await t.Db.LoanRequests.FindAsync(sent.Id.Value);
        Assert.Equal(LoanRequestStatus.Approved, request!.Status);
        Assert.Equal(loan.Id, request.LoanId);
        Assert.NotNull(await t.Loans.FindAsync(sipho, loan.Id));              // it shows in the member's ledger

        Assert.False((await t.Requests.ApproveAsync(zanele, sent.Id.Value, Terms())).Succeeded);   // only once
    }

    [Fact]
    public async Task Declined_and_cancelled_requests_create_no_loan()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var before = await t.Db.Loans.CountAsync();

        var first = await t.Requests.SubmitAsync(sipho, Ask());
        Assert.False((await t.Requests.DeclineAsync(sipho, first.Id!.Value, "no")).Succeeded);       // not a member's call
        Assert.True((await t.Requests.DeclineAsync(zanele, first.Id.Value, "Finish UL-0103 first")).Succeeded);
        var declined = await t.Db.LoanRequests.FindAsync(first.Id.Value);
        Assert.Equal(LoanRequestStatus.Declined, declined!.Status);
        Assert.Equal("Finish UL-0103 first", declined.DeclineReason);

        var second = await t.Requests.SubmitAsync(sipho, Ask());   // a new request is allowed after an answer
        Assert.True(second.Succeeded);
        Assert.False((await t.Requests.CancelAsync(thandi, second.Id!.Value)).Succeeded);            // only the asker cancels
        Assert.False((await t.Requests.CancelAsync(zanele, second.Id.Value)).Succeeded);
        Assert.True((await t.Requests.CancelAsync(sipho, second.Id.Value)).Succeeded);
        Assert.False((await t.Requests.ApproveAsync(zanele, second.Id.Value, Terms())).Succeeded);

        Assert.Equal(before, await t.Db.Loans.CountAsync());
    }

    [Fact]
    public async Task Rules_for_who_may_ask_and_for_what()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");

        Assert.False((await t.Requests.SubmitAsync(zanele, Ask())).Succeeded);   // the treasurer records loans directly

        Assert.NotEmpty(LoanRequestService.Validate(Ask(0)));
        Assert.NotEmpty(LoanRequestService.Validate(Ask(-5)));
        Assert.NotEmpty(LoanRequestService.Validate(Ask(LoanRequestService.MaxAmount + 1)));
        Assert.NotEmpty(LoanRequestService.Validate(Ask(1000, 7)));
        Assert.NotEmpty(LoanRequestService.Validate(Ask(1000, 4, new string('x', 201))));
        Assert.Empty(LoanRequestService.Validate(Ask(LoanRequestService.MaxAmount)));
        Assert.False((await t.Requests.SubmitAsync(sipho, Ask(0))).Succeeded);

        Assert.True((await t.Requests.SubmitAsync(sipho, Ask())).Succeeded);
        Assert.False((await t.Requests.SubmitAsync(sipho, Ask())).Succeeded);    // one waiting request at a time
    }

    [Fact]
    public async Task A_member_with_an_overdue_loan_cannot_ask_for_another()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var overdue = (await t.Loans.VisibleAsync(zanele)).First(l => l.Status == LoanStatus.Overdue).Loan.Borrower.Name;
        var member = await t.AccessAsync(overdue);

        Assert.NotNull(await t.Requests.WhyNotAsync(member));
        Assert.False((await t.Requests.SubmitAsync(member, Ask())).Succeeded);

        var sipho = await t.AccessAsync("Sipho Dlamini");   // no overdue loan in the demo data
        Assert.Null(await t.Requests.WhyNotAsync(sipho));
    }
}
