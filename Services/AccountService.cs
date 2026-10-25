using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

public record LoginResult(Person? Person, string? Error);
public record JoinResult(Person? Person, int CircleId, string? Error);

// Everything about who a person is: signing in, joining through an invite link, and PIN resets.
public class AccountService(LedgerDbContext db, LedgerClock clock, TimeProvider time, ILogger<AccountService> log)
{
    // Five wrong PINs in a row lock the account for fifteen minutes.
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

    // A reset link from the treasurer works once, for two days.
    public static readonly TimeSpan ResetLinkLife = TimeSpan.FromHours(48);

    private static readonly PasswordHasher<Person> Hasher = new();

    // Checked when the phone number is unknown, so a wrong number takes as long to answer as a wrong PIN.
    private static readonly string DummyHash = Hasher.HashPassword(new Person(), "not-a-real-pin");

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ---- Signing in ----

    public async Task<LoginResult> SignInAsync(string? phoneInput, string? pin)
    {
        var phone = PhoneNumber.Normalize(phoneInput);
        // Not tracked: the counters below are written straight to the database, so a tracked copy would go stale.
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Phone == phone);
        if (person?.PinHash is null)
        {
            Hasher.VerifyHashedPassword(new Person(), DummyHash, pin ?? "");
            return Mismatch();
        }

        var now = Now;
        if (person.LockedUntil is { } until && until > now)
        {
            log.LogWarning("Sign-in refused for person {PersonId}: locked until {Until:u}", person.Id, until);
            return new LoginResult(null, LockedMessage(until - now));
        }

        var verdict = Hasher.VerifyHashedPassword(person, person.PinHash, pin ?? "");
        if (verdict == PasswordVerificationResult.Failed)
        {
            // Counted in the database, not on this copy of the person, so parallel guesses cannot lose a count.
            await db.People.Where(p => p.Id == person.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.FailedPinAttempts, p => p.FailedPinAttempts + 1));
            var attempts = await db.People.Where(p => p.Id == person.Id).Select(p => p.FailedPinAttempts).FirstAsync();
            if (attempts >= MaxFailedAttempts)
            {
                var lockedUntil = now + LockFor;
                await db.People.Where(p => p.Id == person.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.FailedPinAttempts, 0)
                        .SetProperty(p => p.LockedUntil, (DateTime?)lockedUntil));
                log.LogWarning("Person {PersonId} locked out after {Attempts} wrong PINs", person.Id, attempts);
                return new LoginResult(null, LockedMessage(LockFor));
            }
            log.LogInformation("Wrong PIN for person {PersonId} ({Attempts} of {Max})", person.Id, attempts, MaxFailedAttempts);
            return Mismatch();
        }

        await db.People.Where(p => p.Id == person.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.FailedPinAttempts, 0)
                .SetProperty(p => p.LockedUntil, (DateTime?)null));
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var upgraded = Hasher.HashPassword(person, pin!);
            await db.People.Where(p => p.Id == person.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.PinHash, upgraded));
        }
        return new LoginResult(person, null);
    }

    private static LoginResult Mismatch() => new(null, "That phone number and PIN do not match.");

    private static string LockedMessage(TimeSpan left)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
        return $"Too many wrong PINs. Try again in {Format.Plural(minutes, "minute")}. If you forgot your PIN, ask your treasurer to reset it.";
    }

    // ---- Joining through an invite link ----

    // The waiting invite behind a join link, or null when the link is wrong, used up or cancelled.
    public async Task<Invitation?> FindJoinInviteAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 32) return null;
        return await db.Invitations.Include(i => i.Circle).Include(i => i.InvitedBy)
            .FirstOrDefaultAsync(i => i.Token == token && i.Status == InvitationStatus.Pending);
    }

    public Task<bool> HasAccountAsync(string phone) => db.People.AnyAsync(p => p.Phone == phone && p.PinHash != null);

    // Creates the account for the invited phone number, sets the PIN and puts the person in the circle.
    public async Task<JoinResult> JoinAsync(string? token, string? name, string? pin, string? confirmPin, bool consent)
    {
        var invite = await FindJoinInviteAsync(token);
        if (invite is null) return Failed("This invite link is no longer valid. Ask your treasurer to send a new one.");

        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100) return Failed("Enter your name, up to 100 characters.");
        var pinError = PinPolicy.Check(pin);
        if (pinError is not null) return Failed(pinError);
        if (pin != confirmPin) return Failed("The two PINs do not match.");
        if (!consent) return Failed("Please agree to the terms and the privacy notice to continue.");

        var person = await db.People.FirstOrDefaultAsync(p => p.Phone == invite.Phone);
        if (person?.PinHash is not null)
            return Failed("This phone number already has an account. Sign in to accept the invite.");

        var now = Now;
        if (person is null)
        {
            person = new Person { Name = name, Phone = invite.Phone, CreatedOn = clock.Today };
            db.People.Add(person);
        }
        else person.Name = name;

        person.PinHash = Hasher.HashPassword(person, pin!);
        person.PinChangedAt = now;
        person.ConsentedAt = now;
        invite.Status = InvitationStatus.Accepted;

        var alreadyIn = person.Id != 0 && await db.Memberships.AnyAsync(m => m.CircleId == invite.CircleId && m.PersonId == person.Id);
        if (!alreadyIn)
            db.Memberships.Add(new Membership { CircleId = invite.CircleId, Person = person, Role = CircleRole.Member, JoinedOn = clock.Today });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Two people opening the same link at once: the phone number is unique, so only one account is made.
            log.LogWarning(ex, "Join failed for invitation {InvitationId}", invite.Id);
            return Failed("Something went wrong while creating your account. Please try the link again.");
        }
        log.LogInformation("Person {PersonId} joined circle {CircleId} from an invite link", person.Id, invite.CircleId);
        return new JoinResult(person, invite.CircleId, null);
    }

    private static JoinResult Failed(string error) => new(null, 0, error);

    // ---- Changing your own PIN ----

    // Checks the current PIN the same way sign-in does (so wrong guesses count towards the lockout), then sets the new one.
    public async Task<OperationResult> ChangePinAsync(int personId, string? currentPin, string? newPin, string? confirmPin)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == personId);
        if (person is null) return OperationResult.Fail("Your account was not found.");

        var check = await SignInAsync(person.Phone, currentPin);
        if (check.Person is null)
            return OperationResult.Fail(check.Error!.StartsWith("Too many") ? check.Error : "Your current PIN is not right.");

        var pinError = PinPolicy.Check(newPin);
        if (pinError is not null) return OperationResult.Fail(pinError);
        if (newPin != confirmPin) return OperationResult.Fail("The two new PINs do not match.");
        if (newPin == currentPin) return OperationResult.Fail("Choose a PIN different from your current one.");

        // SignInAsync reads without tracking, so load the person again to save the new hash.
        db.ChangeTracker.Clear();
        person = await db.People.FirstAsync(p => p.Id == personId);
        person.PinHash = Hasher.HashPassword(person, newPin!);
        person.PinChangedAt = Now;
        person.PinResetTokenHash = null;
        person.PinResetExpiresAt = null;
        await db.SaveChangesAsync();
        log.LogInformation("Person {PersonId} changed their PIN", person.Id);
        return OperationResult.Ok(person.Id);
    }

    // ---- Treasurer-assisted PIN reset ----

    // A one-time link for a member who forgot their PIN. Returns the secret once; only its hash is kept.
    // Accounts are shared across circles, so a treasurer can only reset someone who belongs to no other circle.
    public async Task<(string? Token, string? Error)> CreatePinResetAsync(CircleAccess access, int personId)
    {
        // The treasurer resets members. The backup treasurer can reset one account only: the treasurer's own.
        var backupForTreasurer = access.IsBackup && personId == access.TreasurerId;
        if (!access.IsTreasurer && !backupForTreasurer) return (null, "Only the treasurer can reset a PIN.");
        if (personId == access.Me.Id) return (null, "You cannot reset your own PIN here.");

        var person = await db.People.Include(p => p.Memberships)
            .FirstOrDefaultAsync(p => p.Id == personId && p.Memberships.Any(m => m.CircleId == access.Circle.Id));
        if (person is null) return (null, "That member was not found.");
        if (person.Memberships.Any(m => m.CircleId != access.Circle.Id))
            return (null, $"{person.FirstName} is also in another circle, so their PIN cannot be reset from here.");

        var token = Tokens.New();
        person.PinResetTokenHash = Tokens.Hash(token);
        person.PinResetExpiresAt = Now + ResetLinkLife;
        await db.SaveChangesAsync();
        log.LogInformation("Treasurer {TreasurerId} created a PIN reset link for person {PersonId}", access.Me.Id, person.Id);
        return (token, null);
    }

    // The person a reset link belongs to, or null when it is wrong, expired or already used.
    public async Task<Person?> FindResetAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;
        var hash = Tokens.Hash(token);
        var now = Now;
        return await db.People.FirstOrDefaultAsync(p => p.PinResetTokenHash == hash && p.PinResetExpiresAt > now);
    }

    public async Task<OperationResult> ResetPinAsync(string? token, string? pin, string? confirmPin)
    {
        var person = await FindResetAsync(token);
        if (person is null) return OperationResult.Fail("This reset link has expired or was already used. Ask your treasurer for a new one.");

        var pinError = PinPolicy.Check(pin);
        if (pinError is not null) return OperationResult.Fail(pinError);
        if (pin != confirmPin) return OperationResult.Fail("The two PINs do not match.");

        person.PinHash = Hasher.HashPassword(person, pin!);
        person.PinChangedAt = Now;
        person.PinResetTokenHash = null;
        person.PinResetExpiresAt = null;
        person.FailedPinAttempts = 0;
        person.LockedUntil = null;
        await db.SaveChangesAsync();
        log.LogInformation("Person {PersonId} set a new PIN from a reset link", person.Id);
        return OperationResult.Ok(person.Id);
    }
}
