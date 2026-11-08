using FluentValidation;

namespace Qms.Application.Common;

/// <summary>
/// Validation rules every setup command shares (ADR-008 section 8). Messages name the rule, never the stored value.
/// Field names in <c>errors</c> come from the property name (camelCase in the response).
/// </summary>
public static class CommonRules
{
    /// <summary>Largest money amount: <c>numeric(12,2)</c>.</summary>
    public const decimal MoneyMax = 9_999_999_999.99m;

    /// <summary>One letter A to Z after trimming. No silent upper-casing: <c>h</c> is rejected.</summary>
    public static IRuleBuilderOptions<T, string?> PrefixLetter<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => value is not null && IsPrefixLetter(value.Trim()))
            .WithMessage("'{PropertyName}' must be exactly one letter from A to Z.");

    public static bool IsPrefixLetter(string value) => value.Length == 1 && value[0] is >= 'A' and <= 'Z';

    /// <summary>Sort order of 0 or more.</summary>
    public static IRuleBuilderOptions<T, int> SortOrder<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(0).WithMessage("'{PropertyName}' must be 0 or more.");

    /// <summary>A money amount (when given): 0 to 9 999 999 999.99 with at most two decimals.</summary>
    public static IRuleBuilderOptions<T, decimal?> Money<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.Must(value => value is null || (value >= 0 && value <= MoneyMax && decimal.Round(value.Value, 2) == value))
            .WithMessage("'{PropertyName}' must be between 0 and 9999999999.99 with at most 2 decimals.");

    /// <summary>The <c>version</c> a <c>PUT</c> carries: required, and text this API issued (else 400, never 500).</summary>
    public static IRuleBuilderOptions<T, string?> RowVersionText<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("'{PropertyName}' is required.")
            .Must(value => string.IsNullOrWhiteSpace(value) || RowVersionCodec.TryDecode(value, out _))
            .WithMessage("'{PropertyName}' is not a valid version.");

    /// <summary>Decodes a version that passed <see cref="RowVersionText"/>.</summary>
    public static uint DecodeVersion(string? version) =>
        RowVersionCodec.TryDecode(version, out var rowVersion)
            ? rowVersion
            : throw new InvalidOperationException("The version was not validated.");
}
