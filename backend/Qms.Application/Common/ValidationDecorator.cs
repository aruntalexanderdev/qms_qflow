using FluentValidation;
using FluentValidation.Results;

namespace Qms.Application.Common;

/// <summary>Runs every FluentValidation validator for the command before the inner handler.</summary>
public sealed class ValidationDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly IEnumerable<IValidator<TCommand>> _validators;

    public ValidationDecorator(ICommandHandler<TCommand, TResult> inner, IEnumerable<IValidator<TCommand>> validators)
    {
        _inner = inner;
        _validators = validators;
    }

    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var failure = await ValidationRunner.ValidateAsync(_validators, command, cancellationToken);
        return failure is null ? await _inner.HandleAsync(command, cancellationToken) : Result<TResult>.Failure(failure);
    }
}

/// <summary>Runs every FluentValidation validator for the query before the inner handler.</summary>
public sealed class QueryValidationDecorator<TQuery, TResult> : IQueryHandler<TQuery, TResult>
{
    private readonly IQueryHandler<TQuery, TResult> _inner;
    private readonly IEnumerable<IValidator<TQuery>> _validators;

    public QueryValidationDecorator(IQueryHandler<TQuery, TResult> inner, IEnumerable<IValidator<TQuery>> validators)
    {
        _inner = inner;
        _validators = validators;
    }

    public async Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        var failure = await ValidationRunner.ValidateAsync(_validators, query, cancellationToken);
        return failure is null ? await _inner.HandleAsync(query, cancellationToken) : Result<TResult>.Failure(failure);
    }
}

internal static class ValidationRunner
{
    public static async Task<Error?> ValidateAsync<T>(
        IEnumerable<IValidator<T>> validators,
        T request,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var context = new ValidationContext<T>(request);
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return null;
        }

        var details = failures
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());

        // When every failure carries the same specific code (a dotted <area>.<reason> set with WithErrorCode), that code is
        // the answer, otherwise the general validation.failed (ADR-008 section 6).
        var codes = failures.Select(failure => failure.ErrorCode).Distinct().ToList();
        var code = codes.Count == 1 && codes[0] is { } specific && specific.Contains('.', StringComparison.Ordinal)
            ? specific
            : Error.ValidationFailedCode;
        return Error.Validation(code, "One or more validation errors occurred.", details);
    }
}
