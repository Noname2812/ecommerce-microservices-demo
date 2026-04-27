using System.Security.Claims;
using Duende.IdentityModel;
using Duende.IdentityServer;
using Duende.IdentityServer.Events;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;

namespace UrbanX.Identity.API.Quickstart;

[AllowAnonymous]
[Route("External/[action]")]
public class ExternalController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;
    private readonly IEventService _events;
    private readonly IIdentityAuditWriter _audit;
    private readonly ILogger<ExternalController> _logger;

    public ExternalController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction,
        IEventService events,
        IIdentityAuditWriter audit,
        ILogger<ExternalController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _interaction = interaction;
        _events = events;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Challenge(string scheme, string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)) returnUrl = "~/";
        if (!Url.IsLocalUrl(returnUrl) && !_interaction.IsValidReturnUrl(returnUrl))
        {
            throw new InvalidOperationException("invalid return URL");
        }

        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback)),
            Items =
            {
                { "returnUrl", returnUrl },
                { "scheme", scheme }
            }
        };

        return Challenge(props, scheme);
    }

    [HttpGet]
    public async Task<IActionResult> Callback()
    {
        var result = await HttpContext.AuthenticateAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
        if (result.Succeeded != true)
        {
            throw new InvalidOperationException("External authentication error");
        }

        var externalUser = result.Principal ?? throw new InvalidOperationException("External authentication produced no principal");
        var userIdClaim = externalUser.FindFirst(JwtClaimTypes.Subject)
            ?? externalUser.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Unknown userid");

        var provider = result.Properties.Items["scheme"] ?? throw new InvalidOperationException("Missing external scheme");
        var providerUserId = userIdClaim.Value;

        var user = await _userManager.FindByLoginAsync(provider, providerUserId);
        if (user is null)
        {
            user = await AutoProvisionUserAsync(provider, providerUserId, externalUser);
        }

        if (!user.IsActive)
        {
            return RedirectToAction(nameof(AccountController.AccessDenied), "Account");
        }

        var additionalLocalClaims = new List<Claim>();
        var localSignInProps = new AuthenticationProperties();
        CaptureExternalLoginContext(result, additionalLocalClaims, localSignInProps);

        await _signInManager.SignInWithClaimsAsync(user, localSignInProps, additionalLocalClaims);

        await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);

        var returnUrl = result.Properties.Items["returnUrl"] ?? "~/";
        var context = await _interaction.GetAuthorizationContextAsync(returnUrl);

        await _events.RaiseAsync(new UserLoginSuccessEvent(provider, providerUserId, user.Id.ToString(), user.UserName, true, context?.Client.ClientId));
        await _audit.WriteAsync(user.Id, user.Email, AuthEventType.ExternalLoginGoogle, new { provider });

        return Redirect(returnUrl);
    }

    private async Task<ApplicationUser> AutoProvisionUserAsync(string provider, string providerUserId, ClaimsPrincipal claims)
    {
        var email = claims.FindFirst(JwtClaimTypes.Email)?.Value
                 ?? claims.FindFirst(ClaimTypes.Email)?.Value
                 ?? throw new InvalidOperationException("External provider did not return an email claim");

        var name = claims.FindFirst(JwtClaimTypes.Name)?.Value
                ?? claims.FindFirst(ClaimTypes.Name)?.Value
                ?? email;

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = name,
            IsActive = true
        };

        var create = await _userManager.CreateAsync(user);
        if (!create.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", create.Errors.Select(e => e.Description)));
        }

        var loginInfo = new UserLoginInfo(provider, providerUserId, provider);
        var addLogin = await _userManager.AddLoginAsync(user, loginInfo);
        if (!addLogin.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", addLogin.Errors.Select(e => e.Description)));
        }

        return user;
    }

    private static void CaptureExternalLoginContext(AuthenticateResult result, List<Claim> additionalClaims, AuthenticationProperties props)
    {
        var sid = result.Principal!.FindFirst(JwtClaimTypes.SessionId);
        if (sid != null)
        {
            additionalClaims.Add(new Claim(JwtClaimTypes.SessionId, sid.Value));
        }

        var idToken = result.Properties!.GetTokenValue("id_token");
        if (idToken != null)
        {
            props.StoreTokens(new[] { new AuthenticationToken { Name = "id_token", Value = idToken } });
        }
    }
}
