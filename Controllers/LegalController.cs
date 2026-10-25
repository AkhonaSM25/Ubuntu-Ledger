using Microsoft.AspNetCore.Mvc;

namespace UbuntuLedger.Controllers;

// The terms and the privacy notice. Public on purpose: people read them before they have an account.
public class LegalController : Controller
{
    [HttpGet("/privacy")]
    public IActionResult Privacy() => View();

    [HttpGet("/terms")]
    public IActionResult Terms() => View();
}
