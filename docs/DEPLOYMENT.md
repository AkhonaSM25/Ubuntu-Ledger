# Deploying Ubuntu Ledger to Azure App Service

Settings are environment variables (App Service > Environment variables). Double underscores stand for `:` in config names.

## Required
| Setting | Value | Why |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `Data Source=/home/data/ubuntuledger.db` | `/home` is persistent on App Service; the app folder is replaced on every deploy |
| `Storage__DataPath` | `/home/data` | Payment proofs and local backups live here |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Keeps demo data and the developer error page off |

**Keep it to one instance.** SQLite is a single file; scaling out needs [PostgreSQL](postgres-migration.md).

## Demo data
`Seed__DemoData` defaults to `false`. Set it to `true` **only** on the demo site. Never on a site holding real circles. The app logs a warning if demo data is on outside Development. `Ledger__Today=2026-10-15` pins "today" to the date the demo data was written for.

## Backups
A background job copies the database (SQLite online backup) and zips the proofs on a schedule.

| Setting | Default |
|---|---|
| `Backup__Enabled` | `true` |
| `Backup__IntervalHours` | `24` |
| `Backup__KeepLocal` | `7` |
| `Backup__BlobConnectionString` | empty. Set to a Storage account connection string to also upload each backup |
| `Backup__BlobContainer` | `ubuntu-ledger-backups` |

Local backups sit on the same disk as the data, so they do not survive losing the app. Use blob upload for real protection, and add a lifecycle rule on the container (for example delete after 90 days). Restore by stopping the app and replacing the database file and proofs folder from a backup.

## Monitoring
- `GET /health` reports whether the database is reachable. Point App Service **Health check** at it.
- Set `APPLICATIONINSIGHTS_CONNECTION_STRING` and the app sends logs, requests and failures to Application Insights. Without it, nothing is sent.
- Add an alert on failed requests (5xx) and on the health check.

## Cost control
Create a **budget** with an email alert in Cost Management (for example at 50% and 90%). On Azure for Students subscriptions budgets and some alerts may be limited, so also check the credit balance on the portal home page.

## Other settings
| Setting | Purpose |
|---|---|
| `RateLimit__LoginPerMinute` | Sign-in attempts per minute per client |
| `Legal__ContactEmail` | Contact shown in the privacy notice. **Set this before launch.** |

## Before real users
- Read and have the privacy notice and terms reviewed. They are drafts, not legal advice.
- Confirm HTTPS only is on and the domain is final.
