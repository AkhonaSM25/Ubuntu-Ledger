// DEMO DATA ONLY. Never enable seeding in production: it creates accounts with a known PIN.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Data;

// Demo data matching the design: "The Circle", run by Zanele, with eight loans across seven members.
public static class SeedData
{
    public const string DemoPin = "1234";
    public const string TreasurerPhone = "0825550148";
    public const string MemberPhone = "0714451992";

    public static async Task InitializeAsync(LedgerDbContext db)
    {
        if (await db.People.AnyAsync()) return;

        var hasher = new PasswordHasher<Person>();
        Person P(string name, string phone, string since, bool canSignIn = false)
        {
            var p = new Person { Name = name, Phone = PhoneNumber.Normalize(phone), CreatedOn = DateOnly.Parse(since) };
            if (canSignIn) p.PinHash = hasher.HashPassword(p, DemoPin);
            return p;
        }

        var zanele = P("Zanele Maseko", TreasurerPhone, "2023-08-01", canSignIn: true);
        var thandi = P("Thandi Mokoena", "082 114 3390", "2024-02-10");
        var sipho = P("Sipho Dlamini", MemberPhone, "2023-09-02", canSignIn: true);
        var nomsa = P("Nomsa Khumalo", "083 220 6718", "2024-05-18");
        var lerato = P("Lerato Molefe", "060 913 4427", "2025-01-12");
        var bongani = P("Bongani Ndlovu", "072 508 3361", "2023-11-25");
        var themba = P("Themba Zulu", "084 377 0145", "2025-03-08");
        var palesa = P("Palesa Nkosi", "079 662 8830", "2025-06-21");
        Person[] members = [thandi, sipho, nomsa, lerato, bongani, themba, palesa];
        db.People.Add(zanele);
        db.People.AddRange(members);

        var circle = new Circle { Name = "The Circle", CreatedOn = new DateOnly(2023, 8, 1) };
        circle.Memberships.Add(new Membership { Person = zanele, Role = CircleRole.Treasurer, JoinedOn = circle.CreatedOn });
        foreach (var m in members)
            circle.Memberships.Add(new Membership { Person = m, Role = CircleRole.Member, JoinedOn = m.CreatedOn });
        db.Circles.Add(circle);

        // A second circle, run by Thandi, that has invited Zanele.
        var savers = new Circle { Name = "Mokoena Savers", CreatedOn = new DateOnly(2025, 2, 1) };
        savers.Memberships.Add(new Membership { Person = thandi, Role = CircleRole.Treasurer, JoinedOn = savers.CreatedOn });
        db.Circles.Add(savers);
        db.Invitations.Add(new Invitation { Circle = savers, Name = zanele.Name, Phone = zanele.Phone, InvitedBy = thandi, CreatedAt = new DateTime(2026, 10, 10), Token = Tokens.New() });
        db.Invitations.Add(new Invitation { Circle = circle, Name = "Ayanda Cele", Phone = PhoneNumber.Normalize("076 118 4420"), InvitedBy = zanele, CreatedAt = new DateTime(2026, 10, 12), Token = Tokens.New() });

        Loan L(string reference, Person who, decimal principal, decimal rate, InterestType type, string start, int term, params (string d, decimal a, PaymentMethod m, string? n)[] pays)
        {
            var loan = new Loan
            {
                Reference = reference, Circle = circle, Borrower = who, Principal = principal, RatePercent = rate, InterestType = type,
                StartDate = DateOnly.Parse(start), Term = term, CreatedAt = DateTime.Parse(start),
            };
            foreach (var p in pays)
                loan.Repayments.Add(new Repayment { Date = DateOnly.Parse(p.d), Amount = p.a, Method = p.m, Note = p.n, RecordedAt = DateTime.Parse(p.d) });
            return loan;
        }

        var eft = PaymentMethod.EFT;
        var cash = PaymentMethod.Cash;
        var simple = InterestType.Simple;
        Loan[] loans =
        [
            L("UL-0101", thandi, 6000, 10, simple, "2026-05-10", 6, ("2026-06-10", 1100, eft, "June instalment"), ("2026-07-12", 1100, cash, null), ("2026-08-15", 1100, eft, "Paid late, called ahead")),
            L("UL-0102", lerato, 1500, 10, simple, "2026-06-01", 3, ("2026-07-01", 550, eft, null), ("2026-08-01", 550, eft, null), ("2026-09-01", 550, cash, "Final payment")),
            L("UL-0103", sipho, 5000, 10, simple, "2026-06-05", 5, ("2026-07-05", 1100, eft, null), ("2026-08-06", 1100, cash, null), ("2026-09-05", 1100, eft, null), ("2026-10-06", 1100, cash, "October instalment")),
            L("UL-0104", themba, 1200, 5, InterestType.Monthly, "2026-07-15", 4, ("2026-08-15", 360, cash, null), ("2026-09-15", 360, eft, null)),
            L("UL-0105", nomsa, 2500, 8, simple, "2026-08-12", 3, ("2026-09-12", 900, eft, null), ("2026-10-12", 900, eft, "Paid early")),
            L("UL-0106", palesa, 500, 10, simple, "2026-08-20", 2),
            L("UL-0107", bongani, 8000, 10, simple, "2026-09-17", 8),
            L("UL-0108", sipho, 3000, 10, simple, "2026-09-20", 3),
        ];

        // Two payments mid-confirmation, so the demo shows both directions of the two-party flow:
        // Sipho says he paid UL-0108 (Zanele has to confirm), and Zanele says she received UL-0103's last instalment (Sipho has to confirm).
        loans[7].Repayments.Add(new Repayment
        {
            Date = DateOnly.Parse("2026-10-14"), Amount = 1100, Method = eft, Reference = "SIPHO OCT",
            SubmittedBy = PaymentSide.Payer, Status = PaymentStatus.Pending, RecordedAt = DateTime.Parse("2026-10-14"),
        });
        loans[2].Repayments.Add(new Repayment
        {
            Date = DateOnly.Parse("2026-10-14"), Amount = 1100, Method = cash, Note = "Final instalment",
            SubmittedBy = PaymentSide.Collector, Status = PaymentStatus.Pending, RecordedAt = DateTime.Parse("2026-10-14"),
        });

        // A request waiting on the treasurer, so the demo shows the loan request flow from the first sign-in.
        db.LoanRequests.Add(new LoanRequest
        {
            Circle = circle, Requester = lerato, Amount = 2000, Term = 4, Reason = "Stock for my stall",
            Status = LoanRequestStatus.Pending, CreatedAt = DateTime.Parse("2026-10-14"),
        });

        db.Loans.AddRange(loans);
        await db.SaveChangesAsync();

        // Ids exist now; stamp who recorded the seeded entries.
        foreach (var loan in loans)
        {
            loan.CreatedById = zanele.Id;
            foreach (var r in loan.Repayments) r.RecordedById = r.SubmittedBy == PaymentSide.Payer ? sipho.Id : zanele.Id;
        }
        await db.SaveChangesAsync();
    }
}
