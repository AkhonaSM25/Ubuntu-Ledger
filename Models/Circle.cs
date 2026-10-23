using System.ComponentModel.DataAnnotations;

namespace UbuntuLedger.Models;

// A private lending group. Its treasurer records every loan and repayment.
public class Circle
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = "";

    public DateOnly CreatedOn { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
}

public class Membership
{
    public int Id { get; set; }
    public int CircleId { get; set; }
    public Circle Circle { get; set; } = null!;
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public CircleRole Role { get; set; }
    public DateOnly JoinedOn { get; set; }
}

// An invite by phone number. It shows on the invitee's circles page once they sign in with that number.
public class Invitation
{
    public int Id { get; set; }
    public int CircleId { get; set; }
    public Circle Circle { get; set; } = null!;

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Required, StringLength(20)]
    public string Phone { get; set; } = "";

    public int InvitedById { get; set; }
    public Person InvitedBy { get; set; } = null!;
    public InvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
