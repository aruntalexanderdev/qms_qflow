using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// STORY-051 / ADR-010: language registry, company languages, and every <c>*_en</c> / <c>*_ar</c> pair replaced by
    /// <c>&lt;field&gt;</c> + <c>&lt;field&gt;_translations</c> (jsonb). Hand-written: EF scaffolds "drop name_ar, rename name_en,
    /// add name_translations" and would lose the Arabic text, so the data is moved first.
    ///
    /// Up fill rule: <c>translations.ar</c> = the trimmed Arabic text, only when it is not empty after trimming and differs from
    /// the English text (STORY-005 stored an empty Arabic text when none was given). Whitespace is space, tab, CR and LF.
    /// No index or constraint had <c>name_en</c> in its name, so there is nothing to rename.
    ///
    /// Down: <c>name_ar</c> = <c>translations.ar</c>, or the empty text when absent (the STORY-005 form). Every other translation
    /// key (for example <c>fr</c>) is LOST. The dispenser language fix of Up is not reverted (the removed codes are not
    /// recoverable), and the old check <c>ck_asp_net_users_language</c> can only be restored while every user language is en or ar.
    /// </summary>
    public partial class Localisation : Migration
    {
        private static readonly string[] NameTables = ["company", "service_type", "priority_type", "branch", "zone"];

        // PostgreSQL trim() removes spaces only; the story speaks of "whitespace", so tab / CR / LF are trimmed too.
        private const string Whitespace = "E' \\t\\r\\n'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Language registry, seeded here (explicit rows: the registry changes through later migrations, not model seeding).
            migrationBuilder.CreateTable(
                name: "language",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    native_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    direction = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_language", x => x.id);
                    table.UniqueConstraint("ux_language_code", x => x.code);
                    table.CheckConstraint("ck_language_direction", "direction IN ('ltr', 'rtl')");
                });

            migrationBuilder.Sql("""
                INSERT INTO language (id, code, native_name, direction, is_available, sort_order, created_at, updated_at) VALUES
                ('01930000-0000-7000-8000-000700000001', 'en', 'English', 'ltr', true, 1, now(), now()),
                ('01930000-0000-7000-8000-000700000002', 'ar', 'العربية', 'rtl', false, 2, now(), now());
                """);

            // 2. Company languages. Existing rows get en / {en} from the column defaults.
            migrationBuilder.AddColumn<string>(
                name: "default_language",
                table: "company",
                type: "character varying(35)",
                maxLength: 35,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string[]>(
                name: "enabled_languages",
                table: "company",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{en}'::text[]");

            migrationBuilder.CreateIndex(name: "ix_company_default_language", table: "company", column: "default_language");

            migrationBuilder.AddCheckConstraint(
                name: "ck_company_default_language_enabled",
                table: "company",
                sql: "default_language = ANY(enabled_languages)");

            migrationBuilder.AddForeignKey(
                name: "fk_company_default_language_language_code",
                table: "company",
                column: "default_language",
                principalTable: "language",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            // 3. name_en / name_ar -> name + name_translations (rename keeps ids, length and not null).
            foreach (var table in NameTables)
            {
                AddTranslationsFilledFromArabic(migrationBuilder, table, "name_translations", "name_ar", "name_en");
                migrationBuilder.DropColumn(name: "name_ar", table: table);
                migrationBuilder.RenameColumn(name: "name_en", table: table, newName: "name");
            }

            // 4. Counter: translations only (the counter name is already one column).
            AddTranslationsColumn(migrationBuilder, "counter", "name_translations");

            // 5. ticket_template: header and footer.
            AddTranslationsFilledFromArabic(migrationBuilder, "ticket_template", "header_translations", "header_ar", "header_en");
            AddTranslationsFilledFromArabic(migrationBuilder, "ticket_template", "footer_translations", "footer_ar", "footer_en");
            migrationBuilder.DropColumn(name: "header_ar", table: "ticket_template");
            migrationBuilder.DropColumn(name: "footer_ar", table: "ticket_template");
            migrationBuilder.RenameColumn(name: "header_en", table: "ticket_template", newName: "header");
            migrationBuilder.RenameColumn(name: "footer_en", table: "ticket_template", newName: "footer");

            // PostgreSQL 18 names the not-null constraint of a column after it (company_name_en_not_null) and keeps that name when the
            // column is renamed. Rename them too, so no constraint still says name_en. No such rows exist before PostgreSQL 18.
            RenameNotNullConstraints(migrationBuilder, up: true);

            // 6. User language: the registry replaces the check (en, ar).
            migrationBuilder.DropCheckConstraint(name: "ck_asp_net_users_language", table: "asp_net_users");
            migrationBuilder.CreateIndex(name: "ix_asp_net_users_language", table: "asp_net_users", column: "language");
            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_users_language_language_code",
                table: "asp_net_users",
                column: "language",
                principalTable: "language",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            // 7. Dispenser languages: keep only what the company has enabled (the seed had en + ar); an emptied list becomes the
            //    company default; the default language is reset to the company default when it is no longer listed.
            migrationBuilder.Sql("""
                UPDATE dispenser_setting s
                SET languages = COALESCE(
                    NULLIF(ARRAY(
                        SELECT u.code FROM unnest(s.languages) WITH ORDINALITY AS u(code, position)
                        WHERE u.code = ANY(c.enabled_languages) ORDER BY u.position), '{}'::text[]),
                    ARRAY[c.default_language])
                FROM company c
                WHERE c.id = s.company_id;
                """);
            migrationBuilder.Sql("""
                UPDATE dispenser_setting s
                SET default_language = c.default_language
                FROM company c
                WHERE c.id = s.company_id AND NOT (s.default_language = ANY(s.languages));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // User language: back to the check (fails when a user has a language other than en / ar).
            migrationBuilder.DropForeignKey(name: "fk_asp_net_users_language_language_code", table: "asp_net_users");
            migrationBuilder.DropIndex(name: "ix_asp_net_users_language", table: "asp_net_users");
            migrationBuilder.AddCheckConstraint(
                name: "ck_asp_net_users_language",
                table: "asp_net_users",
                sql: "language IN ('en', 'ar')");

            // ticket_template: translations.ar -> *_ar (empty text when absent); other languages are lost.
            migrationBuilder.RenameColumn(name: "header", table: "ticket_template", newName: "header_en");
            migrationBuilder.RenameColumn(name: "footer", table: "ticket_template", newName: "footer_en");
            RestoreArabicColumn(migrationBuilder, "ticket_template", "header_translations", "header_ar", 500);
            RestoreArabicColumn(migrationBuilder, "ticket_template", "footer_translations", "footer_ar", 500);

            DropTranslationsColumn(migrationBuilder, "counter", "name_translations");

            foreach (var table in NameTables)
            {
                migrationBuilder.RenameColumn(name: "name", table: table, newName: "name_en");
                RestoreArabicColumn(migrationBuilder, table, "name_translations", "name_ar", 200);
            }

            RenameNotNullConstraints(migrationBuilder, up: false);

            migrationBuilder.DropForeignKey(name: "fk_company_default_language_language_code", table: "company");
            migrationBuilder.DropCheckConstraint(name: "ck_company_default_language_enabled", table: "company");
            migrationBuilder.DropIndex(name: "ix_company_default_language", table: "company");
            migrationBuilder.DropColumn(name: "enabled_languages", table: "company");
            migrationBuilder.DropColumn(name: "default_language", table: "company");

            migrationBuilder.DropTable(name: "language");
        }

        private static readonly (string Table, string Column)[] RenamedColumns =
        [
            ("company", "name"), ("service_type", "name"), ("priority_type", "name"), ("branch", "name"), ("zone", "name"),
            ("ticket_template", "header"), ("ticket_template", "footer"),
        ];

        /// <summary>
        /// Renames the PostgreSQL 18 not-null constraints of the renamed columns, when they exist: Up turns
        /// <c>company_name_en_not_null</c> into <c>company_name_not_null</c>, Down does the reverse.
        /// </summary>
        private static void RenameNotNullConstraints(MigrationBuilder migrationBuilder, bool up)
        {
            foreach (var (table, column) in RenamedColumns)
            {
                var (before, after) = up
                    ? ($"{table}_{column}_en_not_null", $"{table}_{column}_not_null")
                    : ($"{table}_{column}_not_null", $"{table}_{column}_en_not_null");
                migrationBuilder.Sql($$"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '{{before}}' AND conrelid = '{{table}}'::regclass) THEN
                            ALTER TABLE {{table}} RENAME CONSTRAINT {{before}} TO {{after}};
                        END IF;
                    END $$;
                    """);
            }
        }

        private static void AddTranslationsColumn(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.AddColumn<string>(
                name: column,
                table: table,
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddCheckConstraint(
                name: $"ck_{table}_{column}_object",
                table: table,
                sql: $"jsonb_typeof({column}) = 'object'");
        }

        private static void AddTranslationsFilledFromArabic(
            MigrationBuilder migrationBuilder, string table, string translations, string arabic, string english)
        {
            AddTranslationsColumn(migrationBuilder, table, translations);
            migrationBuilder.Sql($"""
                UPDATE {table}
                SET {translations} = jsonb_build_object('ar', btrim({arabic}, {Whitespace}))
                WHERE btrim({arabic}, {Whitespace}) <> '' AND btrim({arabic}, {Whitespace}) <> {english};
                """);
        }

        private static void DropTranslationsColumn(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.DropCheckConstraint(name: $"ck_{table}_{column}_object", table: table);
            migrationBuilder.DropColumn(name: column, table: table);
        }

        private static void RestoreArabicColumn(MigrationBuilder migrationBuilder, string table, string translations, string arabic, int length)
        {
            migrationBuilder.AddColumn<string>(
                name: arabic,
                table: table,
                type: $"character varying({length})",
                maxLength: length,
                nullable: false,
                defaultValue: "");
            migrationBuilder.Sql($"UPDATE {table} SET {arabic} = COALESCE({translations} ->> 'ar', '');");
            // The old column had no default.
            migrationBuilder.Sql($"ALTER TABLE {table} ALTER COLUMN {arabic} DROP DEFAULT;");
            DropTranslationsColumn(migrationBuilder, table, translations);
        }
    }
}
