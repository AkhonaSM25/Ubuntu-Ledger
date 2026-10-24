using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UbuntuLedger.Models;

namespace UbuntuLedger.Controllers;

public class ErrorController : Controller
{
    [Route("/error/{code:int?}")]
    public IActionResult Index(int? code)
    {
        ViewData["Code"] = code ?? 500;
        return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
