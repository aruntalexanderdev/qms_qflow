using System.Globalization;

namespace Qms.Application.Localisation;

/// <summary>
/// Picks the response language from an <c>Accept-Language</c> header (AC-26). Entries are tried by quality, highest first
/// (header order breaks ties); a tag matches an offered language exactly or by its primary subtag (<c>ar-AE</c> matches
/// <c>ar</c>); letter case is ignored; <c>q=0</c> and <c>*</c> never match. Nothing matching gives the fallback.
/// </summary>
public static class AcceptLanguageNegotiator
{
    private const int MaxEntries = 32;

    public static string Negotiate(string? header, IReadOnlyCollection<string> offered, string fallback)
    {
        if (string.IsNullOrWhiteSpace(header) || offered.Count == 0)
        {
            return fallback;
        }

        foreach (var tag in Parse(header))
        {
            var match = Find(tag, offered);
            if (match is not null)
            {
                return match;
            }
        }

        return fallback;
    }

    private static string? Find(string tag, IReadOnlyCollection<string> offered)
    {
        var exact = offered.FirstOrDefault(code => string.Equals(code, tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var primary = Primary(tag);
        return offered.FirstOrDefault(code => string.Equals(Primary(code), primary, StringComparison.OrdinalIgnoreCase));
    }

    private static string Primary(string tag)
    {
        var dash = tag.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? tag : tag[..dash];
    }

    private static IEnumerable<string> Parse(string header) =>
        header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxEntries)
            .Select((entry, index) => (Entry: ParseEntry(entry), Index: index))
            .Where(item => item.Entry is { Quality: > 0 } && item.Entry.Value.Tag != "*" && item.Entry.Value.Tag.Length > 0)
            .OrderByDescending(item => item.Entry!.Value.Quality)
            .ThenBy(item => item.Index)
            .Select(item => item.Entry!.Value.Tag);

    private static (string Tag, double Quality)? ParseEntry(string entry)
    {
        var parts = entry.Split(';', StringSplitOptions.TrimEntries);
        var quality = 1d;
        foreach (var parameter in parts.Skip(1))
        {
            if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(parameter[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out quality))
                {
                    return null;
                }
            }
        }

        return (parts[0], quality);
    }
}
