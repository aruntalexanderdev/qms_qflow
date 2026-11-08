using FluentValidation;
using Qms.Application.Common;
using Qms.Application.Localisation;

namespace Qms.Application.PriorityTypes;

/// <summary>The fields create and update share.</summary>
public interface IPriorityTypeFields
{
    string? Name { get; }

    IReadOnlyDictionary<string, string?>? Translations { get; }

    string? Prefix { get; }

    int? PriorityLevel { get; }

    bool? IsPaid { get; }

    decimal? FeeAmount { get; }
}

/// <summary>Create and update validate these fields the same way (BR-3, BR-4, BR-6, BR-7).</summary>
public abstract class PriorityTypeFieldsValidator<T> : AbstractValidator<T>
    where T : IPriorityTypeFields
{
    protected PriorityTypeFieldsValidator(ICompanyLanguages languages)
    {
        RuleFor(command => command.Name).DefaultValue();
        RuleFor(command => command.Translations).Translations(languages);
        RuleFor(command => command.Prefix).PrefixLetter();
        RuleFor(command => command.PriorityLevel).Sent();
        RuleFor(command => command.PriorityLevel).GreaterThanOrEqualTo(1).When(command => command.PriorityLevel is not null).WithMessage("'{PropertyName}' must be 1 or more.");
        RuleFor(command => command.IsPaid).Sent();

        // The fee rules run before the database check constraints are reached (ck_priority_type_fee_*).
        RuleFor(command => command.FeeAmount).NotNull()
            .WithMessage("'{PropertyName}' is required when the priority type is paid.")
            .When(command => command.IsPaid == true);
        RuleFor(command => command.FeeAmount).Money();
        RuleFor(command => command.FeeAmount).Null()
            .WithMessage("'{PropertyName}' must be empty when the priority type is not paid.")
            .When(command => command.IsPaid == false);
    }
}
