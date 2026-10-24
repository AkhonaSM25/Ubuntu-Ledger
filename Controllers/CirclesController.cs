using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UbuntuLedger.Data;
using UbuntuLedger.Models;
using UbuntuLedger.Services;
using UbuntuLedger.ViewModels;

namespace UbuntuLedger.Controllers;

// Choosing, starting and joining circles: the pages outside any one circle.
[Authorize]
public class CirclesController(LedgerDbContext db, CircleService circles) : Controller
{
    private async Task<Person> MeAsync() => (await db.People.FindAsync(User.PersonId()))!;

    [HttpGet("/circles")]
    public async Task<IActionResult> Index()
    {
        var me = await MeAsync();
        return View(new CirclesViewModel { Me = me, Circles = await circles.MyCirclesAsync(me.Id), Invitations = await circles.InvitationsForAsync(me) });
    }

    [HttpGet("/circles/new")]
    public IActionResult Create() => View(new CreateCircleViewModel());

    // One form, several buttons: "add" and "remove-N" edit the list, anything else starts the circle.
    [HttpPost("/circles/new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateCircleViewModel model, string? command)
    {
        if (command == "add")
        {
            ModelState.Clear();
            if (string.IsNullOrWhiteSpace(model.NewPhone))
                ModelState.AddModelError(nameof(model.NewPhone), "Enter their phone number.");
            else
            {
                model.People.Add(new PersonInput { Name = model.NewName?.Trim(), Phone = model.NewPhone.Trim() });
                model.NewName = model.NewPhone = null;
            }
            return View(model);
        }
        if (command?.StartsWith("remove-") == true && int.TryParse(command[7..], out var i) && i >= 0 && i < model.People.Count)
        {
            ModelState.Clear();
            model.People.RemoveAt(i);
            return View(model);
        }

        if (!ModelState.IsValid) return View(model);
        var me = await MeAsync();
        var result = await circles.CreateAsync(me, model.Name, model.People.Select(p => (p.Name ?? "", p.Phone)).ToList());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(model.Name), result.Error!);
            return View(model);
        }
        var sent = model.People.Count;
        TempData["Toast"] = $"{model.Name.Trim()} is ready. " + (sent > 0 ? Format.Plural(sent, "invite") + " sent." : "Now invite your people.");
        return RedirectToAction("Index", "Dashboard", new { circleId = result.Id });
    }

    [HttpPost("/invitations/{id:int}/accept"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        var result = await circles.RespondAsync(await MeAsync(), id, accept: true);
        TempData["Toast"] = result.Succeeded ? "You joined the circle. Your treasurer records your loans there." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/invitations/{id:int}/decline"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(int id)
    {
        var result = await circles.RespondAsync(await MeAsync(), id, accept: false);
        if (!result.Succeeded) TempData["Toast"] = result.Error;
        return RedirectToAction(nameof(Index));
    }
}
