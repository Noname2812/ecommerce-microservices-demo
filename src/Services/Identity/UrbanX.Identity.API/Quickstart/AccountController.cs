using Duende.IdentityServer;
using Duende.IdentityServer.Events;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Quickstart.ViewModels;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;

namespace UrbanX.Identity.API.Quickstart;

[AllowAnonymous]
[Route("Account/[action]")]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;
    private readonly IClientStore _clientStore;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly IEventService _events;
    private readonly IIdentityAuditWriter _audit;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction,
        IClientStore clientStore,
        IAuthenticationSchemeProvider schemeProvider,
        IEventService events,
        IIdentityAuditWriter audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _interaction = interaction;
        _clientStore = clientStore;
        _schemeProvider = schemeProvider;
        _events = events;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl)
    {
        var vm = await BuildLoginViewModelAsync(returnUrl);
        if (vm.IsExternalLoginOnly)
        {
            return RedirectToAction(nameof(Challenge), "External", new { scheme = vm.ExternalLoginScheme, returnUrl });
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInputModel model, string? button)
    {
        var context = await _interaction.GetAuthorizationContextAsync(model.ReturnUrl);

        if (button != "login")
        {
            if (context != null)
            {
                await _interaction.DenyAuthorizationAsync(context, AuthorizationError.AccessDenied);
                return Redirect(model.ReturnUrl ?? "~/");
            }
            return Redirect("~/");
        }

        if (!ModelState.IsValid)
        {
            var vm = await BuildLoginViewModelAsync(model);
            return View(vm);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            await _events.RaiseAsync(new UserLoginFailureEvent(model.Email, "user not found", clientId: context?.Client.ClientId));
            ModelState.AddModelError(string.Empty, AccountOptions.InvalidCredentialsErrorMessage);
            return View(await BuildLoginViewModelAsync(model));
        }

        if (!user.IsActive)
        {
            await _audit.WriteAsync(user.Id, user.Email, AuthEventType.LoginFailed, new { reason = "inactive" });
            ModelState.AddModelError(string.Empty, AccountOptions.AccountInactiveErrorMessage);
            return View(await BuildLoginViewModelAsync(model));
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            model.Password,
            isPersistent: AccountOptions.AllowRememberLogin && model.RememberLogin,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await _audit.WriteAsync(user.Id, user.Email, AuthEventType.AccountLocked);
            ModelState.AddModelError(string.Empty, AccountOptions.AccountLockedErrorMessage);
            return View(await BuildLoginViewModelAsync(model));
        }

        if (!result.Succeeded)
        {
            await _events.RaiseAsync(new UserLoginFailureEvent(model.Email, "invalid credentials", clientId: context?.Client.ClientId));
            await _audit.WriteAsync(user.Id, user.Email, AuthEventType.LoginFailed, new { reason = "invalid_credentials" });
            ModelState.AddModelError(string.Empty, AccountOptions.InvalidCredentialsErrorMessage);
            return View(await BuildLoginViewModelAsync(model));
        }

        await _events.RaiseAsync(new UserLoginSuccessEvent(user.UserName!, user.Id.ToString(), user.UserName, clientId: context?.Client.ClientId));
        await _audit.WriteAsync(user.Id, user.Email, AuthEventType.LoginSuccess);

        if (context != null)
        {
            return Redirect(model.ReturnUrl!);
        }

        if (Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl!);
        }
        if (string.IsNullOrEmpty(model.ReturnUrl))
        {
            return Redirect("~/");
        }

        throw new InvalidOperationException("invalid return URL");
    }

    [HttpGet]
    public async Task<IActionResult> Logout(string? logoutId)
    {
        var vm = new LogoutViewModel { LogoutId = logoutId, ShowLogoutPrompt = AccountOptions.ShowLogoutPrompt };

        if (!User.Identity!.IsAuthenticated)
        {
            vm.ShowLogoutPrompt = false;
            return await LogoutInternal(vm);
        }

        var context = await _interaction.GetLogoutContextAsync(logoutId);
        if (context?.ShowSignoutPrompt == false)
        {
            vm.ShowLogoutPrompt = false;
            return await LogoutInternal(vm);
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Logout(LogoutInputModel model)
        => LogoutInternal(new LogoutViewModel { LogoutId = model.LogoutId });

    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task<IActionResult> LogoutInternal(LogoutViewModel model)
    {
        var loggedOut = await BuildLoggedOutViewModelAsync(model.LogoutId);

        if (User.Identity!.IsAuthenticated)
        {
            var userId = _userManager.GetUserId(User);
            var email = User.Identity.Name;
            await _signInManager.SignOutAsync();
            await _events.RaiseAsync(new UserLogoutSuccessEvent(userId, email));
            if (Guid.TryParse(userId, out var uid))
            {
                await _audit.WriteAsync(uid, email, AuthEventType.Logout);
            }
        }

        if (loggedOut.TriggerExternalSignout)
        {
            var url = Url.Action(nameof(Logout), new { logoutId = loggedOut.LogoutId });
            return SignOut(new AuthenticationProperties { RedirectUri = url }, loggedOut.ExternalAuthenticationScheme!);
        }

        return View("LoggedOut", loggedOut);
    }

    private async Task<LoginViewModel> BuildLoginViewModelAsync(string? returnUrl)
    {
        var context = await _interaction.GetAuthorizationContextAsync(returnUrl);
        if (context?.IdP != null && await _schemeProvider.GetSchemeAsync(context.IdP) != null)
        {
            var local = context.IdP == IdentityServerConstants.LocalIdentityProvider;
            return new LoginViewModel
            {
                EnableLocalLogin = local,
                ReturnUrl = returnUrl,
                Email = context.LoginHint ?? string.Empty,
                ExternalProviders = local ? Enumerable.Empty<ExternalProvider>() : new[]
                {
                    new ExternalProvider { AuthenticationScheme = context.IdP }
                }
            };
        }

        var schemes = await _schemeProvider.GetAllSchemesAsync();
        var providers = schemes
            .Where(x => x.DisplayName != null)
            .Select(x => new ExternalProvider
            {
                DisplayName = x.DisplayName!,
                AuthenticationScheme = x.Name
            })
            .ToList();

        var allowLocal = true;
        if (context?.Client.ClientId != null)
        {
            var client = await _clientStore.FindEnabledClientByIdAsync(context.Client.ClientId);
            if (client != null)
            {
                allowLocal = client.EnableLocalLogin;
                if (client.IdentityProviderRestrictions?.Any() == true)
                {
                    providers = providers.Where(p => client.IdentityProviderRestrictions.Contains(p.AuthenticationScheme)).ToList();
                }
            }
        }

        return new LoginViewModel
        {
            AllowRememberLogin = AccountOptions.AllowRememberLogin,
            EnableLocalLogin = allowLocal && AccountOptions.AllowLocalLogin,
            ReturnUrl = returnUrl,
            Email = context?.LoginHint ?? string.Empty,
            ExternalProviders = providers
        };
    }

    private async Task<LoginViewModel> BuildLoginViewModelAsync(LoginInputModel model)
    {
        var vm = await BuildLoginViewModelAsync(model.ReturnUrl);
        vm.Email = model.Email;
        vm.RememberLogin = model.RememberLogin;
        return vm;
    }

    private async Task<LoggedOutViewModel> BuildLoggedOutViewModelAsync(string? logoutId)
    {
        var logout = await _interaction.GetLogoutContextAsync(logoutId);

        var vm = new LoggedOutViewModel
        {
            AutomaticRedirectAfterSignOut = AccountOptions.AutomaticRedirectAfterSignOut,
            PostLogoutRedirectUri = logout?.PostLogoutRedirectUri,
            ClientName = string.IsNullOrEmpty(logout?.ClientName) ? logout?.ClientId : logout.ClientName,
            SignOutIframeUrl = logout?.SignOutIFrameUrl,
            LogoutId = logoutId
        };

        if (User.Identity!.IsAuthenticated)
        {
            var idp = User.FindFirst(Duende.IdentityModel.JwtClaimTypes.IdentityProvider)?.Value;
            if (idp != null && idp != IdentityServerConstants.LocalIdentityProvider)
            {
                var providerSupportsSignout = await SchemeSupportsSignOutAsync(idp);
                if (providerSupportsSignout)
                {
                    vm.LogoutId ??= await _interaction.CreateLogoutContextAsync();
                    vm.ExternalAuthenticationScheme = idp;
                }
            }
        }

        return vm;
    }

    private async Task<bool> SchemeSupportsSignOutAsync(string scheme)
    {
        var provider = HttpContext.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>();
        var handler = await provider.GetHandlerAsync(HttpContext, scheme);
        return handler is IAuthenticationSignOutHandler;
    }
}
