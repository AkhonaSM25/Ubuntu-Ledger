using System.ComponentModel.DataAnnotations;

namespace UbuntuLedger.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your phone number.")]
    [Display(Name = "Phone number")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Enter your PIN.")]
    [RegularExpression(@"^\d{4,6}$", ErrorMessage = "Your PIN is 4 to 6 digits.")]
    public string Pin { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
