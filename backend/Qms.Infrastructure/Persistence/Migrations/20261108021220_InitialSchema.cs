using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Qms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asp_net_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company", x => x.id);
                    table.CheckConstraint("ck_company_currency_code", "currency_code ~ '^[A-Z]{3}$'");
                });

            migrationBuilder.CreateTable(
                name: "asp_net_role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_users", x => x.id);
                    table.CheckConstraint("ck_asp_net_users_language", "language IN ('en', 'ar')");
                    table.ForeignKey(
                        name: "fk_asp_net_users_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch", x => x.id);
                    table.UniqueConstraint("ak_branches_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_branch_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messaging_setting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    credentials = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messaging_setting", x => x.id);
                    table.ForeignKey(
                        name: "fk_messaging_setting_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "priority_type",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prefix = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    priority_level = table.Column<int>(type: "integer", nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    fee_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_priority_type", x => x.id);
                    table.UniqueConstraint("ak_priority_types_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_priority_type_fee_not_negative", "fee_amount IS NULL OR fee_amount >= 0");
                    table.CheckConstraint("ck_priority_type_fee_required_when_paid", "NOT is_paid OR fee_amount IS NOT NULL");
                    table.CheckConstraint("ck_priority_type_prefix", "prefix ~ '^[A-Z]$'");
                    table.CheckConstraint("ck_priority_type_priority_level", "priority_level >= 1");
                    table.ForeignKey(
                        name: "fk_priority_type_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_type",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prefix = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    target_minutes = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_type", x => x.id);
                    table.UniqueConstraint("ak_service_types_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_service_type_prefix", "prefix ~ '^[A-Z]$'");
                    table.CheckConstraint("ck_service_type_target_minutes", "target_minutes IS NULL OR target_minutes > 0");
                    table.ForeignKey(
                        name: "fk_service_type_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_template",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    header_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    header_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    footer_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    footer_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    show_wait = table.Column<bool>(type: "boolean", nullable: false),
                    show_people_ahead = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_template", x => x.id);
                    table.UniqueConstraint("ak_ticket_templates_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_ticket_template_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_asp_net_user_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_asp_net_user_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_token_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branch_calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_from = table.Column<DateOnly>(type: "date", nullable: false),
                    date_to = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    open_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    close_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_calendar", x => x.id);
                    table.CheckConstraint("ck_branch_calendar_date_range", "date_to >= date_from");
                    table.CheckConstraint("ck_branch_calendar_special_hours_times", "type = 'Holiday' OR (open_time IS NOT NULL AND close_time IS NOT NULL AND close_time > open_time)");
                    table.ForeignKey(
                        name: "fk_branch_calendar_branches_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_calendar_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    open_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    close_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_hours", x => x.id);
                    table.CheckConstraint("ck_branch_hours_close_after_open", "close_time > open_time");
                    table.ForeignKey(
                        name: "fk_branch_hours_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_hours_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_setting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recall_limit = table.Column<int>(type: "integer", nullable: false),
                    skip_default = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    block_when_no_counter = table.Column<bool>(type: "boolean", nullable: false),
                    wait_alert_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_setting", x => x.id);
                    table.CheckConstraint("ck_branch_setting_recall_limit", "recall_limit >= 0");
                    table.CheckConstraint("ck_branch_setting_wait_alert_minutes", "wait_alert_minutes IS NULL OR wait_alert_minutes > 0");
                    table.ForeignKey(
                        name: "fk_branch_setting_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_setting_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_branch",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_branch", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_branch_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_branch_branch_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branch",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zone",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zone", x => x.id);
                    table.UniqueConstraint("ak_zones_branch_id_id", x => new { x.branch_id, x.id });
                    table.ForeignKey(
                        name: "fk_zone_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zone_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_priority_fee",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_priority_fee", x => x.id);
                    table.CheckConstraint("ck_branch_priority_fee_not_negative", "fee_amount >= 0");
                    table.ForeignKey(
                        name: "fk_branch_priority_fee_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_priority_fee_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_priority_fee_priority_types_company_id_priority_type",
                        columns: x => new { x.company_id, x.priority_type_id },
                        principalTable: "priority_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_service_priority",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_service_priority", x => x.id);
                    table.UniqueConstraint("ak_company_service_priorities_company_id_service_type_id_prior", x => new { x.company_id, x.service_type_id, x.priority_type_id });
                    table.ForeignKey(
                        name: "fk_company_service_priority_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_service_priority_priority_types_company_id_priority",
                        columns: x => new { x.company_id, x.priority_type_id },
                        principalTable: "priority_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_service_priority_service_types_company_id_service_t",
                        columns: x => new { x.company_id, x.service_type_id },
                        principalTable: "service_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_flow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_flow", x => x.id);
                    table.CheckConstraint("ck_service_flow_different_services", "from_service_type_id <> to_service_type_id");
                    table.ForeignKey(
                        name: "fk_service_flow_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_flow_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_flow_service_types_company_id_from_service_type_id",
                        columns: x => new { x.company_id, x.from_service_type_id },
                        principalTable: "service_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_flow_service_types_company_id_to_service_type_id",
                        columns: x => new { x.company_id, x.to_service_type_id },
                        principalTable: "service_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "token_sequence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    priority_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_sequence = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_sequence", x => x.id);
                    table.CheckConstraint("ck_token_sequence_last_sequence", "last_sequence BETWEEN 0 AND 9999");
                    table.ForeignKey(
                        name: "fk_token_sequence_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_sequence_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_sequence_priority_type_company_id_priority_type_id",
                        columns: x => new { x.company_id, x.priority_type_id },
                        principalTable: "priority_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_sequence_service_type_company_id_service_type_id",
                        columns: x => new { x.company_id, x.service_type_id },
                        principalTable: "service_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "counter",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    current_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counter", x => x.id);
                    table.UniqueConstraint("ak_counter_branch_id_id", x => new { x.branch_id, x.id });
                    table.CheckConstraint("ck_counter_number", "number >= 1");
                    table.ForeignKey(
                        name: "fk_counter_asp_net_users_current_user_id",
                        column: x => x.current_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_counter_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_counter_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_counter_zones_branch_id_zone_id",
                        columns: x => new { x.branch_id, x.zone_id },
                        principalTable: "zone",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_service_priority",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_tokens_per_day = table.Column<int>(type: "integer", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_service_priority", x => x.id);
                    table.UniqueConstraint("ak_branch_service_priority_branch_id_service_type_id_priority_", x => new { x.branch_id, x.service_type_id, x.priority_type_id });
                    table.CheckConstraint("ck_branch_service_priority_max_tokens", "max_tokens_per_day BETWEEN 1 AND 9999");
                    table.ForeignKey(
                        name: "fk_branch_service_priority_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_service_priority_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_service_priority_company_service_priorities_company_",
                        columns: x => new { x.company_id, x.service_type_id, x.priority_type_id },
                        principalTable: "company_service_priority",
                        principalColumns: new[] { "company_id", "service_type_id", "priority_type_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    registration_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    registration_code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    counter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    printer_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device", x => x.id);
                    table.UniqueConstraint("ak_device_branch_id_id", x => new { x.branch_id, x.id });
                    table.UniqueConstraint("ak_device_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_device_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_counter_branch_id_counter_id",
                        columns: x => new { x.branch_id, x.counter_id },
                        principalTable: "counter",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_zones_branch_id_zone_id",
                        columns: x => new { x.branch_id, x.zone_id },
                        principalTable: "zone",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_counter",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_counter", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_counter_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_counter_counter_counter_id",
                        column: x => x.counter_id,
                        principalTable: "counter",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "counter_service_priority",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counter_service_priority", x => x.id);
                    table.ForeignKey(
                        name: "fk_counter_service_priority_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_counter_service_priority_branch_service_priority_branch_id_",
                        columns: x => new { x.branch_id, x.service_type_id, x.priority_type_id },
                        principalTable: "branch_service_priority",
                        principalColumns: new[] { "branch_id", "service_type_id", "priority_type_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_counter_service_priority_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_counter_service_priority_counter_branch_id_counter_id",
                        columns: x => new { x.branch_id, x.counter_id },
                        principalTable: "counter",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispenser_setting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    country_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    default_country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    phone_min = table.Column<int>(type: "integer", nullable: true),
                    phone_max = table.Column<int>(type: "integer", nullable: true),
                    languages = table.Column<string[]>(type: "text[]", nullable: false),
                    default_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ticket_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirm_seconds = table.Column<int>(type: "integer", nullable: false),
                    show_vip = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispenser_setting", x => x.id);
                    table.CheckConstraint("ck_dispenser_setting_confirm_seconds", "confirm_seconds >= 0");
                    table.CheckConstraint("ck_dispenser_setting_phone_range", "phone_min IS NULL OR phone_max IS NULL OR phone_min <= phone_max");
                    table.ForeignKey(
                        name: "fk_dispenser_setting_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispenser_setting_device_company_id_device_id",
                        columns: x => new { x.company_id, x.device_id },
                        principalTable: "device",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dispenser_setting_ticket_templates_company_id_ticket_templa",
                        columns: x => new { x.company_id, x.ticket_template_id },
                        principalTable: "ticket_template",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispenser_zone_service",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispenser_zone_service", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispenser_zone_service_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispenser_zone_service_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispenser_zone_service_device_branch_id_device_id",
                        columns: x => new { x.branch_id, x.device_id },
                        principalTable: "device",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispenser_zone_service_service_types_company_id_service_typ",
                        columns: x => new { x.company_id, x.service_type_id },
                        principalTable: "service_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispenser_zone_service_zones_branch_id_zone_id",
                        columns: x => new { x.branch_id, x.zone_id },
                        principalTable: "zone",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    number = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    priority_prefix = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    service_prefix = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    priority_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    customer_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_printed = table.Column<bool>(type: "boolean", nullable: false),
                    issued_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fee_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    fee_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fee_settled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fee_settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fee_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token", x => x.id);
                    table.UniqueConstraint("ak_token_branch_id_id", x => new { x.branch_id, x.id });
                    table.UniqueConstraint("ak_tokens_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_token_fee_not_negative", "fee_amount IS NULL OR fee_amount >= 0");
                    table.CheckConstraint("ck_token_priority_prefix", "priority_prefix ~ '^[A-Z]$'");
                    table.CheckConstraint("ck_token_sequence", "sequence BETWEEN 1 AND 9999");
                    table.CheckConstraint("ck_token_service_prefix", "service_prefix ~ '^[A-Z]$'");
                    table.ForeignKey(
                        name: "fk_token_asp_net_users_fee_settled_by_user_id",
                        column: x => x.fee_settled_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_asp_net_users_issued_by_user_id",
                        column: x => x.issued_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_device_branch_id_issued_device_id",
                        columns: x => new { x.branch_id, x.issued_device_id },
                        principalTable: "device",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_priority_type_company_id_priority_type_id",
                        columns: x => new { x.company_id, x.priority_type_id },
                        principalTable: "priority_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_service_type_company_id_current_service_type_id",
                        columns: x => new { x.company_id, x.current_service_type_id },
                        principalTable: "service_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_zones_branch_id_current_zone_id",
                        columns: x => new { x.branch_id, x.current_zone_id },
                        principalTable: "zone",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "message_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    template_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    to_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_log_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_message_log_tokens_company_id_token_id",
                        columns: x => new { x.company_id, x.token_id },
                        principalTable: "token",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "token_visit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    called_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recall_count = table.Column<int>(type: "integer", nullable: false),
                    is_front = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_visit", x => x.id);
                    table.CheckConstraint("ck_token_visit_recall_count", "recall_count >= 0");
                    table.ForeignKey(
                        name: "fk_token_visit_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_visit_branch_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalTable: "branch",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_visit_company_company_id",
                        column: x => x.company_id,
                        principalTable: "company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_visit_counter_branch_id_counter_id",
                        columns: x => new { x.branch_id, x.counter_id },
                        principalTable: "counter",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_visit_priority_type_company_id_priority_type_id",
                        columns: x => new { x.company_id, x.priority_type_id },
                        principalTable: "priority_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_visit_service_type_company_id_service_type_id",
                        columns: x => new { x.company_id, x.service_type_id },
                        principalTable: "service_type",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_token_visit_token_branch_id_token_id",
                        columns: x => new { x.branch_id, x.token_id },
                        principalTable: "token",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_token_visit_zones_branch_id_zone_id",
                        columns: x => new { x.branch_id, x.zone_id },
                        principalTable: "zone",
                        principalColumns: new[] { "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_role_claims_role_id",
                table: "asp_net_role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "asp_net_roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_claims_user_id",
                table: "asp_net_user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "asp_net_user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_roles_role_id",
                table: "asp_net_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "asp_net_users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_company_id",
                table: "asp_net_users",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "asp_net_users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_company_id_at",
                table: "audit_log",
                columns: new[] { "company_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_entity_id",
                table: "audit_log",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_company_id_code",
                table: "branch",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_calendar_branch_id_date_from_date_to",
                table: "branch_calendar",
                columns: new[] { "branch_id", "date_from", "date_to" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_calendar_company_id_branch_id",
                table: "branch_calendar",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_hours_branch_id_day_of_week_open_time",
                table: "branch_hours",
                columns: new[] { "branch_id", "day_of_week", "open_time" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_hours_company_id_branch_id",
                table: "branch_hours",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_priority_fee_branch_id_priority_type_id",
                table: "branch_priority_fee",
                columns: new[] { "branch_id", "priority_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_priority_fee_company_id_branch_id",
                table: "branch_priority_fee",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_priority_fee_company_id_priority_type_id",
                table: "branch_priority_fee",
                columns: new[] { "company_id", "priority_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_service_priority_company_id_branch_id",
                table: "branch_service_priority",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_service_priority_company_id_service_type_id_priority",
                table: "branch_service_priority",
                columns: new[] { "company_id", "service_type_id", "priority_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_setting_branch_id",
                table: "branch_setting",
                column: "branch_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_setting_company_id_branch_id",
                table: "branch_setting",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_company_code",
                table: "company",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_service_priority_company_id_priority_type_id",
                table: "company_service_priority",
                columns: new[] { "company_id", "priority_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_counter_branch_id_number",
                table: "counter",
                columns: new[] { "branch_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_counter_branch_id_zone_id",
                table: "counter",
                columns: new[] { "branch_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_counter_company_id_branch_id",
                table: "counter",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_counter_current_user_id",
                table: "counter",
                column: "current_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_counter_zone_id",
                table: "counter",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_counter_service_priority_branch_id_counter_id",
                table: "counter_service_priority",
                columns: new[] { "branch_id", "counter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_counter_service_priority_branch_id_service_type_id_priority",
                table: "counter_service_priority",
                columns: new[] { "branch_id", "service_type_id", "priority_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_counter_service_priority_company_id_branch_id",
                table: "counter_service_priority",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_counter_service_priority_counter_id_service_type_id_priorit",
                table: "counter_service_priority",
                columns: new[] { "counter_id", "service_type_id", "priority_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_branch_id_counter_id",
                table: "device",
                columns: new[] { "branch_id", "counter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_device_branch_id_zone_id",
                table: "device",
                columns: new[] { "branch_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_device_company_id_branch_id",
                table: "device",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_device_device_key_hash",
                table: "device",
                column: "device_key_hash",
                unique: true,
                filter: "device_key_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_registration_code",
                table: "device",
                column: "registration_code",
                unique: true,
                filter: "registration_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dispenser_setting_company_id_device_id",
                table: "dispenser_setting",
                columns: new[] { "company_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispenser_setting_company_id_ticket_template_id",
                table: "dispenser_setting",
                columns: new[] { "company_id", "ticket_template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispenser_setting_device_id",
                table: "dispenser_setting",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispenser_zone_service_branch_id_device_id",
                table: "dispenser_zone_service",
                columns: new[] { "branch_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispenser_zone_service_branch_id_zone_id",
                table: "dispenser_zone_service",
                columns: new[] { "branch_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispenser_zone_service_company_id_branch_id",
                table: "dispenser_zone_service",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispenser_zone_service_company_id_service_type_id",
                table: "dispenser_zone_service",
                columns: new[] { "company_id", "service_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispenser_zone_service_device_id_zone_id_service_type_id",
                table: "dispenser_zone_service",
                columns: new[] { "device_id", "zone_id", "service_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_log_company_id_token_id",
                table: "message_log",
                columns: new[] { "company_id", "token_id" });

            migrationBuilder.CreateIndex(
                name: "ix_message_log_token_id",
                table: "message_log",
                column: "token_id");

            migrationBuilder.CreateIndex(
                name: "ix_messaging_setting_company_id_channel",
                table: "messaging_setting",
                columns: new[] { "company_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_priority_type_company_id_prefix",
                table: "priority_type",
                columns: new[] { "company_id", "prefix" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_family_id",
                table: "refresh_token",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_token_hash",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_user_id",
                table: "refresh_token",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_flow_branch_id_from_service_type_id_to_service_type",
                table: "service_flow",
                columns: new[] { "branch_id", "from_service_type_id", "to_service_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_flow_company_id_branch_id",
                table: "service_flow",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_flow_company_id_from_service_type_id",
                table: "service_flow",
                columns: new[] { "company_id", "from_service_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_flow_company_id_to_service_type_id",
                table: "service_flow",
                columns: new[] { "company_id", "to_service_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_type_company_id_prefix",
                table: "service_type",
                columns: new[] { "company_id", "prefix" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_token_branch_id_business_date_status",
                table: "token",
                columns: new[] { "branch_id", "business_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_token_branch_id_current_zone_id",
                table: "token",
                columns: new[] { "branch_id", "current_zone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_branch_id_issued_device_id",
                table: "token",
                columns: new[] { "branch_id", "issued_device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_company_id_branch_id",
                table: "token",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_company_id_current_service_type_id",
                table: "token",
                columns: new[] { "company_id", "current_service_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_company_id_priority_type_id",
                table: "token",
                columns: new[] { "company_id", "priority_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_fee_settled_by_user_id",
                table: "token",
                column: "fee_settled_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_issued_by_user_id",
                table: "token",
                column: "issued_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_token_branch_date_number",
                table: "token",
                columns: new[] { "branch_id", "business_date", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_token_sequence_company_id_branch_id",
                table: "token_sequence",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_sequence_company_id_priority_type_id",
                table: "token_sequence",
                columns: new[] { "company_id", "priority_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_sequence_company_id_service_type_id",
                table: "token_sequence",
                columns: new[] { "company_id", "service_type_id" });

            migrationBuilder.CreateIndex(
                name: "ux_token_sequence_branch_date_priority_service",
                table: "token_sequence",
                columns: new[] { "branch_id", "business_date", "priority_type_id", "service_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_branch_id_counter_id",
                table: "token_visit",
                columns: new[] { "branch_id", "counter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_branch_id_token_id",
                table: "token_visit",
                columns: new[] { "branch_id", "token_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_branch_id_zone_id",
                table: "token_visit",
                columns: new[] { "branch_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_company_id_branch_id",
                table: "token_visit",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_company_id_priority_type_id",
                table: "token_visit",
                columns: new[] { "company_id", "priority_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_company_id_service_type_id",
                table: "token_visit",
                columns: new[] { "company_id", "service_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_queue",
                table: "token_visit",
                columns: new[] { "zone_id", "service_type_id", "priority_type_id", "is_front", "queued_at" },
                descending: new[] { false, false, false, true, false },
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_token_id",
                table: "token_visit",
                column: "token_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_visit_user_id",
                table: "token_visit",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_branch_branch_id",
                table: "user_branch",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_branch_user_id_branch_id",
                table: "user_branch",
                columns: new[] { "user_id", "branch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_counter_counter_id",
                table: "user_counter",
                column: "counter_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_counter_user_id_counter_id",
                table: "user_counter",
                columns: new[] { "user_id", "counter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_zone_company_id_branch_id",
                table: "zone",
                columns: new[] { "company_id", "branch_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asp_net_role_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_logins");

            migrationBuilder.DropTable(
                name: "asp_net_user_roles");

            migrationBuilder.DropTable(
                name: "asp_net_user_tokens");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "branch_calendar");

            migrationBuilder.DropTable(
                name: "branch_hours");

            migrationBuilder.DropTable(
                name: "branch_priority_fee");

            migrationBuilder.DropTable(
                name: "branch_setting");

            migrationBuilder.DropTable(
                name: "counter_service_priority");

            migrationBuilder.DropTable(
                name: "dispenser_setting");

            migrationBuilder.DropTable(
                name: "dispenser_zone_service");

            migrationBuilder.DropTable(
                name: "message_log");

            migrationBuilder.DropTable(
                name: "messaging_setting");

            migrationBuilder.DropTable(
                name: "refresh_token");

            migrationBuilder.DropTable(
                name: "service_flow");

            migrationBuilder.DropTable(
                name: "token_sequence");

            migrationBuilder.DropTable(
                name: "token_visit");

            migrationBuilder.DropTable(
                name: "user_branch");

            migrationBuilder.DropTable(
                name: "user_counter");

            migrationBuilder.DropTable(
                name: "asp_net_roles");

            migrationBuilder.DropTable(
                name: "branch_service_priority");

            migrationBuilder.DropTable(
                name: "ticket_template");

            migrationBuilder.DropTable(
                name: "token");

            migrationBuilder.DropTable(
                name: "company_service_priority");

            migrationBuilder.DropTable(
                name: "device");

            migrationBuilder.DropTable(
                name: "priority_type");

            migrationBuilder.DropTable(
                name: "service_type");

            migrationBuilder.DropTable(
                name: "counter");

            migrationBuilder.DropTable(
                name: "asp_net_users");

            migrationBuilder.DropTable(
                name: "zone");

            migrationBuilder.DropTable(
                name: "branch");

            migrationBuilder.DropTable(
                name: "company");
        }
    }
}
