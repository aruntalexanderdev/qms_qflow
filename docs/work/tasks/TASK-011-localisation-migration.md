# TASK-011 — Localisation migration: language registry, translated columns, user and dispenser language

| Field | Value |
|---|---|
| ID | TASK-011 |
| Story | STORY-051 |
| Stage | 2 Database |
| Agent | database-architect |
| Status | To do |
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
- [ ] Code follows common project rules (snake_case, uuid keys, `created_at`/`updated_at`)
- [ ] Tests added and passing (list them below)
- [ ] User-facing text localised (translation keys / translated fields; English required, ADR-010)
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- AC-1: registry rows, unique code, direction check, no tenant filter.
- AC-2: company defaults; default-in-enabled check; language FK.
- AC-3: migration fill on a database built at the previous schema (migrate to `RowVersion`, insert rows (a) to (e), migrate to head): all five tables.
- AC-4: `ticket_template` header/footer fill; `counter.name_translations` `{}`.
- AC-5: jsonb object check on every translations column; default `{}`.
- AC-6: user FK, old check gone.
- AC-7: dispenser language fix and entity default.
- AC-8: seed exactness for spec 4 and `ar` kept as translations.
- AC-9: converter and comparer detect add, edit and remove; no `UPDATE` when unchanged; snapshot has no `NameEn`/`NameAr`/`Header*`/`Footer*` en/ar properties.
- AC-10: Down then Up.
- AC-11: catalog mapping and catalog-names-exist test.

## 7. Notes / result
<Filled by the agent when done.>
