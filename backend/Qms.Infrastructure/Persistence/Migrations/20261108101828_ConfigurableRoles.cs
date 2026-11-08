using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// STORY-048 / TASK-014 (ADR-009): configurable company roles. Creates role, role_permission and user_role, seeds the three
    /// system roles with their default grants for every existing company, moves the Identity company roles to user_role and leaves
    /// only SuperAdmin in asp_net_roles. EF Core also generated an AddColumn xmin for each table; removed (see RowVersion).
    /// </summary>
    public partial class ConfigurableRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_translations = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    branch_scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    system_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role", x => x.id);
                    table.UniqueConstraint("ak_role_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_role_branch_scope", "branch_scope IN ('Company', 'Assigned')");
                    table.CheckConstraint("ck_role_is_system_matches_system_code", "is_system = (system_code IS NOT NULL)");
                    table.CheckConstraint("ck_role_name_translations_object", "jsonb_typeof(name_translations) = 'object'");
                    table.CheckConstraint("ck_role_system_code", "system_code IS NULL OR system_code IN ('CompanyAdmin', 'BranchManager', 'CounterStaff')");
                    table.ForeignKey(
                        name: "fk_role_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permission",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permission", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_permission_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permission_role_company_id_role_id",
                        columns: x => new { x.company_id, x.role_id },
                        principalTable: "role",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_role", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_role_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_role_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_role_role_company_id_role_id",
                        columns: x => new { x.company_id, x.role_id },
                        principalTable: "role",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_role_company_id_system_code",
                table: "role",
                columns: new[] { "company_id", "system_code" },
                unique: true,
                filter: "system_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_role_permission_company_id_role_id",
                table: "role_permission",
                columns: new[] { "company_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ux_role_permission_role_id_permission",
                table: "role_permission",
                columns: new[] { "role_id", "permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_role_company_id_role_id",
                table: "user_role",
                columns: new[] { "company_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_role_user_id",
                table: "user_role",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_role_user_id_role_id",
                table: "user_role",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            // 1. Case-insensitive unique role name per company (EF cannot model an expression index) and the same-company
            //    guarantee for the user (EF cannot key the nullable AppUser.CompanyId): same pattern as fk_user_branch_user_company.
            //    A Super Admin (company_id NULL) never satisfies the non-null FK, so can never hold a company role.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_role_company_id_lower_name ON role (company_id, lower(name));");
            migrationBuilder.Sql("ALTER TABLE user_role ADD CONSTRAINT fk_user_role_user_company FOREIGN KEY (user_id, company_id) REFERENCES asp_net_users (id, company_id) ON DELETE CASCADE;");

            // 2. The three system roles for every existing company (ADR-009 section 4). Literal values: a migration never reads
            //    application code; DefaultRoles (code) must stay equal, a test compares them.
            migrationBuilder.Sql(@"
INSERT INTO role (id, company_id, name, name_translations, branch_scope, is_system, system_code, is_active, sort_order, created_at, updated_at)
SELECT gen_random_uuid(), c.id, d.name, '{}'::jsonb, d.branch_scope, true, d.system_code, true, d.sort_order, now(), now()
FROM company c
CROSS JOIN (VALUES
    ('CompanyAdmin', 'Company Admin', 'Company', 1),
    ('BranchManager', 'Branch Manager / Supervisor', 'Assigned', 2),
    ('CounterStaff', 'Counter Staff', 'Assigned', 3)
) AS d (system_code, name, branch_scope, sort_order);");

            // 3. Default grants as literal permission codes: Company Admin 60 (the M2 catalog incl. company-languages),
            //    Branch Manager / Supervisor 28, Counter Staff none.
            migrationBuilder.Sql(@"
INSERT INTO role_permission (id, company_id, role_id, permission, created_at, updated_at)
SELECT gen_random_uuid(), r.company_id, r.id, g.permission, now(), now()
FROM role r
JOIN (VALUES
    ('CompanyAdmin', 'service-types.view'),
    ('CompanyAdmin', 'service-types.create'),
    ('CompanyAdmin', 'service-types.edit'),
    ('CompanyAdmin', 'service-types.delete'),
    ('CompanyAdmin', 'priority-types.view'),
    ('CompanyAdmin', 'priority-types.create'),
    ('CompanyAdmin', 'priority-types.edit'),
    ('CompanyAdmin', 'priority-types.delete'),
    ('CompanyAdmin', 'service-mapping.view'),
    ('CompanyAdmin', 'service-mapping.edit'),
    ('CompanyAdmin', 'branches.view'),
    ('CompanyAdmin', 'branches.create'),
    ('CompanyAdmin', 'branches.edit'),
    ('CompanyAdmin', 'branches.delete'),
    ('CompanyAdmin', 'branch-hours.view'),
    ('CompanyAdmin', 'branch-hours.edit'),
    ('CompanyAdmin', 'branch-settings.view'),
    ('CompanyAdmin', 'branch-settings.edit'),
    ('CompanyAdmin', 'branch-fees.view'),
    ('CompanyAdmin', 'branch-fees.edit'),
    ('CompanyAdmin', 'branch-service-mapping.view'),
    ('CompanyAdmin', 'branch-service-mapping.edit'),
    ('CompanyAdmin', 'zones.view'),
    ('CompanyAdmin', 'zones.create'),
    ('CompanyAdmin', 'zones.edit'),
    ('CompanyAdmin', 'zones.delete'),
    ('CompanyAdmin', 'counters.view'),
    ('CompanyAdmin', 'counters.create'),
    ('CompanyAdmin', 'counters.edit'),
    ('CompanyAdmin', 'counters.delete'),
    ('CompanyAdmin', 'counter-service-mapping.view'),
    ('CompanyAdmin', 'counter-service-mapping.edit'),
    ('CompanyAdmin', 'service-flows.view'),
    ('CompanyAdmin', 'service-flows.create'),
    ('CompanyAdmin', 'service-flows.edit'),
    ('CompanyAdmin', 'service-flows.delete'),
    ('CompanyAdmin', 'users.view'),
    ('CompanyAdmin', 'users.create'),
    ('CompanyAdmin', 'users.edit'),
    ('CompanyAdmin', 'users.delete'),
    ('CompanyAdmin', 'users.reset-password'),
    ('CompanyAdmin', 'roles.view'),
    ('CompanyAdmin', 'roles.create'),
    ('CompanyAdmin', 'roles.edit'),
    ('CompanyAdmin', 'roles.delete'),
    ('CompanyAdmin', 'password-policy.view'),
    ('CompanyAdmin', 'password-policy.edit'),
    ('CompanyAdmin', 'devices.view'),
    ('CompanyAdmin', 'devices.create'),
    ('CompanyAdmin', 'devices.edit'),
    ('CompanyAdmin', 'devices.delete'),
    ('CompanyAdmin', 'devices.registration-code'),
    ('CompanyAdmin', 'dispenser-settings.view'),
    ('CompanyAdmin', 'dispenser-settings.edit'),
    ('CompanyAdmin', 'ticket-templates.view'),
    ('CompanyAdmin', 'ticket-templates.create'),
    ('CompanyAdmin', 'ticket-templates.edit'),
    ('CompanyAdmin', 'ticket-templates.delete'),
    ('CompanyAdmin', 'company-languages.view'),
    ('CompanyAdmin', 'company-languages.edit'),
    ('BranchManager', 'service-types.view'),
    ('BranchManager', 'priority-types.view'),
    ('BranchManager', 'service-mapping.view'),
    ('BranchManager', 'ticket-templates.view'),
    ('BranchManager', 'branches.view'),
    ('BranchManager', 'branch-hours.view'),
    ('BranchManager', 'branch-settings.view'),
    ('BranchManager', 'branch-fees.view'),
    ('BranchManager', 'service-flows.view'),
    ('BranchManager', 'branch-service-mapping.view'),
    ('BranchManager', 'branch-service-mapping.edit'),
    ('BranchManager', 'zones.view'),
    ('BranchManager', 'zones.create'),
    ('BranchManager', 'zones.edit'),
    ('BranchManager', 'zones.delete'),
    ('BranchManager', 'counters.view'),
    ('BranchManager', 'counters.create'),
    ('BranchManager', 'counters.edit'),
    ('BranchManager', 'counters.delete'),
    ('BranchManager', 'counter-service-mapping.view'),
    ('BranchManager', 'counter-service-mapping.edit'),
    ('BranchManager', 'devices.view'),
    ('BranchManager', 'devices.create'),
    ('BranchManager', 'devices.edit'),
    ('BranchManager', 'devices.delete'),
    ('BranchManager', 'devices.registration-code'),
    ('BranchManager', 'dispenser-settings.view'),
    ('BranchManager', 'dispenser-settings.edit')
) AS g (system_code, permission) ON g.system_code = r.system_code;");

            // 4. Move the users: each Identity company role becomes a user_role row to the matching system role of the user's company.
            migrationBuilder.Sql(@"
INSERT INTO user_role (id, company_id, user_id, role_id, created_at, updated_at)
SELECT gen_random_uuid(), u.company_id, u.id, r.id, now(), now()
FROM asp_net_user_roles ur
JOIN asp_net_roles ar ON ar.id = ur.role_id
JOIN asp_net_users u ON u.id = ur.user_id
JOIN role r ON r.company_id = u.company_id AND r.system_code = ar.name
WHERE ar.name IN ('CompanyAdmin', 'BranchManager', 'CounterStaff');");

            // Refuse (nothing changes, the migration is one transaction) when an Identity company role could not be moved,
            // for example a user without a company that holds one.
            migrationBuilder.Sql(@"
DO $$
DECLARE unmoved integer;
BEGIN
    SELECT count(*) INTO unmoved
    FROM asp_net_user_roles ur
    JOIN asp_net_roles ar ON ar.id = ur.role_id
    WHERE ar.name IN ('CompanyAdmin', 'BranchManager', 'CounterStaff')
      AND NOT EXISTS (
          SELECT 1 FROM user_role x
          JOIN role r ON r.id = x.role_id
          WHERE x.user_id = ur.user_id AND r.system_code = ar.name);
    IF unmoved > 0 THEN
        RAISE EXCEPTION 'ConfigurableRoles: % company role assignment(s) cannot move to user_role (user without a company?); fix or delete them first.', unmoved;
    END IF;
END $$;");

            // 5. Only SuperAdmin stays an Identity role (ADR-009 section 1).
            migrationBuilder.Sql("DELETE FROM asp_net_user_roles WHERE role_id IN (SELECT id FROM asp_net_roles WHERE name IN ('CompanyAdmin', 'BranchManager', 'CounterStaff'));");
            migrationBuilder.Sql("DELETE FROM asp_net_roles WHERE name IN ('CompanyAdmin', 'BranchManager', 'CounterStaff');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the Identity roles and give each user holding a system role the matching Identity role (one role per user, as
            // before: the most privileged system role, Company Admin before Branch Manager before Counter Staff). Users who hold only
            // custom roles get no Identity role and lose access on rollback; custom roles and their grants are dropped with the tables.
            migrationBuilder.Sql(@"
INSERT INTO asp_net_roles (id, name, normalized_name, concurrency_stamp)
SELECT gen_random_uuid(), v.name, upper(v.name), gen_random_uuid()::text
FROM (VALUES ('CompanyAdmin'), ('BranchManager'), ('CounterStaff')) AS v (name)
WHERE NOT EXISTS (SELECT 1 FROM asp_net_roles x WHERE x.name = v.name);");

            migrationBuilder.Sql(@"
INSERT INTO asp_net_user_roles (user_id, role_id)
SELECT DISTINCT ON (ur.user_id) ur.user_id, ar.id
FROM user_role ur
JOIN role r ON r.id = ur.role_id AND r.system_code IS NOT NULL
JOIN asp_net_roles ar ON ar.name = r.system_code
ORDER BY ur.user_id, r.sort_order;");

            migrationBuilder.DropTable(
                name: "role_permission");

            migrationBuilder.DropTable(
                name: "user_role");

            migrationBuilder.DropTable(
                name: "role");
        }
    }
}
