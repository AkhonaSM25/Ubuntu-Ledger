namespace UbuntuLedger.Services;

// A proof-of-payment file that has been checked and saved.
public record StoredProof(string Path, string FileName, string ContentType);

// Keeps proof-of-payment files outside wwwroot, so nothing is public. They are only ever served by
// PaymentsController, after it has checked that the signed-in person can see the loan.
public class ProofStore(IWebHostEnvironment env)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".pdf"] = "application/pdf",
    };

    private string Folder => System.IO.Path.Combine(env.ContentRootPath, "App_Data", "proofs");

    public async Task<(StoredProof? Proof, string? Error)> SaveAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file.Length == 0) return (null, "That file is empty.");
        if (file.Length > MaxBytes) return (null, "The proof must be 5 MB or smaller.");

        var extension = System.IO.Path.GetExtension(file.FileName);
        if (!Types.TryGetValue(extension, out var contentType)) return (null, "Upload a JPG, PNG or PDF.");

        await using var input = file.OpenReadStream();
        var head = new byte[8];
        var read = 0;
        while (read < head.Length)
        {
            var n = await input.ReadAsync(head.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }
        // The extension is chosen by the sender, so check the first bytes really match the type.
        if (!LooksLike(contentType, head.AsSpan(0, read))) return (null, "That file is not a real JPG, PNG or PDF.");

        Directory.CreateDirectory(Folder);
        var stored = Guid.NewGuid().ToString("N") + extension.ToLowerInvariant();
        await using (var output = File.Create(System.IO.Path.Combine(Folder, stored)))
        {
            await output.WriteAsync(head.AsMemory(0, read), ct);
            await input.CopyToAsync(output, ct);
        }

        var shown = System.IO.Path.GetFileName(file.FileName);
        if (shown.Length > 100) shown = shown[..100];
        return (new StoredProof(stored, shown, contentType), null);
    }

    public Stream? Open(string storedName)
    {
        // Stored names are our own GUIDs; GetFileName makes sure a stored value can never walk out of the folder.
        var path = System.IO.Path.Combine(Folder, System.IO.Path.GetFileName(storedName));
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(string storedName)
    {
        var path = System.IO.Path.Combine(Folder, System.IO.Path.GetFileName(storedName));
        if (File.Exists(path)) File.Delete(path);
    }

    private static bool LooksLike(string contentType, ReadOnlySpan<byte> head) => contentType switch
    {
        "image/jpeg" => head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
        "image/png" => head.Length >= 4 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47,
        "application/pdf" => head.Length >= 4 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46,
        _ => false,
    };
}
