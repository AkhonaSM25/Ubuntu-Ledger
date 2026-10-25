using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UbuntuLedger.Data;

namespace UbuntuLedger.Tests;

/// <summary>Runs the real app over HTTP against a temporary SQLite file with the demo data.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ul-tests-" + Guid.NewGuid().ToString("N"));

    public AppFactory()
    {
        Directory.CreateDirectory(_dir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={Path.Combine(_dir, "test.db")}");
        builder.UseSetting("Storage:DataPath", _dir);
        builder.UseSetting("Seed:DemoData", "true");
        builder.UseSetting("Ledger:Today", "2026-10-15");
        builder.UseSetting("Backup:Enabled", "false");
        builder.UseSetting("RateLimit:LoginPerMinute", "1000");
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    public async Task<HttpClient> SignedInAsync(string phone, string pin = SeedData.DemoPin)
    {
        var client = NewClient();
        var response = await PostFormAsync(client, "/login", new() { ["phone"] = phone, ["pin"] = pin });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    // Loads the form page for its anti-forgery token, then posts the fields with it.
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, Dictionary<string, string> fields, string? formPage = null)
    {
        var html = await (await client.GetAsync(formPage ?? url)).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

public class WebTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private async Task<int> CircleIdAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Circles.SingleAsync(c => c.Name == name)).Id;
    }

    [Fact]
    public async Task Health_check_answers_without_signing_in()
    {
        var response = await factory.NewClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Pages_that_need_an_account_send_visitors_to_sign_in()
    {
        var circleId = await CircleIdAsync("The Circle");
        var response = await factory.NewClient().GetAsync($"/circles/{circleId}/loans");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Legal_pages_are_public()
    {
        var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/privacy")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/terms")).StatusCode);
    }

    [Fact]
    public async Task Wrong_pin_stays_on_the_sign_in_page()
    {
        var client = factory.NewClient();
        var response = await AppFactory.PostFormAsync(client, "/login", new() { ["phone"] = SeedData.MemberPhone, ["pin"] = "999999" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("do not match", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_member_can_see_their_loans_but_not_the_treasurers_pages()
    {
        var circleId = await CircleIdAsync("The Circle");
        var sipho = await factory.SignedInAsync(SeedData.MemberPhone);

        Assert.Equal(HttpStatusCode.OK, (await sipho.GetAsync($"/circles/{circleId}/loans")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sipho.GetAsync($"/circles/{circleId}/members")).StatusCode);
    }

    [Fact]
    public async Task Someone_outside_a_circle_cannot_open_it()
    {
        var saversId = await CircleIdAsync("Mokoena Savers");
        var sipho = await factory.SignedInAsync(SeedData.MemberPhone);   // not in Mokoena Savers
        Assert.Equal(HttpStatusCode.NotFound, (await sipho.GetAsync($"/circles/{saversId}/loans")).StatusCode);
    }

    [Fact]
    public async Task Unknown_join_links_show_the_invalid_page_not_an_error()
    {
        var response = await factory.NewClient().GetAsync("/join/not-a-real-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("invite link is not valid", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pages_send_a_content_security_policy_that_blocks_inline_scripts()
    {
        var response = await factory.NewClient().GetAsync("/login");
        var csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_refused_even_on_a_form_that_forgot_the_attribute()
    {
        var sipho = await factory.SignedInAsync(SeedData.MemberPhone);
        var response = await sipho.PostAsync("/account/pin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CurrentPin"] = SeedData.DemoPin, ["NewPin"] = "482915", ["ConfirmPin"] = "482915",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_change_pin_page_needs_sign_in()
    {
        var response = await factory.NewClient().GetAsync("/account/pin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<int> LoanIdAsync(string reference)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Loans.SingleAsync(l => l.Reference == reference)).Id;
    }

    [Fact]
    public async Task The_forgot_pin_page_is_public_and_linked_from_sign_in()
    {
        var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/forgot-pin")).StatusCode);
        Assert.Contains("/forgot-pin", await (await client.GetAsync("/login")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Searching_loans_narrows_the_list()
    {
        var circleId = await CircleIdAsync("The Circle");
        var zanele = await factory.SignedInAsync(SeedData.TreasurerPhone);
        var html = await (await zanele.GetAsync($"/circles/{circleId}/loans?q=Thandi")).Content.ReadAsStringAsync();
        Assert.Contains("Thandi Mokoena", html);
        Assert.DoesNotContain("Sipho Dlamini", html);

        var overdue = await (await zanele.GetAsync($"/circles/{circleId}/loans?filter=overdue")).Content.ReadAsStringAsync();
        Assert.Contains("UL-0", overdue);
    }

    [Fact]
    public async Task Reminders_are_for_the_treasurer_only()
    {
        var circleId = await CircleIdAsync("The Circle");
        var zanele = await factory.SignedInAsync(SeedData.TreasurerPhone);
        var sipho = await factory.SignedInAsync(SeedData.MemberPhone);
        var page = await zanele.GetAsync($"/circles/{circleId}/loans/reminders");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("https://wa.me/", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await sipho.GetAsync($"/circles/{circleId}/loans/reminders")).StatusCode);
    }

    [Fact]
    public async Task A_loan_statement_opens_for_people_who_can_see_the_loan_only()
    {
        var circleId = await CircleIdAsync("The Circle");
        var loanId = await LoanIdAsync("UL-0101");   // Thandi's loan
        var zanele = await factory.SignedInAsync(SeedData.TreasurerPhone);
        var sipho = await factory.SignedInAsync(SeedData.MemberPhone);

        var page = await zanele.GetAsync($"/circles/{circleId}/loans/{loanId}/statement");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("UL-0101", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await sipho.GetAsync($"/circles/{circleId}/loans/{loanId}/statement")).StatusCode);
    }

    [Fact]
    public async Task A_receipt_for_a_payment_that_does_not_exist_is_not_found()
    {
        var circleId = await CircleIdAsync("The Circle");
        var zanele = await factory.SignedInAsync(SeedData.TreasurerPhone);
        Assert.Equal(HttpStatusCode.NotFound, (await zanele.GetAsync($"/circles/{circleId}/payments/99999/receipt")).StatusCode);
    }

    [Fact]
    public async Task The_backup_recovery_page_is_only_for_the_backup_treasurer()
    {
        var circleId = await CircleIdAsync("The Circle");
        var sipho = await factory.SignedInAsync(SeedData.MemberPhone);
        Assert.Equal(HttpStatusCode.NotFound, (await sipho.GetAsync($"/circles/{circleId}/members/treasurer-recovery")).StatusCode);
    }
}
