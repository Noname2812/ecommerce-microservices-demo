using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Quickstart.ViewModels;

namespace UrbanX.Identity.API.Quickstart;

public static class Extensions
{
    public static IActionResult LoadingPage(this Controller controller, string redirectUri)
    {
        controller.HttpContext.Response.StatusCode = 200;
        controller.HttpContext.Response.Headers["Location"] = string.Empty;
        return controller.View("Redirect", new RedirectViewModel { RedirectUrl = redirectUri });
    }
}
