using Qms.Application.Common;

namespace Qms.Infrastructure.Errors;

/// <summary>How the API answers a database error: the kind of failure and the stable error code.</summary>
public sealed record DatabaseError(ErrorType Type, string Code);

/// <summary>
/// Maps PostgreSQL constraint violations to API errors by constraint name (ADR-008 section 7), so the API never parses
/// messages and never shows SQL. 23505 (unique) is 409, 23503 / 23001 (foreign key, restrict) is 409 or, for the user
/// assignment constraints, 404, 23514 (check) is 400. Unknown names get the default code of their SQLSTATE
/// (<c>resource.duplicate</c>, <c>resource.in_use</c>, <c>request.invalid</c>). Each story adds its own constraints here;
/// a test proves every name exists in the database schema.
/// </summary>
public static class PostgresErrorCatalog
{
    public const string UniqueViolation = "23505";
    public const string ForeignKeyViolation = "23503";
    public const string RestrictViolation = "23001";
    public const string CheckViolation = "23514";

    private static readonly DatabaseError DefaultDuplicate = new(ErrorType.Conflict, ErrorCodes.ResourceDuplicate);
    private static readonly DatabaseError DefaultInUse = new(ErrorType.Conflict, ErrorCodes.ResourceInUse);
    private static readonly DatabaseError DefaultInvalid = new(ErrorType.Validation, ErrorCodes.RequestInvalid);

    private static readonly DatabaseError ServiceTypeInUse = new(ErrorType.Conflict, "service_type.in_use");
    private static readonly DatabaseError PriorityTypeInUse = new(ErrorType.Conflict, "priority_type.in_use");
    private static readonly DatabaseError UserNotFound = new(ErrorType.NotFound, "user.not_found");
    private static readonly DatabaseError ResourceNotFound = new(ErrorType.NotFound, ErrorCodes.ResourceNotFound);

    private static readonly Dictionary<string, DatabaseError> UniqueConstraints = new(StringComparer.Ordinal)
    {
        ["ix_service_type_company_id_prefix"] = new(ErrorType.Conflict, "service_type.prefix_taken"),
        ["ix_priority_type_company_id_prefix"] = new(ErrorType.Conflict, "priority_type.prefix_taken"),
    };

    // Foreign keys (23503 and 23001 share this table). Names are the ones PostgreSQL reports (63 characters at most).
    private static readonly Dictionary<string, DatabaseError> ForeignKeys = new(StringComparer.Ordinal)
    {
        // Rows that still reference a service type.
        ["fk_company_service_priority_service_types_company_id_service_t"] = ServiceTypeInUse,
        ["fk_dispenser_zone_service_service_types_company_id_service_typ"] = ServiceTypeInUse,
        ["fk_service_flow_service_types_company_id_from_service_type_id"] = ServiceTypeInUse,
        ["fk_service_flow_service_types_company_id_to_service_type_id"] = ServiceTypeInUse,
        ["fk_token_service_type_company_id_current_service_type_id"] = ServiceTypeInUse,
        ["fk_token_sequence_service_type_company_id_service_type_id"] = ServiceTypeInUse,
        ["fk_token_visit_service_type_company_id_service_type_id"] = ServiceTypeInUse,

        // Rows that still reference a priority type.
        ["fk_branch_priority_fee_priority_types_company_id_priority_type"] = PriorityTypeInUse,
        ["fk_company_service_priority_priority_types_company_id_priority"] = PriorityTypeInUse,
        ["fk_token_priority_type_company_id_priority_type_id"] = PriorityTypeInUse,
        ["fk_token_sequence_priority_type_company_id_priority_type_id"] = PriorityTypeInUse,
        ["fk_token_visit_priority_type_company_id_priority_type_id"] = PriorityTypeInUse,

        // User assignments (STORY-003 TASK-008 note): the user or the branch / counter is not in the company.
        ["fk_user_branch_user_company"] = UserNotFound,
        ["fk_user_counter_user_company"] = UserNotFound,
        ["fk_user_branch_branch_company_id_branch_id"] = ResourceNotFound,
        ["fk_user_counter_counter_company_id_counter_id"] = ResourceNotFound,
    };

    /// <summary>Every constraint or index name the catalog knows. A test checks each exists in the schema.</summary>
    public static IReadOnlyCollection<string> ConstraintNames { get; } =
        [.. UniqueConstraints.Keys, .. ForeignKeys.Keys];

    /// <summary>The answer for a database error, or null when the SQLSTATE is not one the catalog handles.</summary>
    public static DatabaseError? Map(string? sqlState, string? constraintName)
    {
        switch (sqlState)
        {
            case UniqueViolation:
                return constraintName is not null && UniqueConstraints.TryGetValue(constraintName, out var duplicate) ? duplicate : DefaultDuplicate;
            case ForeignKeyViolation:
            case RestrictViolation:
                return constraintName is not null && ForeignKeys.TryGetValue(constraintName, out var reference) ? reference : DefaultInUse;
            case CheckViolation:
                return DefaultInvalid;
            default:
                return null;
        }
    }
}
