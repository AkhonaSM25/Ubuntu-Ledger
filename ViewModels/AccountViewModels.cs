using System.ComponentModel.DataAnnotations;

namespace UbuntuLedger.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your phone number.")]
    [Display(Name = "Phone number")]
    public string Phone { get; set; } = "";

    // Older accounts may still have a 4-digit PIN. New and reset PINs are 6 digits (see PinPolicy).
    [Required(ErrorMessage = "Enter your PIN.")]
    [RegularExpression(@"^\d{4,6}$", ErrorMessage = "Your PIN is 4 to 6 digits.")]
    public string Pin { get; set; } = "";

    public string? ReturnUrl { get; set; }

    // Only true when demo data is on: shows the demo account shortcuts.
    public bool ShowDemo { get; set; }
}

// The join page: who is inviting, and the three things the new person fills in.
public class JoinViewModel
{
    public string CircleName { get; set; } = "";
    public string InvitedBy { get; set; } = "";
    public string Phone { get; set; } = "";

    public string? Name { get; set; }
    public string? Pin { get; set; }
    public string? ConfirmPin { get; set; }
    public bool Consent { get; set; }
}

public class ResetPinViewModel
{
    public string Name { get; set; } = "";
    public string? Pin { get; set; }
    public string? ConfirmPin { get; set; }
}
