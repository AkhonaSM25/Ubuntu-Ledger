# Ubuntu Ledger

A digital record book for stokvel-style lending circles. A circle's treasurer records loans and repayments, and every member can see their own balances, so there is one trusted record and fewer disputes.

Built for the FNB App of the Year Hackathon.

**Stack:** ASP.NET Core MVC (.NET 10), Razor views, Entity Framework Core, SQLite, xUnit.

## Features
- Phone number + six-digit PIN sign-in, with lockout after five wrong PINs (15 minutes)
- Create circles and invite members with a personal join link, shared on WhatsApp
- Invited members create their own account and PIN, and agree to the terms and privacy notice
- Role-based access: treasurers manage the circle, members see only their own loans
- Loans with flat or per-month interest, monthly instalments and due dates
- **Two-party payment confirmation:** a repayment only changes a balance once both the payer and the person collecting have confirmed it; either side can dispute it
- WhatsApp reminders for payments waiting on someone
- Corrections without deleting anything: reverse a confirmed payment or void a loan, with a reason that stays visible in the ledger
- Treasurer-assisted PIN reset through a one-time link
- Per-loan ledger and CSV export; monthly reports with a printable view
- Privacy notice and terms pages (drafts, see [docs/data-inventory.md](docs/data-inventory.md))
- Automatic backups, a `/health` check and optional Azure Application Insights
- Accessibility work toward WCAG 2.2 AA: contrast, visible focus, skip link, keyboard-safe dialogs, labelled errors, reduced motion

## Run it
```bash
dotnet run                      # http://localhost:5080 (creates and seeds ubuntuledger.db)
dotnet test UbuntuLedger.slnx   # unit, service and web tests
```

Demo data loads only when `Seed:DemoData` is `true`. It is on in Development and **off by default everywhere else**, so a real deployment never ships with known accounts. Demo sign-in (PIN 1234, shown on the login page only while demo data is on):
- Treasurer: Zanele Maseko, 082 555 0148
- Member: Sipho Dlamini, 071 445 1992

All demo people and numbers are invented. To run a public demo site, set `Seed__DemoData=true` on that site only.

## Deploying
See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for Azure settings, persistent storage, backups, monitoring and cost alerts, and [docs/postgres-migration.md](docs/postgres-migration.md) for moving off SQLite.

## Project structure
- `Models/` entities and formatting helpers
- `Services/` loan maths, accounts, circle and loan services, access rules, backups
- `Controllers/` and `Views/` the MVC web layer
- `Data/` EF Core context, migrations and seed data
- `UbuntuLedger.Tests/` loan maths, business rules, account security and web tests
- `docs/` deployment, data inventory, migration plan

## How we will know it works
| Measure | Target |
|---|---|
| Circles that record a loan within a week of being created | most new circles |
| Repayments confirmed by both sides within 48 hours | rising month on month |
| Disputed payments that end confirmed or cancelled | nearly all |
| Treasurer time spent on month-end reporting | down, compared with the notebook or WhatsApp thread |

These are goals to measure once real circles use the app, not results.

## How it differs
| | Notebook / spreadsheet | WhatsApp group | Ubuntu Ledger |
|---|---|---|---|
| One shared record | one person holds it | scattered in chat history | yes |
| Members see only their own loans | no | no, everyone sees everything | yes |
| Balances need both sides to agree | no | informal | built in |
| Corrections leave a trail | edits overwrite | messages get lost | reversals stay in the ledger |
| Works on a basic phone browser | n/a | yes | yes |

## Revenue model (proposal, not built)
Free for small circles. A paid tier for larger circles or treasurers who run several, adding things like SMS reminders and exports. Any paid message costs would be passed on openly. This is a proposal to test with real circles.

## Roadmap
- Move to PostgreSQL for multi-instance hosting
- SMS or WhatsApp verification of phone numbers
- Treasurer succession and a backup treasurer
- Payment-provider integration for automatic proof of payment
- Native or installable mobile experience
