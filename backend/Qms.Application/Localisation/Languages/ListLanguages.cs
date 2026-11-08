using Qms.Application.Common;

namespace Qms.Application.Localisation.Languages;

/// <summary>A language as clients show it in a switch (ADR-010 section 8). <c>NativeName</c> is never translated.</summary>
public sealed record LanguageDto(string Code, string NativeName, string Direction)
{
    public static LanguageDto From(LanguageInfo language) => new(language.Code, language.NativeName, language.Direction);
}

/// <summary>The languages on offer to the caller and the default one.</summary>
public sealed record LanguagesDto(string DefaultLanguage, IReadOnlyList<LanguageDto> Languages);

/// <summary>
/// <c>GET /languages</c> (BR-12): an anonymous caller or a Super Admin (no company) gets the available languages of the
/// release; a company user gets the languages the company has enabled. No tenant data is read for the first two.
/// </summary>
public sealed record ListLanguagesQuery;

public sealed class ListLanguagesHandler(ICompanyLanguages languages) : IQueryHandler<ListLanguagesQuery, LanguagesDto>
{
    public async Task<Result<LanguagesDto>> HandleAsync(ListLanguagesQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await languages.GetAsync(cancellationToken);
        if (snapshot.Company is not { } company)
        {
            return new LanguagesDto(
                LanguageDefaults.DefaultLanguage,
                [.. snapshot.Registry.Where(language => language.IsAvailable).OrderBy(language => language.SortOrder).Select(LanguageDto.From)]);
        }

        return new LanguagesDto(
            company.DefaultLanguage,
            [.. snapshot.Registry
                .Where(language => company.Match(language.Code) is not null)
                .OrderBy(language => language.SortOrder)
                .Select(LanguageDto.From)]);
    }
}
