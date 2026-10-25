# Moving from SQLite to PostgreSQL

SQLite fits a single small instance. Move when you need more than one instance, concurrent heavy writes, or managed backups.

1. **Provider.** Add `Npgsql.EntityFrameworkCore.PostgreSQL` and switch `UseSqlite` to `UseNpgsql` in `Program.cs`, reading the provider from config so tests can keep SQLite.
2. **Migrations.** The current migrations are SQLite-shaped. Generate a fresh initial migration for PostgreSQL rather than replaying them.
3. **Money.** Columns holding amounts become `numeric(18,2)`. Then the dashboard totals in `LoanService.SnapshotAsync`, which are summed in memory today because SQLite cannot sum decimals reliably, can become SQL aggregates.
4. **Data.** Export tables from SQLite (CSV) and load them in dependency order: People, Circles, Memberships, Invitations, Loans, Repayments. Compare row counts and total principal before and after.
5. **Proofs.** Move the proofs folder to Blob Storage behind `ProofStore`; instances can no longer share a local disk.
6. **Backups.** Replace the file-copy job with the managed server's point-in-time restore.
7. **Cutover.** Stop writes, take a final backup, load, verify, switch the connection string.
