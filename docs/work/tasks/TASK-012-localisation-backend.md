# TASK-012 — Localisation backend: LocalizedTextRules, language APIs, company languages

| Field | Value |
|---|---|
| ID | TASK-012 |
| Story | STORY-051 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | To do |
| Estimate | L |
| Created | 2026-11-08 |

## 1. Goal
Replace STORY-005's temporary `name_en`/`name_ar` mapping with the shared `LocalizedTextRules`, add `ICompanyLanguages` and `IRequestLanguage`, and build `GET /languages`, `GET`/`PUT /company-languages` with the `company-languages` permissions, audit and tests. The STORY-005 public contract stays as it is.

## 2. Acceptance criteria covered
- AC-12 to AC-31 and AC-40 (from STORY-051). AC-1 to AC-11 are TASK-011.

## 3. Technical details
Source: STORY-051 section 7 "Backend (stage 3)", ADR-010 §3 to §7, ADR-008 (Amendments 2 and 3), ADR-009 §3, §4. Follow the TASK-010 plumbing (handlers use `IQmsDbContext`, `[Audited]`, `CommonRules.DecodeVersion`, `IPostCommitActions.Enqueue`, `[RequirePermission]`, `PostgresErrorCatalog`).
- **`Qms.Application/Common/LocalizedTextRules`** (replaces `LocalizedText.cs`; delete the old class): FluentValidation rule for the default value (per-field max length: names 200; others set by the caller) and for translations (enabled keys, case-insensitive, stored in registry form; `translation.default_language_key` for the default; `translation.language_not_enabled`; duplicate keys that differ only by case rejected at `translations.<code>`; blank values dropped; value length; errors keyed `translations.<code>` / `<field>Translations.<code>`; top-level code specific only when all failures share it). Functions `Merge`, `ForAdmin`, `Resolve`, `ResolveAll` exactly as in AC-23 to AC-25. Pure and unit-testable (no DB).
- **`ICompanyLanguages`** (Application port, scoped, memoised): default and enabled languages of the current tenant, plus the registry rows. Infrastructure implementation with one query per request. Anonymous callers and Super Admin have no company: expose only the registry.
- **`IRequestLanguage`** (Api implementation): parses `Accept-Language` (quality values, primary subtag match such as `ar-AE` → `ar`), accepts only the company's enabled languages, else the company default, else `en` for anonymous callers (AC-26). Add a test-host endpoint to assert it.
- **Service types and priority types**: switch handlers, DTO mapping and validators to `Name`/`NameTranslations` and the helper; admin reads use `ForAdmin`; `PUT` uses `Merge`; lists sort by `sortOrder` then `name` (priority types `priorityLevel` then `prefix` as before). Remove every `NameEn`/`NameAr` use and the TASK-011 adapter. Existing STORY-005 tests that send or expect `ar` move to the ar-enabled test database (AC-31).
- **Endpoints** (`/api/v1`): `GET /languages` (`[AllowAnonymous]`; caller kind decides the list; DTO `{ defaultLanguage, languages: [{ code, nativeName, direction }] }` sorted by `sort_order`); `GET /company-languages` (`company-languages.view`) → `{ defaultLanguage, enabledLanguages, availableLanguages, version }`; `PUT /company-languages` (`company-languages.edit`) request fields nullable with `CommonRules.Sent()` (full replacement, Amendment 3 A): `enabledLanguages`, `version`.
- **`PUT` handler** (`[Audited("company_languages","update")]`): load the current company row through the tenant filter; check version (`xmin` original value); normalise codes (case-insensitive, registry form); reject duplicates (`validation.failed`), unknown/unavailable (`company_language.not_available`), missing default (`company_language.default_required`); for every language removed, find company dispensers whose `dispenser_setting.languages` list it → 409 `company_language.in_use` with the devices (id, name) in the problem details; save `enabled_languages`; enqueue `IDeviceConfigNotifier.ConfigChangedAsync` for every branch via `IPostCommitActions`. A body `defaultLanguage` is ignored. A no-op `PUT` is audited and issues no `UPDATE` (Amendment 3 B).
- **Permissions**: constants `company-languages.view|edit` in `Permissions` (and `Permissions.All`), static `RolePermissionMap` grants both to Company Admin only (Branch Manager, Counter Staff, Super Admin none); keep the invariants (edit implies view). Module description for `GET /permissions` catalog if the catalog class already has one.
- **Error codes**: add `company_language.not_available`, `company_language.default_required`, `company_language.in_use`, `language.not_enabled` to `ErrorCodes`.
- **Not in this task**: `PUT /auth/me/language` (STORY-011), anything resolving texts per `Accept-Language` in a public endpoint (STORY-048 and M3 onwards), device APIs.

## 4. Dependencies
- Needs: TASK-011 (columns, `Language` entity, catalog entries, seed), STORY-005 code.
- Provides to: TASK-013 (`GET /languages` contract, `Accept-Language` accepted and ignored by existing endpoints), STORY-048, 011, 012, 013, 047 (helper, ports, `IRequestLanguage`).

## 5. Done checklist
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] User-facing text localised (translation keys / translated fields; English required, ADR-010)
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- Unit: AC-23 (rules), AC-24 (merge, `ForAdmin`), AC-25 (`Resolve`, `ResolveAll`), AC-26 (Accept-Language negotiation).
- Integration (ADR-008 Amendment 3 E pattern, `SetupHost`): `/languages` anonymous, company user, Super Admin (AC-12 to AC-14); `/company-languages` GET, permissions, happy path with audit row and notifier call, not available, default required, duplicates and missing fields, in use by a dispenser, stale version and no-op, cross-company (AC-15 to AC-22, AC-40); a missing-field test for the `PUT`.
- Service and priority types on phase 1 and ar-enabled databases (AC-27 to AC-30): `ar` → 400, seeded Arabic not returned, valid `AR` saved as `ar`, `fr`/`en` rejected, disabled-language keys kept on `PUT` and returned again after re-enabling, both resources.
- AC-31: STORY-005 tests green after moving the `ar` ones; permission reflection test and role-map invariants include the new module; no `name_en`/`name_ar` in Application code (grep/architecture test).
- Test helper: `SetupTestData` gets a method that makes `ar` available and enables it for Company 1 (and a way to list `ar` on Dispenser 1).

## 7. Notes / result
<Filled by the agent when done.>
