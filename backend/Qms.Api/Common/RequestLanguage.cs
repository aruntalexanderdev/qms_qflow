using Qms.Application.Localisation;

namespace Qms.Api.Common;

/// <summary>
/// <see cref="IRequestLanguage"/> from the <c>Accept-Language</c> header (AC-26): the best match among the company's enabled
/// languages, else the company default. A caller without a company (anonymous, Super Admin) gets English. The negotiation
/// itself is <see cref="AcceptLanguageNegotiator"/> in Qms.Application.
/// </summary>
public sealed class RequestLanguage(IHttpContextAccessor accessor, ICompanyLanguages languages) : IRequestLanguage
{
    private string? _current;

    public async Task<string> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            return _current;
        }

        var snapshot = await languages.GetAsync(cancellationToken);
        if (snapshot.Company is not { } company)
        {
            return _current = LanguageDefaults.DefaultLanguage;
        }

        return _current = Negotiate(company);
    }

    public string Negotiate(CompanyLanguageSettings company)
    {
        ArgumentNullException.ThrowIfNull(company);
        var header = accessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
        return AcceptLanguageNegotiator.Negotiate(header, company.EnabledLanguages.ToList(), company.DefaultLanguage);
    }
}

public static class RequestLanguageSetup
{
    public static IServiceCollection AddQmsRequestLanguage(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        return services.AddScoped<IRequestLanguage, RequestLanguage>();
    }
}
