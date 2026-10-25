using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

// Circles, memberships and invitations.
public class CircleService(LedgerDbContext db, LedgerClock clock)
{
    public Task<List<Membership>> MyCirclesAsync(int personId) =>
        db.Memberships.Where(m => m.PersonId == personId)
            .Include(m => m.Circle).ThenInclude(c => c.Memberships)
            .OrderByDescending(m => m.Role == CircleRole.Treasurer).ThenBy(m => m.Circle.Name)
            .ToListAsync();

    public async Task<List<Invitation>> InvitationsForAsync(Person me)
    {
        var mine = await db.Memberships.Where(m => m.PersonId == me.Id).Select(m => m.CircleId).ToListAsync();
        return await db.Invitations
            .Where(i => i.Phone == me.Phone && i.Status == InvitationStatus.Pending && !mine.Contains(i.CircleId))
            .Include(i => i.InvitedBy)
            .Include(i => i.Circle).ThenInclude(c => c.Memberships)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    // Invites from before join links existed get their link the first time the treasurer looks at them.
    public async Task<List<Invitation>> PendingForCircleAsync(int circleId)
    {
        var pending = await db.Invitations.Where(i => i.CircleId == circleId && i.Status == InvitationStatus.Pending)
            .OrderBy(i => i.CreatedAt).ToListAsync();
        var untokened = pending.Where(i => i.Token is null).ToList();
        if (untokened.Count > 0)
        {
            foreach (var i in untokened) i.Token = Tokens.New();
            await db.SaveChangesAsync();
        }
        return pending;
    }

    public async Task<OperationResult> CreateAsync(Person me, string name, IReadOnlyList<(string Name, string Phone)> people)
    {
        name = name.Trim();
        if (name.Length == 0) return OperationResult.Fail("Give your circle a name.");

        var circle = new Circle { Name = name, CreatedOn = clock.Today };
        circle.Memberships.Add(new Membership { PersonId = me.Id, Role = CircleRole.Treasurer, JoinedOn = clock.Today });
        foreach (var p in people.DistinctBy(p => PhoneNumber.Normalize(p.Phone)))
        {
            var phone = PhoneNumber.Normalize(p.Phone);
            if (phone.Length == 0 || phone == me.Phone) continue;
            circle.Invitations.Add(NewInvite(me, p.Name, phone));
        }
        db.Circles.Add(circle);
        await db.SaveChangesAsync();
        return OperationResult.Ok(circle.Id);
    }

    public async Task<OperationResult> InviteAsync(CircleAccess access, string? name, string phone)
    {
        if (!access.IsTreasurer) return OperationResult.Fail("Only the treasurer can invite people.");
        var digits = PhoneNumber.Normalize(phone);
        if (digits.Length < 9) return OperationResult.Fail("Enter a phone number with at least 9 digits.");

        var alreadyMember = await db.Memberships.AnyAsync(m => m.CircleId == access.Circle.Id && m.Person.Phone == digits);
        if (alreadyMember) return OperationResult.Fail("That number already belongs to someone in this circle.");
        var alreadyInvited = await db.Invitations.AnyAsync(i => i.CircleId == access.Circle.Id && i.Phone == digits && i.Status == InvitationStatus.Pending);
        if (alreadyInvited) return OperationResult.Fail("That number already has an invite waiting.");

        var invite = NewInvite(access.Me, name, digits);
        invite.CircleId = access.Circle.Id;
        db.Invitations.Add(invite);
        await db.SaveChangesAsync();
        return OperationResult.Ok(invite.Id);
    }

    public async Task<OperationResult> CancelInviteAsync(CircleAccess access, int inviteId)
    {
        if (!access.IsTreasurer) return OperationResult.Fail("Only the treasurer can cancel invites.");
        var invite = await db.Invitations.FirstOrDefaultAsync(i => i.Id == inviteId && i.CircleId == access.Circle.Id && i.Status == InvitationStatus.Pending);
        if (invite is null) return OperationResult.Fail("That invite is no longer waiting.");
        invite.Status = InvitationStatus.Cancelled;
        await db.SaveChangesAsync();
        return OperationResult.Ok(invite.Id);
    }

    // Accepting adds the person as a member; the invite must be addressed to their phone number.
    public async Task<OperationResult> RespondAsync(Person me, int inviteId, bool accept)
    {
        var invite = await db.Invitations.Include(i => i.Circle)
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.Phone == me.Phone && i.Status == InvitationStatus.Pending);
        if (invite is null) return OperationResult.Fail("That invitation is no longer available.");

        invite.Status = accept ? InvitationStatus.Accepted : InvitationStatus.Declined;
        if (accept)
        {
            // Accepting is the person's agreement to the terms and the privacy notice shown beside the button.
            me.ConsentedAt ??= DateTime.UtcNow;
            if (!await db.Memberships.AnyAsync(m => m.CircleId == invite.CircleId && m.PersonId == me.Id))
                db.Memberships.Add(new Membership { CircleId = invite.CircleId, PersonId = me.Id, Role = CircleRole.Member, JoinedOn = clock.Today });
        }
        await db.SaveChangesAsync();
        return OperationResult.Ok(invite.CircleId);
    }

    private Invitation NewInvite(Person by, string? name, string phone) => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? PhoneNumber.Format(phone) : name.Trim(),
        Phone = phone,
        InvitedById = by.Id,
        Status = InvitationStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        Token = Tokens.New(),
    };

    // ---- Leaving and removing ----

    // A member leaves, or the treasurer removes one. Nothing is erased: the loans and payments stay in the circle's
    // history. Anyone who still owes money, or has a payment waiting for an answer, has to settle that first.
    public async Task<OperationResult> RemoveMemberAsync(CircleAccess access, int personId)
    {
        var leaving = personId == access.Me.Id;
        if (!leaving && !access.IsTreasurer) return OperationResult.Fail("Only the treasurer can remove a member.");

        var membership = await db.Memberships.Include(m => m.Person)
            .FirstOrDefaultAsync(m => m.CircleId == access.Circle.Id && m.PersonId == personId);
        if (membership is null) return OperationResult.Fail("That member was not found.");
        if (membership.Role == CircleRole.Treasurer)
            return OperationResult.Fail("The treasurer cannot leave or be removed. A circle needs its treasurer.");

        var who = leaving ? "You" : membership.Person.FirstName;
        var have = leaving ? "have" : "has";
        var loans = await db.Loans.Include(l => l.Borrower).Include(l => l.Repayments)
            .Where(l => l.CircleId == access.Circle.Id && l.BorrowerId == personId && l.VoidedAt == null)
            .ToListAsync();
        if (loans.Any(l => LoanCalculator.Summarize(l, clock.Today).IsOpen))
            return OperationResult.Fail($"{who} still {have} an unpaid loan in this circle. It has to be paid off first.");
        if (loans.Any(l => l.AwaitingAcceptance))
            return OperationResult.Fail($"{who} {have} a loan waiting to be accepted. Void it first if it is no longer needed.");
        if (loans.SelectMany(l => l.Repayments).Any(r => r.Status is PaymentStatus.Pending or PaymentStatus.Disputed))
            return OperationResult.Fail($"{who} {have} a payment waiting for an answer. Settle it first.");

        // Loan requests that were still waiting are withdrawn with the membership.
        foreach (var request in await db.LoanRequests
                     .Where(r => r.CircleId == access.Circle.Id && r.RequesterId == personId && r.Status == LoanRequestStatus.Pending)
                     .ToListAsync())
            request.Status = LoanRequestStatus.Cancelled;

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync();
        return OperationResult.Ok(personId);
    }

    // The treasurer names one member as backup treasurer, or takes the role away again.
    public async Task<OperationResult> SetBackupAsync(CircleAccess access, int personId, bool appoint)
    {
        if (!access.IsTreasurer) return OperationResult.Fail("Only the treasurer can choose a backup treasurer.");
        if (personId == access.Me.Id) return OperationResult.Fail("You are already the treasurer.");

        var members = await db.Memberships.Include(m => m.Person).Where(m => m.CircleId == access.Circle.Id).ToListAsync();
        var target = members.FirstOrDefault(m => m.PersonId == personId);
        if (target is null) return OperationResult.Fail("That member was not found.");

        if (appoint)
        {
            foreach (var other in members.Where(m => m.Role == CircleRole.Backup)) other.Role = CircleRole.Member;
            target.Role = CircleRole.Backup;
        }
        else if (target.Role == CircleRole.Backup) target.Role = CircleRole.Member;
        else return OperationResult.Fail($"{target.Person.FirstName} is not the backup treasurer.");

        await db.SaveChangesAsync();
        return OperationResult.Ok(personId);
    }
}
