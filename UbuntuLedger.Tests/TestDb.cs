using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UbuntuLedger.Data;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

/// <summary>
/// A throw-away SQLite in-memory database with the real EF model, the demo seed and the real services.
/// "Today" is pinned to 15 Oct 2026, the date the demo data was written for. Each test builds its own instance.
/// </summary>
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
