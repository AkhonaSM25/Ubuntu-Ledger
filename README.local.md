# Ubuntu Ledger

A ledger for lending circles (stokvels): treasurers record loans and repayments, and members see their own balances.
ASP.NET Core MVC (.NET 10) with EF Core and SQLite.

```
dotnet run                          # http://localhost:5080 (creates and seeds ubuntuledger.db)
dotnet test UbuntuLedger.slnx       # 20 tests
```

Demo sign-in (PIN 1234 for both):
- Treasurer: Zanele Maseko, 082 555 0148
- Member: Sipho Dlamini, 071 445 1992

In Development, `Ledger:Today` is pinned to 2026-10-15 so the demo data shows the same overdue and due-soon states as the design.
Remove it from `appsettings.Development.json` to use the real date.

## Structure

- `Models/`: Person, Circle, Membership, Invitation, Loan, Repayment, plus formatting helpers
- `Services/`: `LoanCalculator` (interest, schedule, status), `LoanService`, `CircleService`, `CircleAccess` (who can see and change what)
- `Controllers/`: Account, Circles, Dashboard, Members, Loans, Repayments, Reports
- `Views/`: Razor views; `Shared/_CircleLayout` is the sidebar / bottom-nav shell
- `Data/`: `LedgerDbContext`, migrations and seed data
- `wwwroot/css/site.css`: design tokens and component styles; `wwwroot/js/site.js`: live previews (optional)
- `UbuntuLedger.Tests/`: xUnit tests for the loan maths and business rules
