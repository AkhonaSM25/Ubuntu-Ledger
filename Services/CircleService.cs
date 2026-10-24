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
            .OrderByDescending(m => m.Role).ThenBy(m => m.Circle.Name)
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

    public Task<List<Invitation>> PendingForCircleAsync(int circleId) =>
        db.Invitations.Where(i => i.CircleId == circleId && i.Status == InvitationStatus.Pending)
            .OrderBy(i => i.CreatedAt).ToListAsync();

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
        if (accept && !await db.Memberships.AnyAsync(m => m.CircleId == invite.CircleId && m.PersonId == me.Id))
            db.Memberships.Add(new Membership { CircleId = invite.CircleId, PersonId = me.Id, Role = CircleRole.Member, JoinedOn = clock.Today });
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
    };
}
