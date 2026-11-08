# TASK-011 — Localisation migration: language registry, translated columns, user and dispenser language

| Field | Value |
|---|---|
| ID | TASK-011 |
| Story | STORY-051 |
| Stage | 2 Database |
| Agent | database-architect |
| Status | Done |
| Estimate | L |
| Created | 2026-11-08 |

## 1. Goal
One EF Core migration (e.g. `Localisation`) that adds the `language` registry and the company language settings, replaces every `*_en` / `*_ar` pair by `<field>` + `<field>_translations` (jsonb) with the data fill, adds the user language FK, fixes the dispenser language defaults, and updates Domain/EF, the dev seeder, the error catalog and the database tests. The seed stays exact for spec section 4.

## 2. Acceptance criteria covered
- AC-1 to AC-11 (from STORY-051). AC-8 and AC-9 also protect stage 3.

## 3. Technical details
Source: STORY-051 section 7 "Database (stage 2)" items 1 to 12 and ADR-010 §2 to §4, §9, §10, §14. Do not restate the design from memory; follow those.
- **New table `language`** (global, not `ITenantEntity`, no query filter): `id`, `code` varchar(35) unique `ux_language_code`, `native_name` varchar(100), `direction` varchar(3) check `ck_language_direction`, `is_available`, `sort_order`, `created_at`, `updated_at`. Rows `en` and `ar` inserted by the migration (explicit SQL, not model seeding that changes later). Domain entity `Language`.
- **`company`**: `default_language` (FK → `language(code)`, restrict, default `'en'`), `enabled_languages` `text[]` default `'{en}'`, check `ck_company_default_language_enabled`.
- **`name` pairs** on `company`, `service_type`, `priority_type`, `branch`, `zone`: rename `name_en` → `name`, add `name_translations` jsonb (not null, default `'{}'`, object check), fill from `trim(name_ar)` when non-empty and different from `name_en`, drop `name_ar`, rename indexes and constraints containing `name_en`. Use the rename (not add-and-copy) so ids, lengths and not-null are kept.
- **`counter.name_translations`**; **`ticket_template`** `header`, `header_translations`, `footer`, `footer_translations` (same fill, drop `_ar`). Name every new check constraint explicitly (per table and column) so the catalog test can find them.
- **`asp_net_users.language`**: drop `ck_asp_net_users_language`, add `fk_asp_net_users_language_language_code` → `language(code)` restrict.
- **`dispenser_setting`**: entity default `Languages = ["en"]`; data fix removes codes the company has not enabled and resets `default_language` to the company default when no longer listed.
- **Domain/EF**: `Name` + `NameTranslations` (and `Header`/`HeaderTranslations`, `Footer`/`FooterTranslations`) replace `NameEn`/`NameAr`/`HeaderEn`/`HeaderAr`/`FooterEn`/`FooterAr`; `Counter.NameTranslations`; `Company.DefaultLanguage` / `EnabledLanguages`; translations as `IReadOnlyDictionary<string,string>` or a `LocalizedText` value object (stage 2 decides, ADR-010 §4), mapped to jsonb with value converter and value comparer. Property and column names must not collide with the Application class `LocalizedText` (STORY-005); if a value object is chosen, put it in Domain and tell stage 3 its name.
- **Keep the stage 3 code compiling**: STORY-005 Application code (`ServiceTypes`, `PriorityTypes`, `Common/LocalizedText.cs`) and tests reference `NameEn`/`NameAr`. Make the minimal edit that keeps the build and tests green (property rename, `ToStorage`/`FromStorage` adapting to `Name`/`NameTranslations` with the `ar` key only); stage 3 replaces it with `LocalizedTextRules`. Note exactly what was touched in section 7.
- **Dev seeder** (`DevSeeder`): `Name` = current English text, `NameTranslations` = `{"ar": <current Arabic>}` where it differs; Company 1 `en`/`{en}`; Dispensers `["en"]`, `en`.
- **`PostgresErrorCatalog`**: user language FK → 400 `language.not_enabled`; company default FK and check → 400 `request.invalid`; catalog test covers the new constraint names.
- **Down migration**: `translations.ar` → `name_ar` (empty string when absent), other keys lost (comment in the migration), restores `ck_asp_net_users_language`, drops `language` and the company columns.
- **Tests to update/add** (Qms.Tests/Persistence): `SchemaTests`, `SeedTests`, `ConstraintTests`, `TenantIntegrityTests`, `RowVersionTests`, `MappingChainTests`, `TenantAndAuditTests`, `UserAssignmentTests`, `SetupTestData` and test endpoints that use `NameEn`/`NameAr`; new `LocalisationMigrationTests`.

## 4. Dependencies
- Needs: STORY-005 merged to main (plumbing, `LocalizedText`, `xmin` row version). If not merged, wait (STORY-051 section 10).
- Provides to: TASK-012 (stage 3): the new entity properties, `Language` entity, company language columns, `counter.name_translations`, catalog entries, a seeded database for the ar-enabled test setup (tests flip `language.ar.is_available` and `company.enabled_languages` themselves).

## 5. Done checklist
- [x] Code follows common project rules (snake_case, uuid keys, `created_at`/`updated_at`)
- [x] Tests added and passing (list them below)
- [x] User-facing text localised (translation keys / translated fields; English required, ADR-010)
- [x] No business logic outside `Qms.Application`
- [x] Spec / ADR updated if behaviour changed (no change needed: the schema follows spec section 9 and ADR-010 as written)
- [x] Handoff written in the story's Stage log

## 6. Tests
New `Qms.Tests/Persistence/LocalisationMigrationTests.cs` (16 tests, Testcontainers PostgreSQL 18):
- AC-1: `Ac1_the_migration_seeds_exactly_en_and_ar_in_the_registry`, `Ac1_code_is_unique_and_direction_is_ltr_or_rtl`, `Ac1_the_registry_is_global_with_no_company_column_and_no_query_filter`.
- AC-2: `Ac2_a_company_inserted_without_the_new_columns_gets_en_and_the_en_list`, `Ac2_default_must_be_enabled_and_must_be_in_the_registry`, `Ac2_a_language_referenced_by_a_company_cannot_be_deleted`.
- AC-2, AC-3, AC-4, AC-6, AC-7 (migrated data): `Ac3_to_ac7_the_data_is_filled_correctly_when_migrating_from_the_previous_schema` (database migrated to `RowVersion`, rows (a) to (e) in the five tables, three ticket templates, four counters, three dispenser settings, two users, then migrated to head).
- AC-7 (entity default): `Ac7_a_dispenser_setting_created_through_the_entity_defaults_to_en`.
- AC-5: `Ac5_translations_must_be_a_json_object_and_default_to_empty`, `Ac5_every_translations_column_is_jsonb_not_null_default_empty_with_the_object_check` (all 8 columns).
- AC-8: `Ac8_the_seed_is_exact_and_keeps_the_arabic_text_as_translations`.
- AC-9: `Ac9_changes_to_the_translations_object_are_tracked_and_read_back`, `Ac9_an_unchanged_load_sends_no_update`, `Ac9_no_name_en_or_name_ar_property_remains_in_the_model_or_the_snapshot`.
- AC-10: `Ac10_down_restores_the_pairs_and_up_works_again`.
- AC-11: `Ac11_language_foreign_keys_and_the_default_check_map_to_the_catalog_answers`; the existing `PlumbingTests.Ac29_every_name_in_the_catalog_exists_in_the_migrated_schema` now covers the new names too.
Updated: `SchemaTests`, `SeedTests`, `ConstraintTests`, `TenantIntegrityTests`, `RowVersionTests`, `MappingChainTests`, `TenantAndAuditTests`, `UserAssignmentTests` (its old-schema world is now plain SQL, because `DevSeeder` and the entities no longer match the `InitialSchema` columns), `SetupPlumbingUnitTests` (adapter), and the support files (`SetupTestData`, `SetupTestEndpoints`, `TenantTestEndpoints`, `AuthHost`) and `DatabaseHostingTests`, `AuditDecoratorTests`, `BranchScopeTests`, `TenantGuardTests`.

## 7. Notes / result
Done 2026-11-08. Migration `20261108085816_Localisation` (hand-written Up and Down; the scaffolded one would drop the Arabic text).

**Schema**
- New `language` (global; `ux_language_code` is the unique constraint and the target of the language FKs; `ck_language_direction`); `en` and `ar` inserted by SQL in the migration with fixed ids `01930000-0000-7000-8000-00070000000{1,2}`.
- `company.default_language` varchar(35) default `en`, `company.enabled_languages` text[] default `{en}`; `fk_company_default_language_language_code` (restrict), `ck_company_default_language_enabled`, index `ix_company_default_language`.
- `name_en` renamed to `name`, `name_translations` jsonb added and filled, `name_ar` dropped, on company, service_type, priority_type, branch, zone. Same for `ticket_template` `header` / `footer`. `counter.name_translations` added.
- Check on every translations column: `ck_<table>_<column>_object` (for example `ck_service_type_name_translations_object`, `ck_ticket_template_header_translations_object`).
- `asp_net_users`: `ck_asp_net_users_language` dropped, `fk_asp_net_users_language_language_code` (restrict) and `ix_asp_net_users_language` added.
- `dispenser_setting`: data fix only (no column change); entity default `["en"]`.
- Fill rule: `translations.ar` is the trimmed Arabic text when non-empty and different from the English text. Trim covers space, tab, CR, LF (PostgreSQL `trim()` alone covers spaces only).
- No index or constraint was named after `name_en` in the old schema. On PostgreSQL 18 the not-null constraints are named after the column (`company_name_en_not_null`) and keep that name through a column rename, so the migration renames them (`company_name_not_null`), and Down renames them back. The rename is skipped when they do not exist (PostgreSQL 16 and 17).

**Domain and EF decision (ADR-010 section 4)**: no value object. The property type is `Dictionary<string, string>` (mutable, so in-place edits work), initialised to an empty dictionary: `NameTranslations` on Company, ServiceType, PriorityType, Branch, Zone, Counter; `HeaderTranslations`, `FooterTranslations` on TicketTemplate. Mapping is in `Qms.Infrastructure/Persistence/Configurations/TranslationsMapping.cs` (`HasTranslationsMapping()`: converter to JSON text, content-based comparer with a copying snapshot, `jsonb`, default `'{}'::jsonb`, required; `HasTranslationsCheck(table, column)`). `Company.DefaultLanguage` (`"en"`) and `Company.EnabledLanguages` (`["en"]`) are plain properties; `Language` entity is in `Qms.Domain/Entities/Localisation/Language.cs` (`DbSet<Language> Languages`).

**Touched outside persistence (minimal adapter for the existing code to compile; stage 3 replaces it)**
- `Qms.Application/Common/LocalizedText.cs`: `ToStorage` now returns `(string Name, Dictionary<string,string> Translations)` (`ar` only, trimmed, dropped when empty); `FromStorage` takes the stored translations (returns `ar` only); new `SameTranslations`. The language checks (`LocalizedTranslations`, `LocalizedName`) are unchanged. Doc comment updated.
- `ServiceTypes/` (`CreateServiceType`, `UpdateServiceType`, `ListServiceTypes`, `ServiceTypeDto`) and `PriorityTypes/` (`CreatePriorityType`, `UpdatePriorityType`, `ListPriorityTypes`, `PriorityTypeDto`): property renames `NameEn`/`NameAr` to `Name`/`NameTranslations`; the update handlers compare translations with `SameTranslations`. Behaviour is the same as before (with the adapter the seeded Arabic is still returned and `ar` is still accepted; that changes in stage 3).
- Tests: see section 6. `SetupPlumbingUnitTests.LocalizedTextTests` was rewritten for the new adapter signature.

**Not changed (deliberate)**: `asp_net_users.language` and `dispenser_setting.default_language` / `languages` stay varchar(8) (`language.code` is varchar(35)); the FK works across lengths. Widening them was not in the story.

**Down** is lossy by design (documented in the migration): only `translations.ar` survives; the dispenser language fix is not reverted; `ck_asp_net_users_language` can only be restored while every user language is `en` or `ar`.
