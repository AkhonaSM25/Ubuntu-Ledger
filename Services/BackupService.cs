using System.IO.Compression;
using Azure.Storage.Blobs;
using Microsoft.Data.Sqlite;

namespace UbuntuLedger.Services;

// What the health check reports about backups.
public class BackupStatus
{
    public DateTime? LastSuccessUtc { get; set; }
    public string? LastError { get; set; }
}

// A copy of the whole record, made on a schedule: a consistent snapshot of the database plus every proof file,
// zipped. The zip is kept on the server and, when Backup:BlobConnectionString is set, uploaded to Azure Blob
// Storage so that losing the server does not lose the circle's financial history.
public class BackupService(IConfiguration config, StoragePaths paths, BackupStatus status, ILogger<BackupService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Backup:Enabled", true))
        {
            log.LogInformation("Backups are switched off (Backup:Enabled is false).");
            return;
        }

        var every = TimeSpan.FromHours(Math.Max(1, config.GetValue("Backup:IntervalHours", 24)));
        try
        {
            // Let the app settle after a start or a deploy before the first copy.
            await Task.Delay(TimeSpan.FromMinutes(config.GetValue("Backup:StartDelayMinutes", 2)), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(every, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down.
        }
    }

    // Makes one backup. Returns the zip's path, or null when it failed (the reason is logged and shown by /health).
    public async Task<string?> RunOnceAsync(CancellationToken ct = default)
    {
        string? work = null;
        try
        {
            Directory.CreateDirectory(paths.Backups);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            work = Path.Combine(paths.Backups, "work-" + stamp);
            Directory.CreateDirectory(work);

            // SQLite's own online backup gives a consistent copy even while the app is writing.
            var databaseCopy = Path.Combine(work, "ubuntuledger.db");
            var connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            await Task.Run(() =>
            {
                using var source = new SqliteConnection(connectionString);
                using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseCopy, Pooling = false }.ToString());
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }, ct);

            var zipPath = Path.Combine(paths.Backups, $"ubuntuledger-{stamp}.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(databaseCopy, "ubuntuledger.db");
                if (Directory.Exists(paths.Proofs))
                    foreach (var file in Directory.EnumerateFiles(paths.Proofs))
                        zip.CreateEntryFromFile(file, "proofs/" + Path.GetFileName(file));
            }

            var blobConnection = config["Backup:BlobConnectionString"];
            if (!string.IsNullOrWhiteSpace(blobConnection))
            {
                var container = new BlobContainerClient(blobConnection, config["Backup:BlobContainer"] ?? "ubuntu-ledger-backups");
                await container.CreateIfNotExistsAsync(cancellationToken: ct);
                await using var stream = File.OpenRead(zipPath);
                await container.GetBlobClient(Path.GetFileName(zipPath)).UploadAsync(stream, overwrite: true, cancellationToken: ct);
                log.LogInformation("Backup {File} uploaded to blob storage.", Path.GetFileName(zipPath));
            }
            else
            {
                log.LogWarning("Backup {File} is only on this server: Backup:BlobConnectionString is not set, so losing the server loses it too.",
                    Path.GetFileName(zipPath));
            }

            Prune();
            status.LastSuccessUtc = DateTime.UtcNow;
            status.LastError = null;
            return zipPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            status.LastError = ex.GetType().Name;
            log.LogError(ex, "Backup failed");
            return null;
        }
        finally
        {
            if (work is not null && Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
    }

    // Keeps the newest few zips on the server; blob storage keeps its own history.
    private void Prune()
    {
        var keep = Math.Max(1, config.GetValue("Backup:KeepLocal", 7));
        foreach (var old in Directory.GetFiles(paths.Backups, "ubuntuledger-*.zip").OrderByDescending(f => f).Skip(keep))
            File.Delete(old);
    }
}
