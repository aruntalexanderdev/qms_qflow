using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Qms.Domain.Entities;

namespace Qms.Application.Common;

/// <summary>
/// Wraps a command marked <see cref="AuditedAttribute"/> (ADR-008 section 11), after validation and the cross-tenant
/// check in the chain: opens a transaction, runs the handler, and on success adds one <c>audit_log</c> row and commits, so
/// the change and its audit row commit together. A failed result or an exception rolls everything back and writes nothing.
/// The row holds the current company and user, the entity id (the <c>Id</c> of the result, else of the command),
/// <see cref="TimeProvider"/> time and the command as JSON without <see cref="NotAuditedAttribute"/> properties.
/// Post-commit actions (<see cref="IPostCommitActions"/>) run after the commit.
/// </summary>
public sealed class AuditDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
{
    private static readonly AuditedAttribute? Audited = typeof(TCommand).GetCustomAttribute<AuditedAttribute>(inherit: false);
    private static readonly bool OptedOut = typeof(TCommand).IsDefined(typeof(NotAuditedAttribute), inherit: false);
    private static readonly PropertyInfo? ResultId = IdProperty(typeof(TResult));
    private static readonly PropertyInfo? CommandId = IdProperty(typeof(TCommand));

    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly IQmsDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly TimeProvider _timeProvider;
    private readonly PostCommitActions _postCommit;
    private readonly ILogger<AuditDecorator<TCommand, TResult>> _logger;

    public AuditDecorator(
        ICommandHandler<TCommand, TResult> inner,
        IQmsDbContext db,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        TimeProvider timeProvider,
        PostCommitActions postCommit,
        ILogger<AuditDecorator<TCommand, TResult>> logger)
    {
        _inner = inner;
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _timeProvider = timeProvider;
        _postCommit = postCommit;
        _logger = logger;
    }

    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        if (Audited is null || OptedOut)
        {
            return await _inner.HandleAsync(command, cancellationToken);
        }

        Result<TResult> result;
        var committed = false;
        _postCommit.BeginCollecting();
        try
        {
            await using (var transaction = await _db.BeginTransactionAsync(cancellationToken))
            {
                result = await _inner.HandleAsync(command, cancellationToken);
                if (result.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return result;
                }

                // A tenant user is audited in its own company; a cross-tenant command (Super Admin) names the target company.
                var companyId = _currentTenant.CompanyId
                    ?? (command as IAuditCompanyTarget)?.AuditCompanyId
                    ?? (result.Value as IAuditCompanyTarget)?.AuditCompanyId
                    ?? throw new InvalidOperationException("An audited command ran without a current company; implement IAuditCompanyTarget.");
                _db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = companyId,
                    UserId = _currentUser.UserId,
                    DeviceId = null,
                    EntityName = Audited.Entity,
                    EntityId = ReadId(ResultId, result.Value) ?? ReadId(CommandId, command),
                    Action = Audited.Action,
                    At = _timeProvider.GetUtcNow(),
                    Data = JsonSerializer.Serialize(command, typeof(TCommand), AuditJson.Options),
                });
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
            }
        }
        finally
        {
            if (!committed)
            {
                _postCommit.Discard();
            }
        }

        // After the commit and outside the transaction scope: a failing action can never undo the change.
        await _postCommit.RunAsync(_logger);
        return result;
    }

    private static PropertyInfo? IdProperty(Type type) =>
        type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance) is { } property && (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
            ? property
            : null;

    private static Guid? ReadId(PropertyInfo? property, object? source) =>
        property is not null && source is not null && property.GetValue(source) is Guid id && id != Guid.Empty ? id : null;
}

/// <summary>JSON options of the audit <c>data</c> column: camelCase like the API, enums as strings, no <see cref="NotAuditedAttribute"/> properties.</summary>
internal static class AuditJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RemoveNotAudited } },
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    private static void RemoveNotAudited(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].AttributeProvider?.IsDefined(typeof(NotAuditedAttribute), inherit: true) == true)
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }
}

/// <summary>Decides which command types get the <see cref="AuditDecorator{TCommand, TResult}"/>.</summary>
public static class AuditRules
{
    /// <summary>True for a type with <see cref="AuditedAttribute"/> and no type-level <see cref="NotAuditedAttribute"/>.</summary>
    public static bool AppliesTo(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        return commandType.IsDefined(typeof(AuditedAttribute), inherit: false) && !commandType.IsDefined(typeof(NotAuditedAttribute), inherit: false);
    }
}
