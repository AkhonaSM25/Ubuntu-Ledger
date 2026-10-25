using System.ComponentModel.DataAnnotations;

namespace UbuntuLedger.Models;

// Anyone the ledger knows by phone number. People without a PIN have been added to a circle but never signed in.
public class Person
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    // Stored without spaces so lookups are exact; see PhoneNumber.Normalize.
    [Required, StringLength(20)]
    public string Phone { get; set; } = "";

    public string? PinHash { get; set; }

    // Sign-in protection: five wrong PINs in a row lock the account for a while (see AccountService).
    public int FailedPinAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? PinChangedAt { get; set; }

    // A one-time link the treasurer hands over when a member forgets their PIN. Only a hash of it is stored.
    [StringLength(64)]
    public string? PinResetTokenHash { get; set; }
    public DateTime? PinResetExpiresAt { get; set; }

    // When the person agreed to the terms and the privacy notice.
    public DateTime? ConsentedAt { get; set; }

    public DateOnly CreatedOn { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();

    public string Initials => PersonExtensions.InitialsOf(Name);
    public string FirstName => Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Name;
}
