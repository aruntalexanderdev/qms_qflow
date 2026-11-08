# STORY-051 — Localisation model: language registry, company languages, translated fields

| Field | Value |
|---|---|
| ID | STORY-051 |
| Status | Analysis |
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
To be completed by the BA.

## 4. Business rules
To be completed by the BA (source: ADR-010 §1–10).

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. The language switch on the login page and in the shell lists only the offered languages and is hidden when only one is offered. In phase 1 it is therefore hidden. The `/admin/languages` screen is M4 (STORY-050).

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
| 1 BA | Yes | BA creates | |
| 2 Database | Yes | BA creates | `language` table and seed; company language columns; `*_en` / `*_ar` → `<field>` + `<field>_translations` with data fill; `counter.name_translations`; user language FK; dispenser language data fix; Domain/EF jsonb mapping; dev seeder; catalog; DB tests |
| 3 Backend | Yes | BA creates | `LocalizedTextRules` replacing the STORY-005 temporary mapping; `ICompanyLanguages`, `IRequestLanguage`; `GET /languages`, `GET` / `PUT /company-languages`; `company-languages` permissions; service/priority types on the new storage; tests |
| 4 Frontend | Yes | BA creates | Language list from the API, switch hidden with one language, `dir` by direction, `Accept-Language` header, `ar.json` kept but not offered, component tests |
| 5 Android | N/A | — | No Android code exists yet; M7 (STORY-033 to STORY-036) builds on ADR-010 directly |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] None blocking.
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
