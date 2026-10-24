using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UbuntuLedger.Services;

namespace UbuntuLedger.Controllers;

public static class UserExtensions
{
    public static int PersonId(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

// Base for every page inside a circle (routes start circles/{circleId}).
// Loads the signed-in person's membership; people outside the circle get a 404.
[Authorize]
public abstract class CircleControllerBase(CircleAccessService accessService) : Controller
{
    protected CircleAccess Access { get; private set; } = null!;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var access = int.TryParse(RouteData.Values["circleId"]?.ToString(), out var circleId)
            ? await accessService.GetAsync(circleId, User.PersonId())
            : null;
        if (access is null)
        {
            context.Result = NotFound();
            return;
        }
        Access = access;
        ViewData["Access"] = access;
        // Powers the Confirmations badge and the balance card in the sidebar.
        ViewData["Snapshot"] = await context.HttpContext.RequestServices.GetRequiredService<LoanService>().SnapshotAsync(access);
        await next();
    }

    protected void Toast(string message) => TempData["Toast"] = message;
}
