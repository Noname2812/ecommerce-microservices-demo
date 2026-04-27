using Duende.IdentityServer.Events;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Identity.API.Quickstart.ViewModels;

namespace UrbanX.Identity.API.Quickstart;

[Authorize]
[Route("Consent/[action]")]
public class ConsentController : Controller
{
    private readonly IIdentityServerInteractionService _interaction;
    private readonly IEventService _events;
    private readonly ILogger<ConsentController> _logger;

    public ConsentController(
        IIdentityServerInteractionService interaction,
        IEventService events,
        ILogger<ConsentController> logger)
    {
        _interaction = interaction;
        _events = events;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? returnUrl)
    {
        var vm = await BuildViewModelAsync(returnUrl);
        if (vm is null)
        {
            return RedirectToAction("Error", "Home");
        }
        return View("Index", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ConsentInputModel model)
    {
        var result = await ProcessConsent(model);

        if (result.IsRedirect)
        {
            var ctx = await _interaction.GetAuthorizationContextAsync(result.RedirectUri);
            if (ctx is not null && IsNativeClient(ctx))
            {
                return this.LoadingPage(result.RedirectUri!);
            }
            return Redirect(result.RedirectUri!);
        }

        if (result.HasValidationError)
        {
            ModelState.AddModelError(string.Empty, result.ValidationError!);
        }

        if (result.ShowView)
        {
            return View("Index", result.ViewModel);
        }

        return RedirectToAction("Error", "Home");
    }

    private async Task<ProcessConsentResult> ProcessConsent(ConsentInputModel model)
    {
        var result = new ProcessConsentResult();

        var request = await _interaction.GetAuthorizationContextAsync(model.ReturnUrl);
        if (request is null) return result;

        ConsentResponse? grantedConsent = null;

        if (model.Button == "no")
        {
            grantedConsent = new ConsentResponse { Error = AuthorizationError.AccessDenied };
            await _events.RaiseAsync(new ConsentDeniedEvent(User.GetSubjectId(), request.Client.ClientId, request.ValidatedResources.RawScopeValues));
        }
        else if (model.Button == "yes")
        {
            if (model.ScopesConsented?.Any() == true)
            {
                var scopes = model.ScopesConsented;
                if (!ConsentOptions.EnableOfflineAccess)
                {
                    scopes = scopes.Where(x => x != Duende.IdentityModel.OidcConstants.StandardScopes.OfflineAccess);
                }

                grantedConsent = new ConsentResponse
                {
                    RememberConsent = model.RememberConsent,
                    ScopesValuesConsented = scopes.ToArray(),
                    Description = model.Description
                };

                await _events.RaiseAsync(new ConsentGrantedEvent(User.GetSubjectId(), request.Client.ClientId, request.ValidatedResources.RawScopeValues, grantedConsent.ScopesValuesConsented, grantedConsent.RememberConsent));
            }
            else
            {
                result.ValidationError = ConsentOptions.MustChooseOneErrorMessage;
            }
        }
        else
        {
            result.ValidationError = ConsentOptions.InvalidSelectionErrorMessage;
        }

        if (grantedConsent != null)
        {
            await _interaction.GrantConsentAsync(request, grantedConsent);
            result.RedirectUri = model.ReturnUrl;
            result.ClientId = request.Client.ClientId;
        }
        else
        {
            result.ViewModel = await BuildViewModelAsync(model.ReturnUrl, model);
        }

        return result;
    }

    private async Task<ConsentViewModel?> BuildViewModelAsync(string? returnUrl, ConsentInputModel? model = null)
    {
        var request = await _interaction.GetAuthorizationContextAsync(returnUrl);
        if (request is null) return null;

        return CreateConsentViewModel(model, returnUrl, request);
    }

    private static ConsentViewModel CreateConsentViewModel(ConsentInputModel? model, string? returnUrl, AuthorizationRequest request)
    {
        var vm = new ConsentViewModel
        {
            RememberConsent = model?.RememberConsent ?? true,
            ScopesConsented = model?.ScopesConsented ?? Enumerable.Empty<string>(),
            Description = model?.Description,

            ReturnUrl = returnUrl,
            ClientName = request.Client.ClientName ?? request.Client.ClientId,
            ClientUrl = request.Client.ClientUri,
            ClientLogoUrl = request.Client.LogoUri,
            AllowRememberConsent = request.Client.AllowRememberConsent,

            IdentityScopes = request.ValidatedResources.Resources.IdentityResources
                .Select(x => CreateScope(x, model?.ScopesConsented?.Contains(x.Name) ?? true))
                .ToArray()
        };

        var apiScopes = new List<ScopeViewModel>();
        foreach (var parsed in request.ValidatedResources.ParsedScopes)
        {
            var apiScope = request.ValidatedResources.Resources.FindApiScope(parsed.ParsedName);
            if (apiScope is null) continue;

            var scopeVm = CreateScope(parsed, apiScope, model?.ScopesConsented?.Contains(parsed.RawValue) ?? true);
            apiScopes.Add(scopeVm);
        }

        if (ConsentOptions.EnableOfflineAccess && request.ValidatedResources.Resources.OfflineAccess)
        {
            apiScopes.Add(new ScopeViewModel
            {
                Value = Duende.IdentityModel.OidcConstants.StandardScopes.OfflineAccess,
                DisplayName = ConsentOptions.OfflineAccessDisplayName,
                Description = ConsentOptions.OfflineAccessDescription,
                Emphasize = true,
                Checked = model?.ScopesConsented?.Contains(Duende.IdentityModel.OidcConstants.StandardScopes.OfflineAccess) ?? true
            });
        }

        vm.ApiScopes = apiScopes;
        return vm;
    }

    private static ScopeViewModel CreateScope(IdentityResource identity, bool check) => new()
    {
        Value = identity.Name,
        DisplayName = identity.DisplayName ?? identity.Name,
        Description = identity.Description,
        Emphasize = identity.Emphasize,
        Required = identity.Required,
        Checked = check || identity.Required
    };

    private static ScopeViewModel CreateScope(ParsedScopeValue parsed, ApiScope api, bool check) => new()
    {
        Value = parsed.RawValue,
        DisplayName = api.DisplayName ?? api.Name,
        Description = api.Description,
        Emphasize = api.Emphasize,
        Required = api.Required,
        Checked = check || api.Required
    };

    private static bool IsNativeClient(AuthorizationRequest context) =>
        !context.RedirectUri.StartsWith("https", StringComparison.OrdinalIgnoreCase) &&
        !context.RedirectUri.StartsWith("http", StringComparison.OrdinalIgnoreCase);
}
