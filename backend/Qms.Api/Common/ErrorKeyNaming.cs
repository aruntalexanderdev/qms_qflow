using System.Text.Json;

namespace Qms.Api.Common;

/// <summary>
/// Normalises keys of the problem details <c>errors</c> dictionary to the JSON naming of the API:
/// camelCase segments, no <c>$.</c> prefix, indexes kept (<c>items[0].name</c>).
/// </summary>
public static class ErrorKeyNaming
{
    public static string Normalize(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        var path = key;
        if (path == "$")
        {
            return string.Empty;
        }

        if (path.StartsWith("$.", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            segments[i] = bracket < 0
                ? JsonNamingPolicy.CamelCase.ConvertName(segment)
                : JsonNamingPolicy.CamelCase.ConvertName(segment[..bracket]) + segment[bracket..];
        }

        return string.Join('.', segments);
    }
}
