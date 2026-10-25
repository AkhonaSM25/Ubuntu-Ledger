using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using UbuntuLedger.Data;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

public class AccountController(AccountService accounts, CircleService circles, IConfiguration config) : Controller
{
    // The demo accounts (with their published PIN) only exist when demo data is switched on.
    private bool DemoMode => config.GetValue("Seed:DemoData", false);

    [HttpGet("/")]
    public IActionResult Root() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Circles") : RedirectToAction(nameof(Login));

    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl) => View(new LoginViewModel
    {
        Phone = DemoMode ? PhoneNumber.Format(SeedData.TreasurerPhone) : "",
        ReturnUrl = returnUrl,
        ShowDemo = DemoMode,
    });

    [HttpPost("/login"), ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        model.ShowDemo = DemoMode;
        if (!ModelState.IsValid) return View(model);

        var result = await accounts.SignInAsync(model.Phone, model.Pin);
        if (result.Person is not { } person)
        {
            ModelState.AddModelError("", result.Error!);
            return View(model);
        }

        await StartSessionAsync(person);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)) return LocalRedirect(model.ReturnUrl);

        // Someone in exactly one circle with nothing to answer goes straight to it.
        var mine = await circles.MyCirclesAsync(person.Id);
        var invites = await circles.InvitationsForAsync(person);
        return mine.Count == 1 && invites.Count == 0
            ? RedirectToAction("Index", "Dashboard", new { circleId = mine[0].CircleId })
            : RedirectToAction("Index", "Circles");
    }

    [Authorize, HttpPost("/logout"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    // ---- Joining through the link a treasurer sends over WhatsApp ----

    [HttpGet("/join/{token}")]
    public async Task<IActionResult> Join(string token)
    {
        var invite = await accounts.FindJoinInviteAsync(token);
        if (invite is null) return View("JoinInvalid");
        if (await accounts.HasAccountAsync(invite.Phone)) return View("JoinHasAccount", invite);
        return View(NewJoinModel(invite));
    }

    [HttpPost("/join/{token}"), ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Join(string token, JoinViewModel model)
    {
        var invite = await accounts.FindJoinInviteAsync(token);
        if (invite is null) return View("JoinInvalid");

        var result = await accounts.JoinAsync(token, model.Name, model.Pin, model.ConfirmPin, model.Consent);
        if (result.Person is not { } person)
        {
            var again = NewJoinModel(invite);
            again.Name = model.Name?.Trim() ?? again.Name;
            again.Consent = model.Consent;
            ModelState.AddModelError("", result.Error!);
            return View(again);
        }

        await StartSessionAsync(person);
        TempData["Toast"] = $"Welcome, {person.FirstName}. You are now in {invite.Circle.Name}.";
        return RedirectToAction("Index", "Dashboard", new { circleId = result.CircleId });
    }

    private static JoinViewModel NewJoinModel(Invitation invite) => new()
    {
        CircleName = invite.Circle.Name,
        InvitedBy = invite.InvitedBy.Name,
        Phone = PhoneNumber.Format(invite.Phone),
        // The treasurer may only have typed a number; a phone number is not a name.
        Name = invite.Name.Any(char.IsLetter) ? invite.Name : "",
    };

    // ---- Setting a new PIN from a reset link ----

    [HttpGet("/reset/{token}")]
    public async Task<IActionResult> ResetPin(string token)
    {
        var person = await accounts.FindResetAsync(token);
        if (person is null) return View("ResetInvalid");
        return View(new ResetPinViewModel { Name = person.FirstName });
    }

    [HttpPost("/reset/{token}"), ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPin(string token, ResetPinViewModel model)
    {
        var result = await accounts.ResetPinAsync(token, model.Pin, model.ConfirmPin);
        if (result.Succeeded)
        {
            TempData["Toast"] = "Your new PIN is set. Sign in with it.";
            return RedirectToAction(nameof(Login));
        }

        // Still a good link? Let them try again. Otherwise it has expired or been used.
        var person = await accounts.FindResetAsync(token);
        if (person is null) return View("ResetInvalid");
        ModelState.AddModelError("", result.Error!);
        return View(new ResetPinViewModel { Name = person.FirstName });
    }

    private async Task StartSessionAsync(Person person)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, person.Id.ToString()), new Claim(ClaimTypes.Name, person.Name)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
