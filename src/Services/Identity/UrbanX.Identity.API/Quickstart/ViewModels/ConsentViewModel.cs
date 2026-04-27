namespace UrbanX.Identity.API.Quickstart.ViewModels;

public class ConsentInputModel
{
    public string? Button { get; set; }
    public IEnumerable<string> ScopesConsented { get; set; } = Enumerable.Empty<string>();
    public bool RememberConsent { get; set; } = true;
    public string? ReturnUrl { get; set; }
    public string? Description { get; set; }
}

public class ConsentViewModel : ConsentInputModel
{
    public string? ClientName { get; set; }
    public string? ClientUrl { get; set; }
    public string? ClientLogoUrl { get; set; }
    public bool AllowRememberConsent { get; set; }

    public IEnumerable<ScopeViewModel> IdentityScopes { get; set; } = Enumerable.Empty<ScopeViewModel>();
    public IEnumerable<ScopeViewModel> ApiScopes { get; set; } = Enumerable.Empty<ScopeViewModel>();
}

public class ScopeViewModel
{
    public string? Value { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Emphasize { get; set; }
    public bool Required { get; set; }
    public bool Checked { get; set; }
}

public class ProcessConsentResult
{
    public bool IsRedirect => RedirectUri != null;
    public string? RedirectUri { get; set; }
    public string? ClientId { get; set; }

    public bool ShowView => ViewModel != null;
    public ConsentViewModel? ViewModel { get; set; }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);
    public string? ValidationError { get; set; }
}
