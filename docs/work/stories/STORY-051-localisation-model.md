# STORY-051 — Localisation model: language registry, company languages, translated fields

| Field | Value |
|---|---|
| ID | STORY-051 |
| Status | QA |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 2 (Web i18n, Display voice), 7 (dispenser languages), 8 (`/login` language choice, `/admin/languages`), 9 (translated text pattern, `language`, `company`, all former `*_en` / `*_ar` columns, user `language`, `dispenser_setting.languages`), 10 (`/languages`, `/company-languages`), 11 (Languages), 12 (M2), 13 (localisation resolved) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **English to be the default language, with other languages enabled only when my company needs them and master data translated where I choose**, so that **phase 1 runs cleanly in English, and adding a language later is a matter of configuration rather than a software change**.

## 2. Background
On 2026-11-08 the product owner replaced the fixed English + Arabic design with configurable localisation (**ADR-010**, Accepted). The main points:
- English is the default (fixed `en` in phase 1).
- A deployment-wide language registry lists what a release ships. The Company Admin enables languages for the company.
- Master data text is a required English value plus optional translations (`<field>_translations` jsonb) with English fallback.
- UI texts come from translation files.
- Phase 1 ships and enables English only.

Today the schema still has `name_en` / `name_ar` on `company`, `service_type`, `priority_type`, `branch` and `zone`, and `header_*` / `footer_*` on `ticket_template` (STORY-002). Further consequences of the old design:
- Users' `language` is limited to `en` / `ar` by a check constraint.
- Dispensers default to `languages = {en, ar}`.
- The web app hard-codes `en` / `ar` (STORY-004).

STORY-005 (stage 3) already exposes the target API contract, `name` + `translations`, through a temporary mapping in `Qms.Application/Common/LocalizedText.cs`. That mapping stores `name` in `name_en` and `translations.ar` in `name_ar` (an empty string when absent), and it refuses other keys.

This story replaces the storage before any real data exists. It is placed **right after STORY-005** and before STORY-048, so that every later M2 story builds its text columns directly on the new pattern: `role.name` (048), `consent_text` (012) and `fee_line` (013). No second migration is needed.

Depends on STORY-005 (plumbing, `LocalizedText`, permissions catalog and static resolver, service and priority type endpoints).

## 3. Scope
**In scope**
- Database: `language` registry and seed; `company.default_language` / `enabled_languages`; every `*_en` / `*_ar` pair on `company`, `service_type`, `priority_type`, `branch`, `zone` and `ticket_template` (header, footer) replaced by `<field>` + `<field>_translations`; `counter.name_translations`; user `language` FK; dispenser language defaults; Domain/EF jsonb mapping; dev seeder; error catalog; DB tests.
- Backend: shared `LocalizedTextRules`; `ICompanyLanguages`; `IRequestLanguage`; `GET /languages`; `GET` / `PUT /company-languages`; permission module `company-languages`; STORY-005 service and priority types moved to the new storage and helper.
- Frontend: language list from `GET /languages`, switch hidden with one language, `dir` by direction, `Accept-Language` header, `ar.json` kept but not offered.

**Out of scope** (each belongs to the story named)
- `PUT /auth/me/language`, user create/update language validation: STORY-011 (the 051 / 011 split of section 7 stands).
- Database role names, role permission defaults in the database: STORY-048 (it adds `company-languages` to the database default grants).
- Consent text and dispenser language validation: STORY-012. Template texts API and ticket preview language: STORY-013. Company create with default language: STORY-047.
- `/admin/languages` screen: M4 (STORY-050). The shell switch calling `PUT /auth/me/language`: M4 (STORY-026).
- An API to change `language.is_available` or `company.default_language`: not built; both need a release or a new ADR (ADR-010 §1, §2).
- Android and displays: no work in this story.
- Voice clips, ticket fixed labels per language, ticket printed in two languages: M3 to M7.

## 4. Business rules
| # | Rule | Source |
|---|---|---|
| BR-1 | English (`en`) is the default language of the application and of every company in phase 1. `company.default_language` exists but no API changes it. | ADR-010 §1; spec 13 (localisation) |
| BR-2 | The language registry is global (not tenant data). Seed: `en` "English" ltr available order 1; `ar` "العربية" rtl **not available** order 2. `is_available` changes only through a release. | ADR-010 §2; spec 9 `language` |
| BR-3 | A company has `default_language` (`en`) and `enabled_languages` (`{en}`); the default must be in the enabled list. Only available languages can be enabled. | ADR-010 §3; spec 9 `company` |
| BR-4 | The Company Admin changes the enabled languages with `PUT /company-languages` (permission `company-languages.edit`). The default is always included (400 `company_language.default_required`). A language that is not available is refused (400 `company_language.not_available`). | ADR-010 §3; spec 10 |
| BR-5 | Disabling a language that a dispenser of the company still lists is refused (409 `company_language.in_use`, devices listed). Disabling keeps all stored translations. | ADR-010 §3 |
| BR-6 | Master data text = required default-language value `<field>` + optional `<field>_translations` (flat object, never contains the default language). Unique constraints apply to the default value only. | ADR-010 §4; spec 9 |
| BR-7 | Translation keys must be enabled for the caller's company, matched case-insensitively and stored in registry form (`AR` becomes `ar`); the default language as a key gives `translation.default_language_key`; a key that is not enabled gives `translation.language_not_enabled`. Keys differing only by case are refused. | ADR-010 §5; STORY-005 review N2 |
| BR-8 | Translation values are trimmed; null, empty or whitespace means "no translation" and is dropped without error; a non-empty value has the same length limit as the default value (names 1 to 200). | ADR-010 §5 |
| BR-9 | `PUT` replaces the translations of **enabled** languages; stored translations of languages that are not enabled are kept unchanged and are not returned. | ADR-010 §5; ADR-008 Amendment 2 |
| BR-10 | Fallback: a requested language that is enabled and has a value gives that value; anything else gives the default value, field by field, never an error. A requested language that is not enabled is treated as the default. | ADR-010 §6 |
| BR-11 | Admin APIs return only the keys of enabled non-default languages; lists sort by `sortOrder`, then `name`. | ADR-010 §7.1; ADR-008 Amendment 2 |
| BR-12 | `GET /languages`: anonymous or Super Admin gets the available registry languages; a company user gets the company's enabled languages; always with `defaultLanguage` and each language's `direction`; no tenant data for anonymous callers. | ADR-010 §8; spec 10 |
| BR-13 | User `language` references the registry (FK). A stored language that is later disabled stays stored and resolves to the default at read time. | ADR-010 §9 |
| BR-14 | A new dispenser setting defaults to `languages = {<company default>}`, `default_language = <company default>` (phase 1: `{en}`, `en`). | ADR-010 §10; ADR-008 Amendment 2 §14 |
| BR-15 | Web language precedence: before sign-in `qms.lang` if offered, else default; after sign-in, restore or refresh `qms.lang` if offered, else `user.language` if offered, else default. A stored choice that is no longer offered is replaced, never shown. | ADR-007 Amendment 1 |
| BR-16 | `<html dir>` follows the language's `direction`. The switch lists offered languages by `nativeName` and is hidden when only one is offered. The web app sends `Accept-Language: <active code>` on same-origin `/api/` calls. | ADR-007 Amendment 1; ADR-010 §7.2, §8 |
| BR-17 | `en.json` must contain every key; other files are optional; the key-parity check runs for available languages only. `ar.json` is kept but not offered. | ADR-010 §8 |
| BR-18 | `PUT /company-languages` follows the setup conventions: full replacement (missing field is 400), `version` = company row version (stale gives 409 `request.conflict`), audit row `company_languages` / `update`, device config notification after commit (no-op in M2), no-op `PUT` still audited. | ADR-008 §5, §11, §12; Amendment 3 A, B |
| BR-19 | Permission module `company-languages` with actions `view` and `edit`; default grant Company Admin only; Super Admin has no company and gets 403 on company endpoints. | ADR-009 §3, §4, Amendment 1; ADR-008 Amendment 1 A |

## 5. Acceptance criteria
Test data: seeded spec §4 data (Company 1, Branch 1, Zone 1, Counter 2, Services 1 to 6 with prefixes A, B, C, D, F, G, Normal N level 2, VIP V level 1 paid 50.00 AED, Dispenser 1 and 2). Users from `SetupTestData` (ADR-008 Amendment 3 E): CA-C1 (Company Admin, Company 1), CA-C2 (Company 2), BM-B1 (Branch Manager, Branch 1), CS (Counter Staff, Counter 1), SA (Super Admin). "ar-enabled test database" means a test database where `language.ar.is_available` is set to true and `ar` is enabled for Company 1 (and not for Company 2); phase 1 databases never have this state.

### Database (stage 2, TASK-011)

**AC-1: Language registry seeded by the migration**
- Given a new database migrated to head
- When the `language` table is read
- Then it has exactly two rows ordered by `sort_order`: `en` / "English" / `ltr` / `is_available` true / 1, and `ar` / "العربية" / `rtl` / `is_available` false / 2; `code` is unique (`ux_language_code`), `direction` outside `ltr` / `rtl` is rejected by `ck_language_direction`, the table has no `company_id` and no tenant query filter, and `created_at` / `updated_at` are set.

**AC-2: Company language settings**
- Given a database migrated from the previous schema that already contains Company 1
- When the migration has run
- Then Company 1 has `default_language` = `en` and `enabled_languages` = `{en}`; inserting a company without these columns gets the same defaults; setting `default_language` = `ar` with `enabled_languages` = `{en}` is rejected by `ck_company_default_language_enabled`; setting `default_language` = `xx` (not in `language`) is rejected by the foreign key (restrict).

**AC-3: Name pairs become name plus translations (data fill)**
- Given a database at the previous schema with rows in `service_type`, `priority_type`, `branch`, `zone` and `company` where (a) `name_en` "Cards" and `name_ar` "البطاقات", (b) `name_en` "Cards" and `name_ar` "Cards", (c) `name_en` "Cards" and `name_ar` "" (the STORY-005 "no Arabic" value), (d) `name_ar` "  " (whitespace), (e) `name_ar` " البطاقات " (padded)
- When the migration runs
- Then in every table `name` holds the old `name_en` (same length limit, still not null), `name_ar` and `name_en` no longer exist, and `name_translations` is: (a) `{"ar": "البطاقات"}`, (b) `{}`, (c) `{}`, (d) `{}`, (e) `{"ar": "البطاقات"}` (trimmed); the column default is `'{}'`; every index or constraint that had `name_en` in its name is renamed with `name` and still enforces the same rule.

**AC-4: Counter and ticket template**
- Given the previous schema with `ticket_template` rows having `header_en` / `header_ar` / `footer_en` / `footer_ar` (one row with Arabic different from English, one with Arabic equal to English, one with empty Arabic)
- When the migration runs
- Then `ticket_template` has `header`, `header_translations`, `footer`, `footer_translations` filled by the same rule as AC-3 and no `_en` / `_ar` column; `counter` has a new `name_translations` (`{}` for all existing counters, including Counter 1 to Counter 4) and `counter.name` is unchanged.

**AC-5: Translations column constraints**
- Given the migrated database
- When a row is inserted into `service_type` with `name_translations` = `'[]'` (an array), `'"x"'` (a string) or `'null'`, or with `name_translations` omitted
- Then the first three are rejected by the `jsonb_typeof(...) = 'object'` check (SQLSTATE 23514, constraint named per table and column) and the omitted value defaults to `{}`; the same check exists on every `<field>_translations` column (company, service_type, priority_type, branch, zone, counter, ticket_template header and footer).

**AC-6: User language foreign key**
- Given users with `language` `en` and `ar` (as in the previous schema)
- When the migration runs
- Then `ck_asp_net_users_language` is gone, `fk_asp_net_users_language_language_code` → `language(code)` (restrict) exists, existing users keep their values, and inserting a user with `language` = `fr` is rejected (23503).

**AC-7: Dispenser language data fix**
- Given the previous schema with the seeded Dispenser 1 and Dispenser 2 settings (`languages` `{en, ar}`, `default_language` `en`) and one extra setting with `default_language` `ar` and `languages` `{en, ar}`, and Company 1 with `{en}` after AC-2
- When the migration runs
- Then the languages the company has not enabled are removed (`{en}` for all three), the `default_language` `en` is kept, the extra setting's default `ar` is replaced by the company default `en`, and a new `dispenser_setting` created through the entity gets `languages` `{en}` and `default_language` `en`.

**AC-8: Seed still exact for spec section 4**
- Given a fresh database created by the migration and the Development seeder
- When the seed is read
- Then Company 1, Branch 1, Zone 1, Zone 2, Counters 1 to 4, Services 1 to 6 (prefixes A, B, C, D, F, G), Normal N and VIP V, mappings, Dispenser 1 and 2 and their mappings are exactly as before the migration (same ids, codes, prefixes, counts and `name` values equal to the former English text); the seeded Arabic text is kept as `name_translations` = `{"ar": "<former Arabic text>"}` where it differs from the English text; Company 1 is `en` / `{en}`; both dispensers have `languages` `{en}` and `default_language` `en`.

**AC-9: Domain and EF mapping tracks translation changes**
- Given the EF model with `NameTranslations` mapped to jsonb (value converter and value comparer)
- When a loaded service type's translations are changed in memory (key added, value edited, key removed) and `SaveChanges` runs, and when a second context loads the row
- Then each change is detected and saved (the row version changes) and read back identically; an unchanged load does not issue an `UPDATE`; a snapshot test shows no `NameEn` / `NameAr` / `HeaderEn` / `HeaderAr` / `FooterEn` / `FooterAr` property remains.

**AC-10: Down migration**
- Given a database at the new schema with a service type whose `name` is "Cards" and `name_translations` `{"ar": "البطاقات", "fr": "Cartes"}` and one with `{}`
- When the migration is reverted
- Then `name_en` = "Cards" and `name_ar` = "البطاقات" for the first, `name_ar` = "" for the second, the `fr` text is lost (documented in the migration), `language`, the company language columns and the user FK are gone, the old `ck_asp_net_users_language` check is back, and re-applying the migration works.

**AC-11: Error catalog**
- Given `PostgresErrorCatalog`
- When a user is saved with an unknown `language` (FK violation), and when a company is saved with a default outside the enabled list (check) or an unknown default (FK)
- Then the first maps to 400 `language.not_enabled` and the other two to 400 `request.invalid`; the catalog test finds every new constraint name in the model or migration.

### Backend (stage 3, TASK-012)

**AC-12: GET /languages, anonymous**
- Given the phase 1 database (`en` available, `ar` not available)
- When an anonymous caller sends `GET /api/v1/languages`
- Then 200 with `{ "defaultLanguage": "en", "languages": [ { "code": "en", "nativeName": "English", "direction": "ltr" } ] }`; no company data is in the response and no audit row is written.

**AC-13: GET /languages, company user**
- Given the ar-enabled test database (Company 1 has `{en, ar}`, Company 2 has `{en}`)
- When CA-C1, then CA-C2, then an anonymous caller call `GET /api/v1/languages`
- Then CA-C1 gets `en` (English, ltr) and `ar` (العربية, rtl) sorted by `sort_order`; CA-C2 gets only `en`; the anonymous caller gets both (both are available); in all three `defaultLanguage` is `en`. In the phase 1 database CA-C1 gets only `en`.

**AC-14: GET /languages, Super Admin**
- Given the ar-enabled test database
- When SA sends `GET /api/v1/languages` with a valid token
- Then 200 with the available registry languages (`en`, `ar`), not a company's setting; in the phase 1 database only `en`.

**AC-15: GET /company-languages**
- Given the ar-enabled test database
- When CA-C1 sends `GET /api/v1/company-languages`
- Then 200 with `defaultLanguage` "en", `enabledLanguages` `["en","ar"]`, `availableLanguages` listing `en` and `ar` with `code`, `nativeName`, `direction`, and a non-empty string `version`. In the phase 1 database `enabledLanguages` is `["en"]` and `availableLanguages` has only `en`.

**AC-16: Permissions on /company-languages**
- Given the permission module `company-languages` with actions `view` and `edit`
- When each of the following calls `GET` and `PUT /api/v1/company-languages`: anonymous, CS, BM-B1, SA, CA-C1
- Then anonymous gets 401 `auth.unauthorized`; CS, BM-B1 and SA get 403 `auth.forbidden` on both (BM-B1 has no `company-languages` grant by default; SA has no company); CA-C1 gets 200 on both. `Permissions.All` contains `company-languages.view` and `company-languages.edit`, the static map grants both to Company Admin only, `edit` implies `view`, and the reflection test finds `[RequirePermission]` on both actions.

**AC-17: PUT /company-languages, enable Arabic**
- Given `ar` is available (`is_available` true) and Company 1 has `{en}`, and CA-C1 read `version` v1
- When CA-C1 sends `PUT /api/v1/company-languages` with `{ "enabledLanguages": ["en", "ar"], "version": v1 }`
- Then 200 with `enabledLanguages` `["en","ar"]`, `defaultLanguage` "en" and a new `version`; `GET /languages` as CA-C1 now lists `ar`; exactly one `audit_log` row exists (`entity` `company_languages`, `action` `update`, `company_id` Company 1, `user_id` CA-C1, `data` = the command as sent); `IDeviceConfigNotifier.ConfigChangedAsync` ran once for each branch of Company 1 after the commit.

**AC-18: PUT /company-languages, language not available or unknown**
- Given the phase 1 database (`ar` not available) and Company 1 `{en}`
- When CA-C1 sends `{ "enabledLanguages": ["en", "ar"], "version": v }`, and then `{ "enabledLanguages": ["en", "fr"], "version": v }`
- Then both get 400 `company_language.not_available` (the second because `fr` is not in the registry), the response names the offending code, nothing changes and no audit row is written. Codes are matched case-insensitively and stored in registry form (`"EN"` is accepted as `en`).

**AC-19: PUT /company-languages, default required, duplicates, shape**
- Given the ar-enabled test database
- When CA-C1 sends `["ar"]` (default removed), `["en","en"]`, `[]`, a missing `enabledLanguages`, a missing `version`, and a body with `"defaultLanguage": "ar"`
- Then `["ar"]` and `[]` give 400 `company_language.default_required`; `["en","en"]` gives 400 `validation.failed` (duplicate); a missing `enabledLanguages` or `version` gives 400 `validation.failed` naming the field; the `defaultLanguage` property is ignored, `default_language` stays `en` and no API changes it; nothing is saved and no audit row is written in any case.

**AC-20: PUT /company-languages, disabling a language in use**
- Given the ar-enabled test database where Dispenser 1 (Branch 1) has `languages` `{en, ar}` and Dispenser 2 has `{en}`
- When CA-C1 sends `{ "enabledLanguages": ["en"], "version": v }`
- Then 409 `company_language.in_use` and the response lists Dispenser 1 (id and name) but not Dispenser 2; Company 1 is unchanged and no audit row is written. After Dispenser 1's settings are changed to `{en}` (test data update) the same `PUT` returns 200, and `ar` translations already stored on service types are still in the database.

**AC-21: PUT /company-languages, stale version and no-op**
- Given CA-C1 read `version` v1, then another `PUT` succeeded (version v2)
- When CA-C1 sends the first body again with `version` v1, and later a `PUT` with the same values and the current version
- Then the stale one gets 409 `request.conflict` with no change and no audit row; the equal-values `PUT` returns 200, writes one audit row, sends no `UPDATE`, and `version` is unchanged.

**AC-22: Cross-company isolation of /company-languages**
- Given Company 1 has `{en, ar}` and Company 2 has `{en}` (ar-enabled test database)
- When CA-C2 sends `GET /api/v1/company-languages`, and `PUT` with `["en","ar"]` and Company 1's `version`
- Then the `GET` shows only Company 2's setting (`enabledLanguages` `["en"]`, Company 2's own `version`); the `PUT` with Company 1's `version` gets 409 `request.conflict` (the endpoint has no company id, so it can only ever act on Company 2's row) and changes nothing; the same `PUT` with Company 2's own `version` returns 200 for Company 2 only, Company 1 still has `{en, ar}`, and the audit row has Company 2's `company_id`.

**AC-23: Shared LocalizedTextRules, key and value validation (unit)**
- Given `LocalizedTextRules` with Company 1 enabled `{en, ar}` and, in a second case, `{en}`
- When it validates translations `{"ar": "البطاقات"}`, `{"AR": "البطاقات"}`, `{"ar": "  "}`, `{"ar": null}`, `{"ar": "<201 characters>"}`, `{"en": "Cards"}`, `{"fr": "Cartes"}`, `{"ar": "x", "AR": "y"}`
- Then: the first two pass and the key is stored as `ar`; the third and fourth pass and the key is dropped; the fifth fails at `translations.ar`; the sixth fails with `translation.default_language_key` at `translations.en`; the seventh fails with `translation.language_not_enabled` at `translations.fr`; the eighth fails at `translations.ar` (duplicate by case). With `{en}` only, the first case fails with `translation.language_not_enabled` at `translations.ar`. The default value must be 1 to 200 characters after trimming (a field limit of 500 is accepted for other fields). The top-level code is the specific code when every failure shares it, else `validation.failed`.

**AC-24: Merge keeps translations of disabled languages (unit)**
- Given stored translations `{"ar": "البطاقات", "fr": "Cartes"}` and enabled languages `{en, ar}`
- When `Merge(stored, submitted, enabled)` runs with submitted `{"ar": "بطاقات"}`, with `{}`, and with `{"ar": ""}`
- Then the results are `{"ar": "بطاقات", "fr": "Cartes"}`, `{"fr": "Cartes"}`, `{"fr": "Cartes"}`: enabled keys are replaced or removed, the key `fr` of a language that is not enabled is always kept. `ForAdmin(translations, enabled)` returns only `ar` for the first result.

**AC-25: Fallback and resolved maps (unit)**
- Given a service type with `name` "Cards" and translations `{"ar": "البطاقات"}`, enabled `{en, ar}`
- When `Resolve` is called for `ar`, `en`, `fr`, `AR` and for a field with no translation, and `ResolveAll` for device languages `[en, ar]`
- Then the results are "البطاقات", "Cards", "Cards" (`fr` not enabled), "البطاقات", and the default value for the untranslated field; `ResolveAll` returns `{ "en": "Cards", "ar": "البطاقات" }` with every requested key present, and `{ "en": "Cards", "ar": "Cards" }` for the untranslated field. With enabled `{en}` the same calls return "Cards" for `ar`.

**AC-26: Accept-Language negotiation and company languages port**
- Given the ar-enabled test database and a test-host endpoint that returns `IRequestLanguage.Current`
- When CA-C1 calls it with `Accept-Language` `ar`, `ar-AE`, `fr`, `fr, ar;q=0.8`, no header, and CA-C2 (Company 2 has only `en`) calls it with `ar`
- Then the results are `ar`, `ar` (primary subtag matched to an enabled language), `en`, `ar`, `en`, and `en`; an anonymous request resolves to `en`. `ICompanyLanguages` runs at most one company-language query per request however many validators and handlers use it (a command interceptor counts the SQL statements).

**AC-27: Service types accept enabled languages only (phase 1)**
- Given the phase 1 database (Company 1 `{en}`) and no Service 7
- When CA-C1 sends `POST /api/v1/service-types` with `name` "Cards", prefix "H", and `translations` `{"ar": "البطاقات"}`, and then `{"fr": "Cartes"}`, and then `{"en": "Cards"}`
- Then each gets 400 with `errors` keyed `translations.ar`, `translations.fr`, `translations.en` and the top-level codes `translation.language_not_enabled`, `translation.language_not_enabled`, `translation.default_language_key`; nothing is saved. A body with no `translations`, `{}` or `{"ar": ""}` is 201 with `translations` `{}` (empty values are dropped, not an error).

**AC-28: Seeded Arabic is stored but not returned in phase 1**
- Given the phase 1 database where the seeded Service 2 has the seeded Arabic text in `name_translations`
- When CA-C1 calls `GET /api/v1/service-types` and `GET /api/v1/service-types/{Service 2 id}`
- Then the six items (prefixes A, B, C, D, F, G) come back sorted by `sortOrder`, then `name`, each with `translations` `{}`; `name` is the stored English text; the database still holds the Arabic text. The same holds for Normal and VIP on `/priority-types`.

**AC-29: Arabic saved and returned when enabled**
- Given the ar-enabled test database
- When CA-C1 posts a service type `name` "Cards", prefix "H", `translations` `{"AR": " البطاقات "}`, then reads it, then `PUT`s it with `translations` `{"ar": "بطاقات"}` and the current `version`
- Then 201 and the DTO shows `translations` `{"ar": "البطاقات"}` (key stored as `ar`, value trimmed); the `GET` returns the same; the `PUT` returns `{"ar": "بطاقات"}`; `{"fr": "Cartes"}` is still 400 `translation.language_not_enabled`; `PUT` with `{}` clears `ar`. The same rules hold for `POST` and `PUT /priority-types` (Normal N, VIP V; `fr` → 400; `en` → 400 `translation.default_language_key`).

**AC-30: PUT keeps translations of a disabled language**
- Given the ar-enabled test database with a service type "Cards" having `{"ar": "البطاقات"}`
- When CA-C1 disables `ar` (AC-17 in reverse, no dispenser lists it), then `PUT`s the service type with a new `name` "Cards and loans" and no `translations`, then re-enables `ar` and reads it
- Then after disabling, `GET` shows `translations` `{}`; the `PUT` returns 200 with `translations` `{}`; the database still holds `{"ar": "البطاقات"}`; after re-enabling `GET` returns `{"ar": "البطاقات"}`. The same check holds for a priority type.

**AC-31: STORY-005 contract and plumbing unchanged**
- Given the migrated storage and `LocalizedTextRules` replacing `LocalizedText`
- When the STORY-005 integration and unit tests run
- Then every STORY-005 test passes except the ones that send or expect `ar`, which move to the ar-enabled test database (accepted contract change: with only `en` enabled, `ar` is 400 and the seeded Arabic is not returned); DTO shapes `{ id, name, translations, prefix, … , version }`, the `translation.language_not_enabled` / `translation.default_language_key` codes, audit rows, 404 isolation and 409 behaviour are unchanged; there is no `name_en` / `name_ar` reference left in Application code; the case-insensitive duplicate-key rule (STORY-005 review N2) is kept; `Permissions.All` lists the new module and the role-map invariant tests still pass.

### Frontend (stage 4, TASK-013)

**AC-32: Phase 1 login page, English only**
- Given the phase 1 stack (`GET /languages` returns only `en`) and nothing in `localStorage['qms.lang']`
- When an anonymous user opens `/login`
- Then `GET /api/v1/languages` is called once without a token; the page is English, `<html lang="en" dir="ltr">`, no language switch is rendered (not just visually hidden), and signing in as the seeded Company Admin shows the English shell with no switch in the top bar.

**AC-33: Two offered languages, switch and RTL**
- Given a fake list `[ {en, English, ltr}, {ar, العربية, rtl} ]` (component test) or the ar-enabled stack (e2e)
- When the user opens `/login` and chooses العربية
- Then the switch lists "English" and "العربية" by `nativeName`, `<html lang="ar" dir="rtl">` is set, translated texts that exist in `ar.json` are Arabic and missing keys show English, `qms.lang` = `ar`, no request is sent to the server by the switch, and later `/api/` calls carry `Accept-Language: ar`. For a fake rtl language `xx` the same `dir="rtl"` is set from its `direction`; for a fake ltr language `dir="ltr"`.

**AC-34: Precedence with offered languages**
- Given offered languages `[en]` (phase 1) and a signed-in user whose `language` is `ar`
- When `qms.lang` is (a) not set, (b) `ar`, (c) `en`, and the user signs in or the page is reloaded
- Then in (a) and (b) the UI is English and `qms.lang` becomes `en` (`ar` is not offered, so the stored value is replaced and never shown, and `user.language` `ar` is not offered either, so the default applies); in (c) English. With offered `[en, ar]`: (a) gives `ar` from `user.language` and stores it, (b) `ar`, (c) `en`. Before sign-in with `qms.lang` `ar` and offered `[en]`, the page is English.

**AC-35: Offered list is re-read for the signed-in user**
- Given an anonymous start-up that loaded the deployment list and CA-C1 (ar-enabled stack, Company 1 `{en, ar}`) signs in, and later CA-C2 (Company 2 `{en}`) signs in after sign-out in the same tab
- When each sign-in, start-up restore or session adopted from another tab completes
- Then `GET /languages` is called again with the token; CA-C1 sees both languages in the switch, CA-C2 sees none (switch hidden); SA sees the available languages; if CA-C1's active language is `ar` and CA-C2 signs in, the UI becomes English and `qms.lang` is `en`.

**AC-36: Accept-Language header scope**
- Given the auth interceptor
- When a same-origin `/api/` call, a call to another origin and a non-API same-origin asset request are made with active language `en`
- Then only the same-origin `/api/` call carries `Accept-Language: en`; the others carry none (the bearer-token rule of ADR-007 is unchanged).

**AC-37: Translation files**
- Given `public/i18n/en.json` and the kept `public/i18n/ar.json`
- When the unit tests and the key-parity check run, once with only `en` available and once with a fake available `ar`
- Then with only `en` available the check requires every key used in the app to exist in `en.json` and ignores `ar.json`; with `ar` available the check fails if `ar.json` lacks a key of `en.json`; `ar.json` is still present, loads when selected and falls back to `en` for missing keys; ngx-translate's fallback language is `en`.

**AC-38: Existing STORY-004 criteria still hold**
- Given the STORY-004 acceptance criteria AC-1 to AC-18
- When the web tests run on the phase 1 stack and, for AC-3 and AC-17, on the fake two-language list or ar-enabled stack
- Then AC-1, AC-2, AC-4 to AC-16 and AC-18 pass unchanged; AC-3 (Arabic and RTL) and AC-17 (language precedence) pass when `ar` is offered and are replaced in phase 1 by AC-33 and AC-34 (the "Arabic switch" is not shown while only English is offered: accepted consequence of ADR-010); the `Language` type is a `string` and no `'en' | 'ar'` literal or `SUPPORTED_LANGUAGES` constant remains.

**AC-39: List cannot be loaded**
- Given `GET /languages` fails (network error or 500) at start-up
- When the login page opens
- Then the page is English with no switch (the default language `en` is always available in phase 1), the login form still works, and the next successful call to `/languages` (after sign-in) updates the list. No error is shown for this failure.

### Cross-cutting

**AC-40: Audit and privacy**
- Given any `PUT /company-languages` outcome in AC-17 to AC-22
- When the `audit_log` is read
- Then rows exist only for 200 outcomes (including the no-op `PUT`), none for 400, 403, 404 and 409; `GET /languages` and `GET /company-languages` write none; `data` contains no personal data (the command only holds language codes and `version`).

## 6. UI / UX
No new screen. The `/admin/languages` screen is M4 (STORY-050).
- **Language switch** (login page and shell top bar, existing STORY-004 component): a list of the offered languages, each labelled with its `nativeName` ("English", "العربية"), which is never translated. It is **hidden (not rendered) when only one language is offered**, which is the phase 1 state. Keyboard, focus ring and contrast follow STORY-004 AC-8 and the design system (rules 11); no new component style.
- **Direction:** the page direction comes from the selected language's `direction`; CSS logical properties stay mandatory so an rtl language works when enabled.
- **Fonts:** Instrument Sans and IBM Plex Sans Arabic stay bundled (no CDN); the Arabic font files are downloaded by the browser only when Arabic-script text is rendered (ADR-010 §12, `unicode-range`). Not changed by this story; stage 4 must not add conditional font loading code.
- **Translatable texts:** this story adds no new user-facing text except what stage 4 needs for an optional accessible name of the switch (reuse the existing STORY-004 key). API error codes added here (`company_language.not_available`, `company_language.default_required`, `company_language.in_use`, `translation.language_not_enabled`, `translation.default_language_key`, `language.not_enabled`) get `errors.*` entries in `en.json` in the M4 admin screen story (STORY-050); no screen shows them in this story.
- **Localisation of data:** English is required everywhere in phase 1; other languages are optional and fall back to English (ADR-010 §6).

## 7. Data and API impact
**Spec:** sections 2, 7, 8, 9, 10, 11, 12, 13 changed on 2026-11-08 with the product owner's approval (ADR-010). No queue rule changes.

**Database (stage 2)**: one migration, e.g. `Localisation`.
1. **New table `language`** (global, **not** a tenant entity, no query filter):
   - columns: `id` uuid, `code` varchar(35) unique (`ux_language_code`), `native_name` varchar(100), `direction` varchar(3) with check `ck_language_direction` (`ltr` / `rtl`), `is_available` bool, `sort_order` int, `created_at`, `updated_at`;
   - seeded **in the migration**: `en` ("English", ltr, available, 1) and `ar` ("العربية", rtl, not available, 2);
   - Domain entity `Language`.
2. **`company`**: add `default_language` (varchar(35), not null, default `'en'`, FK → `language(code)`, restrict) and `enabled_languages` (`text[]`, not null, default `'{en}'`), with check `ck_company_default_language_enabled` (`default_language = ANY(enabled_languages)`). Existing rows get `en` / `{en}`.
3. **Replace the pairs** on `company`, `service_type`, `priority_type`, `branch` and `zone`:
   - rename `name_en` → `name` (data, length and not-null kept);
   - add `name_translations` (jsonb, not null, default `'{}'`, check `jsonb_typeof(name_translations) = 'object'`);
   - fill it with `jsonb_build_object('ar', trim(name_ar))` where `trim(name_ar) <> ''` and `trim(name_ar) <> name_en`;
   - drop `name_ar`;
   - rename any index or constraint whose name contains `name_en`.
4. **`counter`**: add `name_translations` (same definition; the product owner listed counter names).
5. **`ticket_template`**: `header_en` → `header` + `header_translations`, and `footer_en` → `footer` + `footer_translations`, with the same fill rule from `header_ar` / `footer_ar`; then drop the `_ar` columns. (`fee_line` is created later by STORY-013 in the new form.)
6. **`asp_net_users.language`**: drop `ck_asp_net_users_language`; add FK `fk_asp_net_users_language_language_code` → `language(code)` (restrict). Existing values `en` / `ar` both exist in the registry.
7. **`dispenser_setting`**:
   - entity default `Languages = ["en"]`;
   - data fix: remove the codes the device's company has not enabled (seed `{en, ar}` → `{en}`);
   - `default_language` is kept when still listed, else set to the company default.
8. **Domain and EF:** `NameEn` / `NameAr` → `Name` + `NameTranslations`, and `HeaderEn` / `HeaderAr` / `FooterEn` / `FooterAr` → `Header` / `HeaderTranslations` / `Footer` / `FooterTranslations`. Translations are `IReadOnlyDictionary<string, string>` (or a `LocalizedText` value object, stage 2 decides, ADR-010 §4), mapped to `jsonb` with a value converter **and** a value comparer, so that changes are tracked.
9. **Down migration:** reverses the steps. `translations.ar` → `name_ar` (empty when absent); other keys are lost. This is documented in the migration.
10. **Dev seeder:** `Name` gets the current English text, and `NameTranslations` keeps the current Arabic seed text as `{"ar": …}` (invisible while `ar` is not enabled). Company 1: `{en}`. Dispensers: `["en"]`, `en`.
11. **`PostgresErrorCatalog`:**
    - user language FK → `language.not_enabled`;
    - company default FK and check → 400 `request.invalid` (the Application pre-checks, so these are only backstops);
    - a catalog test covers the new names.
12. **DB tests:** schema, seed and constraint tests updated. Add tests for:
    - the jsonb object check;
    - the default-in-enabled check;
    - the language FK;
    - migration fill, including a row whose `name_ar` equals `name_en` and a row whose `name_ar` is empty.

**Backend (stage 3)**
- **Shared helper `LocalizedTextRules`** (Application, ADR-010 §5–6). It replaces the temporary storage mapping in `LocalizedText.cs`; the public contract of STORY-005 is unchanged. It provides:
  - FluentValidation rules for the default value (with a per-field length) and for translations. Keys must be enabled for the company and are matched case-insensitively. The default-language key is refused. Empty values are dropped. Values follow the same length and content rules as the default value. Codes: `translation.language_not_enabled`, `translation.default_language_key`.
  - `Merge(stored, submitted, enabledLanguages)`: replaces the enabled keys and keeps stored keys of languages that are not enabled.
  - `ForAdmin(translations, enabled)`: returns only the enabled non-default keys.
  - `Resolve(value, translations, language, enabled)`: one resolved text.
  - `ResolveAll(value, translations, languages)`: a map with every key present, for the device APIs in M3.
- **`ICompanyLanguages`** (Application port, scoped, one query per request): the current company's default and enabled languages, and the registry. It is used by validators (injected) and handlers.
- **`IRequestLanguage`** (Api implementation): negotiates `Accept-Language` against the company's enabled languages, else the default. STORY-048 uses it for role names in `/auth/me`; M3–M8 use it for resolved web APIs.
- **Service and priority types** (STORY-005 code): switch to the new columns and the helper.
  - Admin responses return only the keys of enabled languages, so the seeded Arabic is **not** returned while `ar` is not enabled.
  - A `PUT` keeps the stored `ar`.
  - Sort by `sortOrder`, then `name`.
- **Endpoints:**

| Method | Route | Access | Notes |
|---|---|---|---|
| GET | `/api/v1/languages` | anonymous allowed | `{ defaultLanguage, languages: [{ code, nativeName, direction }] }`, sorted by `sort_order`. Anonymous caller or Super Admin: the available registry languages. Company user: the company's enabled languages. No tenant data for anonymous callers. Not audited |
| GET | `/api/v1/company-languages` | `company-languages.view` | `{ defaultLanguage, enabledLanguages: string[], availableLanguages: [{ code, nativeName, direction }], version }` (`version` = company row version, ADR-008 §5) |
| PUT | `/api/v1/company-languages` | `company-languages.edit` | `{ enabledLanguages, version }`. Each code must be available (400 `company_language.not_available`). The default must be included (400 `company_language.default_required`). Duplicates → 400. Disabling a language still listed by a dispenser of the company → 409 `company_language.in_use` (lists the devices). Stale `version` → 409. Audited (`company_languages`, `update`). Then `IDeviceConfigNotifier.ConfigChangedAsync` for every branch (no-op in M2) |

- **Permissions:** new module `company-languages` (view, edit) in `Permissions` and the static `RolePermissionMap`. The default grant is Company Admin. STORY-048 adds it to the database default grants (ADR-009 Amendment 1).
- **Not in this story:** `PUT /auth/me/language` (STORY-011 validates it against the offered languages); user creation (STORY-011, STORY-047); dispenser settings validation (STORY-012); template texts API (STORY-013).
- **Tests:**
  - Unit tests for the helper: keys, case, default key, empty values, merge preservation, fallback, `ResolveAll`.
  - Integration tests for `/languages`: anonymous, company user and Super Admin.
  - Integration tests for `/company-languages`: happy path with an audit row; 400 not available; 400 default removed; 409 in use by a dispenser; 409 stale version; 401 anonymous; 403 without the permission; Company 2 sees only its own setting.
  - Service types with a test database where `ar` is made available and enabled for Company 1: a valid `ar` saves and is returned; `fr` → 400 `translation.language_not_enabled`; after disabling `ar`, a `PUT` without `ar` keeps the stored `ar`, and it is returned again after re-enabling.

**Web (stage 4)**: frontend switch only.
- `LanguageService`:
  - the hard-coded `SUPPORTED_LANGUAGES` / `Language = 'en' | 'ar'` are replaced by the list from `GET /api/v1/languages`, read at start-up (anonymous) and re-read after sign-in, restore or user switch;
  - precedence per ADR-007 Amendment 1;
  - `<html dir>` comes from the language's `direction`;
  - ngx-translate fallback stays `en`.
- `LanguageSwitchComponent` (login page and shell): lists offered languages by `nativeName` and is **hidden when only one is offered**.
- Auth interceptor: adds `Accept-Language: <active code>` to same-origin `/api/` calls (ADR-010 §7.2).
- `auth.models.ts`: `language: string`.
- `public/i18n/ar.json` is **kept** but not offered. The test translation loader and any key-parity check require only `en.json` to be complete; parity is enforced only for languages that are available.
- Component tests:
  - the switch is hidden with one language and shown with two (fake list);
  - `dir="rtl"` is set for a fake rtl language;
  - the precedence cases: stored but not offered → replaced; `user.language` offered → used; neither → default.
- No new screen: `/admin/languages` is M4 (STORY-050).

**Android / displays:** none in this story. M3 (`/device/config`, `POST /tokens` `language`), M6 and M7 build directly on ADR-010.

**Cross-cutting:**
- **Security:** anonymous `/languages` exposes only the registry, never company data; permission checks on `/company-languages`.
- **Audit:** the company languages update.
- **Performance:** `ICompanyLanguages` adds at most one indexed query per request, memoised.
- **Privacy:** no personal data involved.
- **Bilingual:** English only in phase 1 (rules §7).

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-010 (new); ADR-007 Amendment 1, ADR-008 Amendment 2, ADR-009 Amendment 1 |
| 1 BA | Yes | — | This analysis |
| 2 Database | Yes | [TASK-011](../tasks/TASK-011-localisation-migration.md) (AC-1 to AC-11) | `language` table and seed; company language columns; `*_en` / `*_ar` → `<field>` + `<field>_translations` with data fill; `counter.name_translations`; user language FK; dispenser language data fix; Domain/EF jsonb mapping; dev seeder; catalog; DB tests |
| 3 Backend | Yes | [TASK-012](../tasks/TASK-012-localisation-backend.md) (AC-12 to AC-31, AC-40) | `LocalizedTextRules` replacing the STORY-005 temporary mapping; `ICompanyLanguages`, `IRequestLanguage`; `GET /languages`, `GET` / `PUT /company-languages`; `company-languages` permissions; service/priority types on the new storage; tests |
| 4 Frontend | Yes | [TASK-013](../tasks/TASK-013-localisation-frontend.md) (AC-32 to AC-39) | Language list from the API, switch hidden with one language, `dir` by direction, `Accept-Language` header, `ar.json` kept but not offered, component tests |
| 5 Android | N/A | — | No Android code exists yet; M7 (STORY-033 to STORY-036) builds on ADR-010 directly |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
- **Performance:** `ICompanyLanguages` adds at most one indexed query per request, memoised per request (AC-26). `GET /languages` for anonymous callers reads the small registry table only. No JSON-content queries or indexes in phase 1.
- **Security:** anonymous `GET /languages` exposes the registry only, never company data (AC-12); `/company-languages` is permission-guarded and acts only on the caller's company, with no company id in the route or body (AC-16, AC-22); Super Admin gets 403 on company endpoints.
- **Data safety:** the migration keeps all existing Arabic text (as `translations.ar`) and has a documented Down; only keys other than `ar` are lost on Down (AC-10). Disabling a language never deletes translations (AC-30).
- **Privacy:** no personal data; audit `data` holds language codes and `version` only (AC-40).
- **Localisation:** English only in phase 1 (rules 7, ADR-010); the mechanism is tested with an ar-enabled test database, not shipped enabled.
- **Compatibility:** accepted contract changes for STORY-005 (AC-31): `ar` in `translations` is 400 until Arabic is available and enabled, and the seeded Arabic is not returned in phase 1. STORY-004 AC-3 and AC-17 apply only when Arabic is offered (AC-38).
- **Concurrency:** `PUT /company-languages` uses the company row version; two Company Admins editing at once get 409 for the second (AC-21).
- **Test data:** ar-enabled test database = `language.ar.is_available` set true by the test and `ar` enabled for Company 1 only; never part of the shipped seed.

## 10. Open questions
- [ ] None blocking.
- [ ] BA interpretation to confirm (non-blocking, tests follow it): an unknown language code in `PUT /company-languages` (for example `fr`, not in the registry) returns 400 `company_language.not_available`, the same as a registry language that is not available (ADR-010 §3 says the Application validates that every enabled language "exists and is available" with that one code). Duplicates in `enabledLanguages` give 400 `validation.failed`. A `defaultLanguage` property in the body is ignored (AC-18, AC-19).
- [ ] BA interpretation to confirm (non-blocking): if `GET /languages` fails, the web app falls back to English with no switch (AC-39); ADR-007 Amendment 1 does not cover this case. English is always available in phase 1.
- [ ] For STORY-048 (not this story): ADR-007 Amendment 1 B.5 says the app re-reads `GET /auth/me` after a language switch because role names are resolved per `Accept-Language`. No role names are resolved yet, so TASK-013 does not add that call; STORY-048's frontend stage adds it. The switch is hidden in phase 1 anyway.
- [ ] STORY-004 AC-3 (Arabic and RTL) and AC-17 (precedence) cannot be demonstrated on the phase 1 stack because Arabic is not offered. AC-38 re-bases them on a fake list and the ar-enabled stack; QA of this story runs them on that stack. Confirm that QA accepts this instead of editing the STORY-004 file.
- [ ] Note: placement right after STORY-005 (instead of after STORY-048 or at the end of M2) is deliberate. Roles (048), consent text (012), fee line (013), company create (047) and user language (011) all write translated text or languages. Built later, each of them would need a second migration and a contract change. If STORY-005 is not merged when stage 2 starts, stage 2 waits for it.
- [ ] Note for the BA: the default zone created with a branch (STORY-007) is now "Main zone" with no Arabic translation (ADR-008 Amendment 2 §14). The STORY-007 question about the Arabic default name falls away.
- [ ] Later (not this story): Super Admin API for `language.is_available` (global settings); a ticket printed in two languages at once (spec §13 open row); a per-display language setting (M6).

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 6 note, 7, 8, 10); `docs/adr/ADR-010-localisation.md` (new); ADR-001 (fonts note), ADR-007 (Amendment 1), ADR-008 (Amendment 2, §8), ADR-009 (Amendment 1); `docs/design/DESIGN_SYSTEM.md` (font and RTL wording); `docs/PROJECT_SPEC.md` §2, §7–§13 (approved by the product owner 2026-11-08); `docs/rules/common-project-rules.md` §5, §7; `docs/rules/workflow-rules.md` stage 4/5 exit wording; STORY-007, 009, 011, 012, 013, 047, 048, 049 (amendments); `docs/work/BACKLOG.md`
Notes for next stage:
- Runs right after STORY-005 and before STORY-048. All later M2 stories assume the new columns, `LocalizedTextRules` and `ICompanyLanguages`.
- Keep STORY-005's public contract (`name`, `translations`) unchanged; only storage and validation source change. Error code: STORY-005 should already use `translation.language_not_enabled` (see the STORY-005 note in the architect's reply). If it shipped `translations.unsupported_language`, this story renames it, and the BA lists that as an accepted contract change.
- Phase 1 behaviour after this story: only `en` is offered anywhere; seeded Arabic names stay in the database and are invisible.
- The BA creates three tasks (stages 2, 3, 4).
Open questions: none blocking.

### Stage 1 — Analysis — business-analyst — 2026-11-08
Result: Done
Changed: this story (sections 3, 4, 5, 6, 8 links, 9, 10); new [TASK-011](../tasks/TASK-011-localisation-migration.md), [TASK-012](../tasks/TASK-012-localisation-backend.md), [TASK-013](../tasks/TASK-013-localisation-frontend.md).
Notes for next stage:
- 40 acceptance criteria: AC-1 to AC-11 database (TASK-011), AC-12 to AC-31 backend, AC-40 audit/privacy (TASK-012), AC-32 to AC-39 frontend (TASK-013).
- Stage 2: the data fill cases are AC-3 (rows a to e: Arabic different, equal, empty, whitespace, padded) and AC-4 (`ticket_template`); keep STORY-005 code compiling with a minimal adapter and say what was touched (TASK-011 section 3). `PUT /auth/me/language` is STORY-011, not here (the split stands).
- Accepted contract changes for STORY-005 (AC-31): `ar` in `translations` is 400 `translation.language_not_enabled` in phase 1, and the seeded Arabic is stored but not returned. STORY-005's error codes already match ADR-010 (`translation.language_not_enabled`, `translation.default_language_key`), so no code rename is needed. The case-insensitive duplicate-key rule from STORY-005 review N2 is kept (BR-7). STORY-005 tests that use `ar` move to an ar-enabled test database (Company 1 `{en, ar}`, `language.ar.is_available` true by test SQL, never in the shipped seed).
- Permissions: `company-languages.view|edit` default grant Company Admin only; Branch Manager, Counter Staff and Super Admin get 403 (BR-19, AC-16). Cross-company isolation is AC-22 (no company id in the route, so it can only act on the caller's company).
- STORY-004 AC-3 and AC-17 apply only when Arabic is offered; AC-38 re-bases them (see section 10).
Open questions: none blocking. Two BA interpretations to confirm (unknown language code in `PUT` gives `company_language.not_available`; language-list fetch failure falls back to English) and the STORY-048 note about re-reading `/auth/me` after a switch; all in section 10.

### Stage 2 — Database — database-architect — 2026-11-08
Result: Done
Changed: migration `backend/Qms.Infrastructure/Persistence/Migrations/20261108085816_Localisation.cs` (+ Designer, snapshot); `Qms.Domain/Entities/Localisation/Language.cs` (new); `Company`, `ServiceType`, `PriorityType`, `Branch`, `Zone`, `Counter`, `TicketTemplate`, `DispenserSetting` entities; `Persistence/Configurations/TranslationsMapping.cs` (new), `OrganizationConfigurations.cs`, `MessagingAndIdentityConfigurations.cs`, `QmsDbContext.cs` (`Languages`), `Seed/DevSeeder.cs`; `Errors/PostgresErrorCatalog.cs`; adapter in `Qms.Application/Common/LocalizedText.cs` and the property renames in `Qms.Application/ServiceTypes/*` and `PriorityTypes/*`; tests (new `Qms.Tests/Persistence/LocalisationMigrationTests.cs`, 16 tests; existing persistence, support and unit tests updated). Task: [TASK-011](../tasks/TASK-011-localisation-migration.md) Done. Build: 0 warnings, 0 errors. Tests: 408 passed, 0 failed, 0 skipped.
Notes for next stage:
- Entities: `Name` + `NameTranslations` (`Dictionary<string,string>`, never null, empty by default) on Company, ServiceType, PriorityType, Branch, Zone; `Counter.NameTranslations`; `TicketTemplate.Header/HeaderTranslations/Footer/FooterTranslations`; `Company.DefaultLanguage` / `EnabledLanguages` (string[]); `Language` (`Code`, `NativeName`, `Direction`, `IsAvailable`, `SortOrder`) via `db.Languages` (no tenant filter, readable with no company). Mutating the dictionary in place is tracked (value comparer); no query can filter on JSON content.
- Constraint names for the catalog and tests: `ux_language_code`, `ck_language_direction`, `ck_company_default_language_enabled`, `fk_company_default_language_language_code`, `fk_asp_net_users_language_language_code`, `ck_<table>_<column>_object` for the 8 translations columns. Catalog: user language FK is 400 `language.not_enabled`; company default FK and check are 400 `request.invalid`.
- The adapter in `LocalizedText` is temporary and still hard-codes `en` / `ar`: replace it with `LocalizedTextRules`, `Merge` (keep keys of disabled languages), `ForAdmin`. Until then the seeded Arabic is still returned and `ar` still accepted (AC-28/AC-31 change that). Lists should sort by `Name` (done) then id; `sortOrder` first for service types.
- The ar-enabled test database is made by the test: `UPDATE language SET is_available = true WHERE code = 'ar'` and `UPDATE company SET enabled_languages = '{en,ar}' WHERE id = <Company 1>` (the check allows it). The seed has no `ar` enabled anywhere.
- Mapping `Language` rows by `code` is the FK target (alternate key `ux_language_code`); `language.id` is not referenced.
- `asp_net_users.language` and `dispenser_setting` language columns are still varchar(8) (registry codes are varchar(35)); widen in the story that validates them if a longer code is ever shipped.
- Down loses translation keys other than `ar` and does not undo the dispenser language fix (documented in the migration).
- PostgreSQL 18 (test container) names not-null constraints after the column; the migration renames them, and a model check still passes (`HasPendingModelChanges` is false).
Open questions: none

### Stage 3 — Backend — backend-developer — 2026-11-08
Result: Done
Changed: `Qms.Application/Common/LocalizedTextRules.cs` (new; `LocalizedText.cs` deleted); `Qms.Application/Localisation/*` (new: `LanguageModels.cs`, `AcceptLanguageNegotiator.cs`, `Languages/ListLanguages.cs`, `CompanyLanguages/*`); `Common/Error.cs` (optional `Extensions`), `ErrorCodes.cs`, `IQmsDbContext.cs` (`Languages`), `Auth/Permissions.cs`; service types and priority types (validators, handlers, DTOs); `Qms.Infrastructure/Localisation/CompanyLanguages.cs` and its registration; `Qms.Api/Controllers/LanguagesController.cs`, `CompanyLanguagesController.cs`, `Common/RequestLanguage.cs`, `Common/ResultExtensions.cs`, `Program.cs`; tests (new `LocalizedTextRulesTests`, `LanguagesApiTests`, `CompanyLanguagesApiTests`, `LocalisedNamesApiTests`, `FakeCompanyLanguages`; STORY-005 tests moved to the ar-enabled database via `SetupHost.StartWithArabicAsync`; architecture tests extended). Task: [TASK-012](../tasks/TASK-012-localisation-backend.md) Done. Build: 0 warnings, 0 errors. Tests: 489 passed, 0 failed, 0 skipped (run twice).
Notes for next stage:
- `GET /api/v1/languages` (anonymous allowed; send the token when signed in): `{ "defaultLanguage": "en", "languages": [ { "code": "en", "nativeName": "English", "direction": "ltr" } ] }`, sorted by registry `sort_order`. No token or Super Admin: the languages the release makes available. Company user (any role, no permission needed): the languages the company enabled. Re-read after sign-in, session restore and user switch. An invalid or expired token on this endpoint is treated as anonymous (200), it is not a 401.
- `Accept-Language` is accepted by every endpoint and changes nothing yet: service type and priority type admin responses are not resolved by it (raw `name` + `translations`). Only the test host reads it (`IRequestLanguage`). Negotiation, for the later resolved APIs: quality order, primary subtag (`ar-AE` matches `ar`), only the company's enabled languages, else the company default; anonymous and Super Admin get `en`.
- `translations` in service/priority type responses holds only enabled non-default languages: with `{en}` it is always `{}`, even though the seed stores Arabic. Sending `ar` is 400 `translation.language_not_enabled` (error key `translations.ar`) until Arabic is available and enabled; `en` as a key is 400 `translation.default_language_key`; empty or blank values are dropped. Keys are case-insensitive and returned in registry form. A `PUT` without `translations` keeps the stored texts of languages that are not enabled.
- `GET /api/v1/company-languages` (`company-languages.view`): `{ defaultLanguage, enabledLanguages: string[], availableLanguages: [{ code, nativeName, direction }], version }`. `PUT` (`company-languages.edit`): `{ enabledLanguages: string[], version }` (both required; `defaultLanguage` in the body is ignored). Errors: 400 `company_language.not_available` / `company_language.default_required` (`errors.enabledLanguages`), 400 `validation.failed` (duplicates, blank code, missing field), 409 `company_language.in_use` (problem body has `devices: [{ id, name }]` and `languages: [..]`), 409 `request.conflict` (stale `version`). Company Admin only (Branch Manager, Counter Staff and Super Admin get 403, anonymous 401). New error codes need `errors.*` entries when STORY-050 builds the screen.
- Test helpers for stage 4 e2e: none new for the browser; backend tests make Arabic available with `UPDATE language SET is_available = true WHERE code = 'ar'` and `UPDATE company SET enabled_languages = '{en,ar}'`.
Open questions: none. Deviations to confirm (non-blocking): `IRequestLanguage` is `GetCurrentAsync(ct)` instead of a sync `Current`; a 200 `PUT /company-languages` that carries an ignored `defaultLanguage` is audited like any other 200.

### Stage 4 — Frontend — frontend-developer — 2026-11-08
Result: Done
Changed: frontend/src/app/core/{api/languages.api.ts, api/models/language.models.ts, api/models/auth.models.ts, i18n/language.service.ts, i18n/language-state.ts, auth/auth.interceptor.ts}, shared/language-switch.component.ts, layout/topbar, features/auth/login (the switch is not rendered with one language), specs (new `language-switch`, `language.service`, `translations`, interceptor additions; login, shell adapted), frontend/src/testing/language-fakes.ts, frontend/tsconfig.app.json. Task: [TASK-013](../tasks/TASK-013-localisation-frontend.md) Done.
Notes for next stage:
- Lint clean; `npm test -- --watch=false` 13 files / 115 tests green; `npm run build` green. E2E on the real stack (API + postgres:18 + ng serve): phase 1 login makes one anonymous `GET /languages`, shows no switch, `lang=en dir=ltr`; after sign-in still English, no switch; with `ar` made available and enabled for C1 by SQL the switch shows English and العربية, Arabic flips to RTL (sidebar right), `Accept-Language: ar` is sent, reload keeps it; disabling `ar` for the company again replaces the stored `ar` with `en`; `/languages` 500 gives English with no switch and login works.
- QA ar-enabled procedure: `UPDATE language SET is_available=true WHERE code='ar'; UPDATE company SET enabled_languages='{en,ar}' WHERE code='C1';` (company code `C1` in the dev seed). Revert with `enabled_languages='{en}'` (and `is_available=false`).
- AC-3 and AC-17 of STORY-004 are covered by the two-language fake-list tests and the ar-enabled e2e; in phase 1 the Arabic switch is not shown (ADR-010).
- The web does not re-read `/auth/me` after a switch and does not call `PUT /auth/me/language` (STORY-048 and M4).
- Stage 5 (Android) is N/A.
Open questions: none


### Stage 5 — Android — android-developer — 2026-11-08
Result: N/A — No Android code exists yet (stage plan)
Changed: none
Notes for next stage: Ready for code review (stage 6).
Open questions: none

### Stage 6 — Code review — code-reviewer — 2026-11-08
Result: Approved
Builds/tests: backend ✅ (`dotnet build -c Release` 0 warnings / 0 errors; `dotnet test -c Release` 489 passed, 0 failed, 0 skipped, 3 min 30 s) · web ✅ (`npm ci`; `npm run lint` clean (ng lint + stylelint); `npm test -- --watch=false` 13 files / 115 tests passed; `npm run build` green) · android N/A
ACs checked: AC-1 ✅, AC-2 ✅, AC-3 ✅ (see S2), AC-4 ✅, AC-5 ✅, AC-6 ✅, AC-7 ✅, AC-8 ✅, AC-9 ✅, AC-10 ✅, AC-11 ✅, AC-12 ✅, AC-13 ✅, AC-14 ✅, AC-15 ✅, AC-16 ✅, AC-17 ✅, AC-18 ✅, AC-19 ✅, AC-20 ✅ (see S1), AC-21 ✅, AC-22 ✅, AC-23 ✅, AC-24 ✅, AC-25 ✅, AC-26 ✅, AC-27 ✅, AC-28 ✅, AC-29 ✅, AC-30 ✅, AC-31 ✅, AC-32 ✅, AC-33 ✅, AC-34 ✅, AC-35 ✅ (see S3), AC-36 ✅, AC-37 ✅, AC-38 ✅, AC-39 ✅, AC-40 ✅

What was checked (summary):
- Migration: the fill uses `btrim(x, E' \t\r\n')` and skips blank values and values equal to the English text. Ids, length and not-null survive the renames (`RenameColumn`). The PostgreSQL 18 `*_en_not_null` constraints are renamed in both directions, guarded by `pg_constraint`. No `xmin` DDL; `xmin` appears only in the model/snapshot as the row version. The Down migration documents its losses (non-`ar` keys, the dispenser fix not reverted, the user check needing en/ar only); its test re-applies Up. `HasPendingModelChanges` is green. The jsonb converter plus content comparer (copy snapshot, order-independent hash) tracks in-place add/edit/remove; a new dictionary with the same content and an unchanged load send no `UPDATE` (tests at LocalisationMigrationTests.cs:354, :402).
- Tenant safety: `CompanyLanguages` joins the registry to `db.Companies`, and the `company.Id == CurrentCompanyId` query filter leaves only the caller's company. With no company, only the registry is read. `/languages` returns registry data only to anonymous callers and the Super Admin. An invalid token on `/languages` gives the anonymous answer, which is the same as calling without a token, so it cannot be used to probe anything (accepted). The 409 `devices` list comes from tenant-filtered `DispenserSettings`/`Device`, so devices of other companies cannot appear (see S1 for the test).
- LocalizedTextRules: keys are matched case-insensitively against the enabled list, duplicates by case are refused, and `en` gives `translation.default_language_key`. Blank values are dropped before key checks (matches AC-27: `{"ar": ""}` gives 201 in phase 1). `Merge` keeps stored keys of languages that are not enabled. `ForAdmin` returns only enabled non-default keys. `Resolve`/`ResolveAll` fall back field by field. The negotiator handles q-values, `q=0`, `*`, invalid q, primary subtag and a 32-entry cap. `ICompanyLanguages` runs one query per request (AC-26 test).
- PUT /company-languages: a full replacement (`Sent`, duplicates, blank code) → 404 → stale version 409 → not_available → default_required → in_use 409 → save with `xmin` original value. A no-op writes the audit row with no UPDATE, the version is unchanged and no notifier runs. The notifier runs once per branch, post-commit, only on a change. Permissions: Company Admin only; the catalog and reflection tests cover it.
- STORY-005: only the agreed contract change; the C2 bodies in two isolation tests dropped `ar` (needed, because C2 has `{en}`); the assertions are unchanged.
- Frontend: precedence matches ADR-007 A1 (stored value written only when signed in). One load runs per identity key, and an older answer is discarded when the key changed. On failure the UI falls back to English with no switch, and the key is cleared so a later load can retry. The switch is not rendered with one language (also at the call sites). `Accept-Language` is sent only on same-origin `/api/` calls. There is no circular DI (`LanguageState`). No `'en' | 'ar'` type or `SUPPORTED_LANGUAGES` remains (only test fakes use `ar`). The switch keeps `lang`, `aria-pressed`, the 44 px targets and the focus-ring test.

Must fix
- none

Should fix
- S1 [stage 3] backend/Qms.Tests/Setup/CompanyLanguagesApiTests.cs:283 — `Ac20_a_dispenser_of_another_company_does_not_block` would pass even without the tenant filter: no Company 2 dispenser setting lists `ar` (the comment says it has no settings row). → Insert or update a Company 2 dispenser setting with `{en, ar}` by SQL. Assert that C1's `PUT ["en"]` returns 200, and that the AC-20 409 `devices` list never contains the C2 device.
- S2 [stage 2] backend/Qms.Tests/Persistence/LocalisationMigrationTests.cs:507 — the migration says it trims tab, CR and LF (`Localisation.cs:14`, `:26`), but the previous-schema data only has spaces (`"  "`, `" ... "`). → Add a row with tab/CR/LF padding and one that is only `\t\r\n`, expecting `{"ar": ...}` trimmed and `{}`.
- S3 [stage 4] frontend/src/app/core/i18n/language.service.spec.ts — the logic that discards a superseded load (`language.service.ts:92` onwards) has no test. → Add two tests. (a) Sign in while the anonymous `/languages` request is still pending: the anonymous answer must not be applied after the user's answer. (b) Sign out while the user's request is pending: the user's list must not appear after sign-out.
- S4 [stage 3, carry to STORY-012] backend/Qms.Application/Localisation/CompanyLanguages/UpdateCompanyLanguages.cs:70-79 — the in-use check and a dispenser-settings write are not serialised. Once STORY-012 lets dispenser languages be edited, a concurrent "add `ar` to a dispenser" and "disable `ar`" can both commit. No write path exists today, so this is not a defect now. → STORY-012 locks the company row (`SELECT … FOR UPDATE`) in both writes, or re-checks inside the same locked transaction. Record it in STORY-012.

Nice to have
- N1 backend/Qms.Tests/Setup/LanguagesApiTests.cs:192 — the test name says "does not touch the language tables" but it asserts one query. Rename it.
- N2 UpdateCompanyLanguages.cs:108 — `DispensersListingAsync` runs one query per removed code. A single query (`setting.Languages.Any(code => removed.Contains(code))`) would do; it only matters once more languages exist.
- N3 frontend/src/app/core/i18n/language.service.ts:110 — `resolve` reads `this.store.user()` instead of the identity the load was made for. This is harmless today (the effect starts a new load and that result wins). Passing the user of the load identity would make the rule clearer.
- N4 frontend/src/app/core/i18n/translations.spec.ts:20, :55 — the "available" list for the parity check is a fixture, not tied to the registry. Add a comment: when a release makes a language available, its file goes into `FILES` and into the available list.
- N5 AC-31 wording: every STORY-005 API test moved to the ar-enabled database, not only the ones that send or expect `ar`. The phase 1 path is covered by `LocalisedNamesApiTests` (AC-27/AC-28), so this is fine; noted for QA.
- N6 CI time (3 min 30 s for 489 tests): every test class is in the single `PostgresCollection`, so xUnit runs everything one after another. Each test also starts a new `WebApplicationFactory` and clones a database. Steps to consider:
  - (1) Split into 3–4 collections (Persistence, Auth, Setup, Application/unit), each with its own `PostgresFixture` container. Databases are already per test, so this is safe, and xUnit runs the collections in parallel (about 4 containers × 300 MB).
  - (2) Or keep one container and share it through an assembly-level fixture (xUnit v3 `AssemblyFixture`) with several collections. Per-test template clones already isolate the data.
  - (3) Move pure unit tests (LocalizedTextRules, negotiator, catalog) out of the collection so they run in parallel right away.
  - (4) Later: share one `QmsApiFactory` per class for read-only tests.
  - Expected: under 1.5 min. Not blocking.

Notes for next stage (QA):
- Run AC-32 to AC-39 on the phase 1 stack and on the ar-enabled stack. To enable: `UPDATE language SET is_available=true WHERE code='ar'; UPDATE company SET enabled_languages='{en,ar}' WHERE code='C1';`. To revert: `enabled_languages='{en}'`, `is_available=false`.
- STORY-004 AC-3 and AC-17 are re-based by AC-38 (Arabic only when offered).
- `/languages` answers an invalid or expired token as anonymous (200). This was accepted in review.
Open questions: none (the two stage 3 deviations are accepted: `IRequestLanguage.GetCurrentAsync(ct)` is async, and a 200 PUT with an ignored `defaultLanguage` is audited, as ADR-008 Amendment 3 says).
