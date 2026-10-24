using System.ComponentModel.DataAnnotations;
using UbuntuLedger.Models;

namespace UbuntuLedger.ViewModels;

public class CirclesViewModel
{
    public required Person Me { get; init; }
    public required IReadOnlyList<Membership> Circles { get; init; }
    public required IReadOnlyList<Invitation> Invitations { get; init; }
}

public class PersonInput
{
    public string? Name { get; set; }
    public string Phone { get; set; } = "";
}

public class CreateCircleViewModel
{
    [Required(ErrorMessage = "Give your circle a name.")]
    [StringLength(80)]
    public string Name { get; set; } = "";

    // The person being typed in, before "Add person".
    public string? NewName { get; set; }
    public string? NewPhone { get; set; }

    public List<PersonInput> People { get; set; } = [];
}

public class InviteViewModel
{
    public string? Name { get; set; }

    [Required(ErrorMessage = "Enter a phone number.")]
    public string Phone { get; set; } = "";

    public IReadOnlyList<Invitation> Pending { get; set; } = [];
}
