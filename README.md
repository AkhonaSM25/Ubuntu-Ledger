# Ubuntu Ledger

A digital record book for stokvel-style lending circles. A circle's treasurer records loans and repayments, and every member can see their own balances, so there is one trusted record and fewer disputes.

Built for the FNB App of the Year Hackathon.

**Stack:** ASP.NET Core MVC (.NET 10), Razor views, Entity Framework Core, SQLite, xUnit.

## Features
- Phone number + PIN sign-in
- Create circles and invite members by phone number
- Role-based access: treasurers manage everything, members see only their own loans
- Loans with flat or per-month interest, monthly instalments and due dates
- Repayment recording with a running balance, and overdue / due-soon status
- Per-loan ledger and CSV export
- Monthly reports with a printable view

## Run it
```bash
dotnet run                      # http://localhost:5080 (creates and seeds ubuntuledger.db)
dotnet test UbuntuLedger.slnx   # unit and service tests
```

Demo sign-in (PIN 1234 for both):
- Treasurer: Zanele Maseko, 082 555 0148
- Member: Sipho Dlamini, 071 445 1992

All demo people and numbers are invented. Demo data loads only when `Seed:DemoData` is `true`.

## Project structure
- `Models/` entities and formatting helpers
- `Services/` loan maths, circle and loan services, access rules
- `Controllers/` and `Views/` the MVC web layer
- `Data/` EF Core context, migrations and seed data
- `UbuntuLedger.Tests/` tests for the loan maths and business rules

## Roadmap
- Borrower confirmation of each repayment (two-sided acceptance)
- Self-service sign-up and PIN setup for invited members
- Void and reversal entries for corrections
- Login lockout and SMS verification
