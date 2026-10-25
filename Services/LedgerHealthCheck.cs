using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using UbuntuLedger.Data;

namespace UbuntuLedger.Services;

// Behind /health. Unhealthy when the database cannot be reached; degraded when the last backup failed.
public class LedgerHealthCheck(LedgerDbContext db, BackupStatus backups) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("The database cannot be reached.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The database check failed.", ex);
        }

        return backups.LastError is null
            ? HealthCheckResult.Healthy("OK")
            : HealthCheckResult.Degraded("The last backup failed: " + backups.LastError);
    }
}
