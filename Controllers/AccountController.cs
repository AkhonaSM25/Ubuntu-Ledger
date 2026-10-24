using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

public class AccountController(LedgerDbContext db, CircleService circles) : Controller
{
    [HttpGet("/")]
    public IActionResult Root() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Circles") : RedirectToAction(nameof(Login));

    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl) =>
        View(new LoginViewModel { Phone = PhoneNumber.Format(SeedData.TreasurerPhone), ReturnUrl = returnUrl });

    [HttpPost("/login"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var phone = PhoneNumber.Normalize(model.Phone);
        var person = await db.People.FirstOrDefaultAsync(p => p.Phone == phone);
        var ok = person?.PinHash is not null &&
                 new PasswordHasher<Person>().VerifyHashedPassword(person, person.PinHash, model.Pin) != PasswordVerificationResult.Failed;
        if (!ok)
        {
            ModelState.AddModelError("", "That phone number and PIN do not match.");
            return View(model);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, person!.Id.ToString()), new Claim(ClaimTypes.Name, person.Name)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

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
}
