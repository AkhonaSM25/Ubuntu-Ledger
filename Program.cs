using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// SQLite keeps the app portable; a hosted database is a configuration change.
builder.Services.AddDbContext<LedgerDbContext>(options => options.UseSqlite(connectionString));

// Sign-in is phone number + PIN, kept in a cookie.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Secure cookies everywhere except local development over http.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorization();

// Slow down PIN guessing from one address: 10 sign-in attempts per minute per IP address. The account lockout in
// AccountService is the real limit (it follows the account, not the address); this just stops floods.
var loginsPerMinute = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Every POST, PUT, PATCH and DELETE has to carry the anti-forgery token, so a new form cannot forget it.
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LedgerClock>();
builder.Services.AddScoped<CircleAccessService>();
builder.Services.AddScoped<CircleService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<LoanService>();
builder.Services.AddScoped<LoanRequestService>();
builder.Services.AddSingleton<StoragePaths>();
builder.Services.AddSingleton<ProofStore>();

// Daily backup of the database and proof files, and a /health endpoint for the host to watch.
builder.Services.AddSingleton<BackupStatus>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackupService>());
builder.Services.AddHealthChecks().AddCheck<LedgerHealthCheck>("ledger");

// Logs, errors and request timings go to Application Insights when the host provides its connection string.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

// Create/upgrade the database. The demo circle is only seeded when Seed:DemoData is true: on by default for local
// development, off everywhere else. Switch it on for the hackathon demo site with the environment variable
// Seed__DemoData=true, and never on a site that holds real circles: the demo accounts have a known PIN.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
    await db.Database.MigrateAsync();
    if (app.Configuration.GetValue("Seed:DemoData", false))
    {
        await SeedData.InitializeAsync(db);
        if (!app.Environment.IsDevelopment())
            app.Logger.LogWarning("Seed:DemoData is on: demo accounts with a published PIN exist. Never do this with real data.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // Only this site's own scripts run. Styles may be inline (the views use style attributes); the one outside
    // source is Google Fonts. Pages cannot be framed or post forms anywhere else.
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});

// The host's health probe may arrive over plain http; do not bounce it to https.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var controllers = app.MapControllers();
// Integration tests run in the "Testing" environment, where there is no published static-asset manifest to read.
if (!app.Environment.IsEnvironment("Testing"))
{
    app.MapStaticAssets();
    controllers.WithStaticAssets();
}
app.MapHealthChecks("/health");

app.Run();

// Lets the integration tests start the whole app (WebApplicationFactory<Program>).
public partial class Program;
