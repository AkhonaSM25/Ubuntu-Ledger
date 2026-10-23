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

    public DateOnly CreatedOn { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();

    public string Initials => PersonExtensions.InitialsOf(Name);
    public string FirstName => Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Name;
}
