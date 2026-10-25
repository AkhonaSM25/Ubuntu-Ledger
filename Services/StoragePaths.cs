namespace UbuntuLedger.Services;

// Where the app keeps files that must not be lost: proofs of payment and backups. By default that is App_Data
// next to the app, which is fine on a laptop. On a host that replaces the app folder when you deploy (Azure App
// Service does), set Storage:DataPath to a folder that survives deploys. See docs/DEPLOYMENT.md.
public class StoragePaths(IWebHostEnvironment env, IConfiguration config)
{
    public string Root => config["Storage:DataPath"] is { Length: > 0 } configured
        ? Path.GetFullPath(configured, env.ContentRootPath)
        : Path.Combine(env.ContentRootPath, "App_Data");

    public string Proofs => Path.Combine(Root, "proofs");
    public string Backups => Path.Combine(Root, "backups");
}
