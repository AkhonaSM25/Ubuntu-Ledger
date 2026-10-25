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
}
