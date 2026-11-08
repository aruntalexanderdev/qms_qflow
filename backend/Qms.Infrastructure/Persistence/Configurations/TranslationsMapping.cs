using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Qms.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping of a <c>&lt;field&gt;_translations</c> column (ADR-010 section 4): a flat language-code-to-text object stored as
/// <c>jsonb not null default '{}'</c> with the check <c>jsonb_typeof(...) = 'object'</c>. The value converter reads and writes
/// the JSON text; the value comparer compares content and snapshots a copy, so adding, editing or removing a key in place
/// is seen by the change tracker (without it EF would compare references and silently skip the UPDATE).
/// </summary>
internal static class TranslationsMapping
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static readonly ValueConverter<Dictionary<string, string>, string> Converter = new(
        translations => JsonSerializer.Serialize(translations, Json),
        json => Deserialize(json));

    public static readonly ValueComparer<Dictionary<string, string>> Comparer = new(
        (left, right) => Same(left, right),
        translations => Hash(translations),
        translations => Copy(translations));

    public static PropertyBuilder<Dictionary<string, string>> HasTranslationsMapping(this PropertyBuilder<Dictionary<string, string>> property) =>
        property
            .HasConversion(Converter, Comparer)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

    /// <summary>Adds <c>ck_&lt;table&gt;_&lt;column&gt;_object</c> for a translations column.</summary>
    public static void HasTranslationsCheck(this TableBuilder table, string tableName, string column) =>
        table.HasCheckConstraint(CheckName(tableName, column), $"jsonb_typeof({column}) = 'object'");

    public static string CheckName(string tableName, string column) => $"ck_{tableName}_{column}_object";

    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json) ?? new Dictionary<string, string>();

    private static bool Same(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        return left.All(pair => right.TryGetValue(pair.Key, out var other) && string.Equals(pair.Value, other, StringComparison.Ordinal));
    }

    private static int Hash(Dictionary<string, string> translations)
    {
        // Order-independent, so two equal objects hash alike whatever the insertion order.
        var hash = 0;
        foreach (var (key, value) in translations)
        {
            hash ^= HashCode.Combine(key, value);
        }

        return hash;
    }

    private static Dictionary<string, string> Copy(Dictionary<string, string> translations) => new(translations);
}
