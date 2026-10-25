using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UbuntuLedger.Data;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

/// <summary>
/// A throw-away SQLite in-memory database with the real EF model, the demo seed and the real services.
/// "Today" is pinned to 15 Oct 2026, the date the demo data was written for. Each test builds its own instance.
/// </summary>
/// <summary>A clock the tests can move forward, for lockouts and expiring links.</summary>
public sealed class TestTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public sealed class TestDb : IDisposable
{
    public static readonly DateOnly Today = new(2026, 10, 15);
    private readonly SqliteConnection _connection;

    public LedgerDbContext Db { get; }
    public LedgerClock Clock { get; }
    public CircleService Circles { get; }
    public LoanService Loans { get; }
    public LoanRequestService Requests { get; }
    public CircleAccessService Access { get; }
    public TestTime Time { get; } = new();
    public AccountService Accounts { get; }

    private TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Db = new LedgerDbContext(new DbContextOptionsBuilder<LedgerDbContext>().UseSqlite(_connection).Options);
        Clock = new LedgerClock(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ledger:Today"] = Today.ToString("yyyy-MM-dd") }).Build());
        Circles = new CircleService(Db, Clock);
        Loans = new LoanService(Db, Clock);
        Requests = new LoanRequestService(Db, Loans);
        Access = new CircleAccessService(Db);
        Accounts = new AccountService(Db, Clock, Time, NullLogger<AccountService>.Instance);
    }

    public static async Task<TestDb> SeededAsync()
    {
        var t = new TestDb();
        await t.Db.Database.MigrateAsync();
        await SeedData.InitializeAsync(t.Db);
        return t;
    }

    public Task<Person> PersonAsync(string name) => Db.People.SingleAsync(p => p.Name == name);
    public Task<Circle> CircleAsync(string name) => Db.Circles.SingleAsync(c => c.Name == name);

    public async Task<CircleAccess> AccessAsync(string person, string circle = "The Circle") =>
        (await Access.GetAsync((await CircleAsync(circle)).Id, (await PersonAsync(person)).Id))!;

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
