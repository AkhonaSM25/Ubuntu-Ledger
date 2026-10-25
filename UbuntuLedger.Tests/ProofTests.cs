using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

public sealed class ProofFolder : IDisposable
{
    private sealed class Env(string root) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "UbuntuLedger.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ul-proofs-" + Guid.NewGuid().ToString("N"));
    public StoragePaths Paths { get; }
    public ProofStore Store { get; }

    public ProofFolder()
    {
        Directory.CreateDirectory(Root);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataPath"] = Root }).Build();
        Paths = new StoragePaths(new Env(Root), config);
        Store = new ProofStore(Paths);
    }

    public static IFormFile File(byte[] bytes, string name)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "proof", name);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}

public class ProofTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4 test");

    [Fact]
    public async Task A_real_png_or_pdf_is_saved_under_a_random_name_inside_the_proofs_folder()
    {
        using var f = new ProofFolder();
        var (png, pngError) = await f.Store.SaveAsync(ProofFolder.File(Png, "slip.PNG"));
        var (pdf, pdfError) = await f.Store.SaveAsync(ProofFolder.File(Pdf, "receipt.pdf"));

        Assert.Null(pngError);
        Assert.Null(pdfError);
        Assert.Equal("image/png", png!.ContentType);
        Assert.Equal("application/pdf", pdf!.ContentType);
        Assert.NotEqual("slip.PNG", png.Path);   // never stored under the sender's file name
        Assert.True(File.Exists(Path.Combine(f.Paths.Proofs, png.Path)));
        using var opened = f.Store.Open(png.Path);
        Assert.NotNull(opened);
    }

    [Fact]
    public async Task Fake_empty_oversized_and_wrong_type_files_are_refused()
    {
        using var f = new ProofFolder();
        Assert.NotNull((await f.Store.SaveAsync(ProofFolder.File(Encoding.ASCII.GetBytes("<script>alert(1)</script>"), "x.png"))).Error);  // not really a PNG
        Assert.NotNull((await f.Store.SaveAsync(ProofFolder.File([], "x.png"))).Error);                                                    // empty
        Assert.NotNull((await f.Store.SaveAsync(ProofFolder.File(Png, "x.exe"))).Error);                                                   // wrong type
        Assert.NotNull((await f.Store.SaveAsync(ProofFolder.File(Png, "x.html"))).Error);
        var huge = new FormFile(new MemoryStream(Png), 0, ProofStore.MaxBytes + 1, "proof", "big.png");
        Assert.NotNull((await f.Store.SaveAsync(huge)).Error);                                                                              // too big
        Assert.False(Directory.Exists(f.Paths.Proofs) && Directory.EnumerateFiles(f.Paths.Proofs).Any());                                  // nothing was kept
    }

    [Fact]
    public async Task A_stored_name_cannot_walk_out_of_the_proofs_folder()
    {
        using var f = new ProofFolder();
        await f.Store.SaveAsync(ProofFolder.File(Png, "ok.png"));   // makes sure the folder exists
        File.WriteAllText(Path.Combine(f.Root, "secret.txt"), "secret");

        Assert.Null(f.Store.Open("../secret.txt"));
        Assert.Null(f.Store.Open("..\\secret.txt"));
        Assert.Null(f.Store.Open(Path.Combine(f.Root, "secret.txt")));
    }

    [Fact]
    public async Task Only_people_who_can_see_the_loan_can_load_a_payment_and_so_its_proof()
    {
        using var t = await TestDb.SeededAsync();
        var thandi = await t.AccessAsync("Thandi Mokoena");
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var loan = await t.Db.Loans.SingleAsync(l => l.Reference == "UL-0101");
        var proof = new StoredProof("abc123.png", "slip.png", "image/png");
        var sent = await t.Loans.SubmitPaymentAsync(thandi, new NewPayment(loan.Id, 100, TestDb.Today, PaymentMethod.EFT, null, proof));
        Assert.True(sent.Succeeded);

        Assert.Equal("abc123.png", (await t.Loans.LoadPaymentAsync(thandi, sent.Id!.Value))!.ProofPath);   // the borrower
        Assert.NotNull(await t.Loans.LoadPaymentAsync(zanele, sent.Id.Value));                              // the treasurer
        Assert.Null(await t.Loans.LoadPaymentAsync(sipho, sent.Id.Value));                                  // another member
    }
}
