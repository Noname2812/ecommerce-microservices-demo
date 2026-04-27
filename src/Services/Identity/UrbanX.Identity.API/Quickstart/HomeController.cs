using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Quickstart.ViewModels;

namespace UrbanX.Identity.API.Quickstart;

[AllowAnonymous]
public class HomeController : Controller
{
    private readonly IIdentityServerInteractionService _interaction;
    private readonly IWebHostEnvironment _environment;

    public HomeController(IIdentityServerInteractionService interaction, IWebHostEnvironment environment)
    {
        _interaction = interaction;
        _environment = environment;
    }

    [HttpGet("/")]
    public IActionResult Index()
    {
        if (_environment.IsDevelopment())
        {
            return View();
        }

        return NotFound();
    }

    [HttpGet("/Home/Error")]
    public async Task<IActionResult> Error(string? errorId)
    {
        var vm = new ErrorViewModel();

        if (!string.IsNullOrWhiteSpace(errorId))
        {
            var message = await _interaction.GetErrorContextAsync(errorId);
            if (message != null)
            {
                vm.Error = message;
                if (!_environment.IsDevelopment())
                {
                    message.ErrorDescription = null;
                }
            }
        }

        return View("Error", vm);
    }
}
