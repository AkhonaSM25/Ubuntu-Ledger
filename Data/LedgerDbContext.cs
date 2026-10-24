using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Models;

namespace UbuntuLedger.Data;

public class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Circle> Circles => Set<Circle>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Repayment> Repayments => Set<Repayment>();
    public DbSet<LoanRequest> LoanRequests => Set<LoanRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Person>().HasIndex(p => p.Phone).IsUnique();

        b.Entity<Membership>().HasIndex(m => new { m.CircleId, m.PersonId }).IsUnique();
        b.Entity<Membership>().HasOne(m => m.Circle).WithMany(c => c.Memberships).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Membership>().HasOne(m => m.Person).WithMany(p => p.Memberships).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Invitation>().HasIndex(i => new { i.Phone, i.Status });
        b.Entity<Invitation>().HasOne(i => i.InvitedBy).WithMany().OnDelete(DeleteBehavior.Restrict);

        b.Entity<Loan>().HasIndex(l => l.Reference).IsUnique();
        b.Entity<Loan>().HasOne(l => l.Borrower).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Loan>().Property(l => l.Principal).HasPrecision(18, 2);
        b.Entity<Loan>().Property(l => l.RatePercent).HasPrecision(9, 4);

        b.Entity<Repayment>().Property(r => r.Amount).HasPrecision(18, 2);

        b.Entity<LoanRequest>().Property(r => r.Amount).HasPrecision(18, 2);
        b.Entity<LoanRequest>().HasIndex(r => new { r.CircleId, r.Status });
        b.Entity<LoanRequest>().HasOne(r => r.Requester).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}
