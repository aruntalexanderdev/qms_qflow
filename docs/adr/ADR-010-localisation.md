# ADR-010 — Localisation: English default, configurable languages, translated master data

Status: Accepted (product owner decision, 2026-11-08)
Date: 2026-11-08
Story: STORY-051 (localisation model); affects STORY-005 to STORY-013, STORY-047 to STORY-049 and every later story with user-facing text

## Context
The spec had English and Arabic fixed everywhere: `name_en` / `name_ar` columns on every master table, `header_en` / `header_ar`, `fee_line_en` / `fee_line_ar` and `consent_text_en` / `consent_text_ar` on templates and dispensers, an English/Arabic switch in the web app, Arabic and English voice clips and tickets, and a rule that every user-facing text exists in both languages (common rules §7).

On 2026-11-08 the product owner decided:
- **English is the default language of the whole application.** Languages are **configurable**, not a fixed English + Arabic pair.
- Master data text is stored as **one required default-language value plus optional translations** (language code → text). A missing translation falls back to English. The `*_en` / `*_ar` pairs are replaced before real data exists.
- The **Company Admin enables languages** for the company. UI text translations ship as **translation files** maintained by the team; there is no in-app translation editor in phase 1.
- **Phase 1 ships English only.** The mechanism (registry, enabled languages, translated data, RTL by direction, per-user and per-device choice, voice clips per language) is built, but only English is enabled and shipped. Arabic and other languages are added later by shipping their files and enabling them.

At this date only STORY-001 to STORY-004 are merged. STORY-005 (service and priority types) is in stage 3. It builds its API contract as `name` (required) plus `translations` (optional object keyed by BCP-47 code), mapped for now to `name_en` / `name_ar`, and it rejects keys other than `ar`. No production data exists.

## Decision

### 1. Default language
- The default language is **`en`, fixed in phase 1.** `company.default_language` exists, with value `en`, but no API changes it. Making it selectable later needs no schema change; it does need an ADR, because changing a company's default would also mean swapping the base values of its translated fields.
- The default-language value of every translated field is **required**. Every other language is **optional**.

### 2. Language registry (deployment-wide)
New global table `language`. It is **not a tenant table**, because the languages a release ships are the same for every company.

| Column | Meaning |
|---|---|
| `id` uuid, `created_at`, `updated_at` | Standard columns (rules §5) |
| `code` | BCP-47 tag as used in files and APIs, e.g. `en`, `ar`. Unique; other tables reference it by foreign key |
| `native_name` | Endonym shown in language switches, e.g. "English", "العربية". This label is never translated |
| `direction` | `ltr` / `rtl` |
| `is_available` | True when this release ships the language: complete web and Android translation files, voice clips and font coverage. Only available languages can be enabled |
| `sort_order` | Order in switches |

- Seed (STORY-051 migration): `en` ("English", ltr, available, 1) and `ar` ("العربية", rtl, **not available**, 2).
- `is_available` changes only in a release, through a migration or seed change shipped with the translation files. There is no API for it in phase 1. A later Super Admin "global settings" story may add one.

### 3. Company language settings
- `company.default_language` (FK → `language.code`, default `'en'`) and `company.enabled_languages` (`text[]`, not null, default `'{en}'`), with the check `default_language = ANY(enabled_languages)`.
- The Application validates that every enabled language exists and is available. Otherwise it returns 400 `company_language.not_available`. The array has no element foreign key; this is accepted, the same as `dispenser_setting.languages`.
- **Who:** the Company Admin, through `GET` / `PUT /api/v1/company-languages` (singleton, ADR-008 §2). Permissions `company-languages.view` / `.edit`; the default grant is Company Admin.
  - The body is `{ enabledLanguages: string[], version }`.
  - The default language is always included. Removing it returns 400 `company_language.default_required`.
  - Disabling a language that a dispenser of the company still lists returns 409 `company_language.in_use`, with the dispensers listed.
  - The change is audited (`company_languages`, `update`) and calls `IDeviceConfigNotifier` for every branch.
- A new company (STORY-047) starts with `default_language = en` and `enabled_languages = {en}`.
- **Disabling a language** keeps all stored translations, so turning it back on restores them. Users and devices that chose the language fall back at read time (§6).

### 4. Translated fields: data model
**Pattern:** `<field>` (required, default-language text, same type and length as before) plus `<field>_translations` (`jsonb`, not null, default `'{}'`, check `jsonb_typeof(<field>_translations) = 'object'`). The JSON is a flat object from language code to text, for example `{"ar": "البطاقات"}`. It never contains the default language.

| Table | Translated fields (replace) | Built by |
|---|---|---|
| `company` | `name` (was `name_en` / `name_ar`) | STORY-051 migration |
| `service_type` | `name` | STORY-051 |
| `priority_type` | `name` | STORY-051 |
| `branch` | `name` | STORY-051 |
| `zone` | `name` | STORY-051 |
| `counter` | `name` gains `name_translations` (the product owner listed counter names; counter names can appear on consoles and displays) | STORY-051 |
| `ticket_template` | `header`, `footer` (were `header_en` / `_ar`, `footer_en` / `_ar`) | STORY-051 |
| `ticket_template` | `fee_line` + `fee_line_translations` (instead of `fee_line_en` / `_ar`) | STORY-013 |
| `dispenser_setting` | `consent_text` + `consent_text_translations` (instead of `consent_text_en` / `_ar`); `consent_text` is nullable, and translations are allowed only when it is set | STORY-012 |
| `role` | `name` + `name_translations` (instead of `name_en` / `name_ar`) | STORY-048 |

- **Not translated** (admin labels or personal data, one column): `device.name`, `ticket_template.name`, user `display_name`, codes, prefixes, URLs.
- **Uniqueness** applies to the default value only, for example `role (company_id, lower(name))`. The former unique `(company_id, name_ar)` on `role` is dropped. Translations are not unique.
- **EF Core:** the Domain property is `IReadOnlyDictionary<string, string>` (or a small `LocalizedText` value object, stage 2 decides), mapped to `jsonb` with a value converter and comparer. No query filters on JSON content in phase 1.

### 5. Validation (one shared Application helper)
STORY-051 adds a shared helper in `Qms.Application`, `LocalizedTextRules` (FluentValidation extensions plus a merge function), and every handler with translated fields uses it. It replaces STORY-005's temporary `ar` mapping and the `BilingualName()` rule.
- **Default value:** required, trimmed, with the field's length limit. Names are 1–200 characters; header, footer and consent text 1–500; fee line 1–200. Script is not checked.
- **Keys:** each key must be a language that is **enabled for the caller's company**. Keys are matched case-insensitively and stored in the registry's form (`AR` → `ar`).
  - A key that is not enabled gives `translation.language_not_enabled`.
  - The default language as a key gives `translation.default_language_key`.
- **Values:** trimmed. A `null`, empty or whitespace value means "no translation": the key is dropped, not stored, and not an error. An admin form can therefore send its empty optional fields as they are. A non-empty value has the same length limit and content rules as the default value; for example, a fee-line translation must contain `{amount}` exactly once.
- Failures return 400 with `errors` keyed `translations.<code>` (or `<field>Translations.<code>`). The top-level code follows ADR-008 §6: the specific code when every failure shares it, else `validation.failed`.
- **Write semantics:** `PUT` replaces the translations of **enabled** languages. Stored translations of languages that are not enabled are **kept unchanged** and are not returned (§7), so disabling and re-enabling a language loses nothing.

### 6. Fallback rules
For a requested language L, the API resolves a text as `translations[L]` when L is enabled for the company and the key exists. Otherwise it uses the default value.
- Every field falls back on its own. There is no machine translation.
- A requested language that is not enabled is treated as the default language.
- A translated field that is not filled in falls back to English. The fallback is not an error.

### 7. How APIs expose translated text
1. **Admin (setup) APIs** return and accept the raw form: `name` plus `translations` for the field `name`, and `<field>` plus `<field>Translations` for any other field (`header` / `headerTranslations`, `footer` / `footerTranslations`, `feeLine` / `feeLineTranslations`, `consentText` / `consentTextTranslations`).
   - Only keys of enabled non-default languages are returned.
   - Lists sort by the default value (`name`), not `nameEn`.
   - Audit `data` holds the command as sent.
2. **Resolved APIs** return plain resolved text, never the translation maps:
   - **Web user APIs** that show master data, such as `/auth/me` role names and the M5 / M8 console, dashboard and reports. They resolve with the request's `Accept-Language`, matched against the company's enabled languages, else the default. The web app sends its active UI language on every API call.
   - **Ticket preview** `GET /ticket-templates/{id}/preview?language=<code>`. The language must be enabled; otherwise 400 `language.not_enabled`. Without the parameter, the default is used.
   - **Device APIs** (`/device/config`, display state). A customer switches language on the screen with no round trip, so each translated text is returned as an object keyed by **each of the device's languages**, already resolved with fallback (every key present), for example `"name": { "en": "Cards" }`.
   - **`POST /tokens`** takes `language` (one of the dispenser's languages, default the dispenser's default language; otherwise 400 `language.not_enabled`). Its print data is resolved in that language.
3. Error, permission and enum labels are never sent as text. The API sends codes, and clients translate them from their translation files (unchanged).

### 8. Web UI translations
- Translation files are `frontend/public/i18n/<code>.json`, maintained by the team. `en.json` is the source and must contain every key. Other files are optional. A missing key falls back to `en` (ngx-translate fallback language).
- **Arabic:** `ar.json` from STORY-004 is **kept**, but `ar` is not available, so it is never offered. It is not required to receive new keys. Before `ar` (or any language) is made available, a translation pass completes its file, and a key-parity test runs for every available language.
- **Offered languages** come from `GET /api/v1/languages` (STORY-051). The response is `{ defaultLanguage, languages: [{ code, nativeName, direction }] }`, and the set depends on the caller:
  - anonymous (login page) and Super Admin: the available languages of the deployment;
  - a signed-in company user: the company's enabled languages.
- The language switch (login page and shell) lists only the offered languages and is **hidden when only one is offered**. That is the phase 1 state, with English only.
- **RTL:** `<html dir>` comes from the language's `direction`, not from a hard-coded `ar` check. CSS logical properties stay mandatory (rules §5), so an rtl language works when it is enabled.
- The hard-coded `SUPPORTED_LANGUAGES = ['en', 'ar']` and the `Language = 'en' | 'ar'` type are replaced by the API list and a `string` code.

### 9. User language preference
- `asp_net_users.language` (exists) becomes an FK → `language.code`. It replaces the check `ck_asp_net_users_language IN ('en','ar')`.
- `PUT /api/v1/auth/me/language` (STORY-011) accepts only a language offered to the caller (§8 rules). Otherwise it returns 400 `language.not_enabled`. User create (STORY-011, STORY-047 first admin) uses the same rule, and the default is the company default.
- A stored language that is later disabled stays stored and resolves to the default at read time.
- The JWT `lang` claim (ADR-004) carries `user.language` unchanged.
- Precedence in the browser: ADR-007 B, as amended by this ADR.

### 10. Device language settings
- `dispenser_setting.languages` (`text[]`) must be a non-empty subset of the company's enabled languages, and `default_language` must be one of them (400 `language.not_enabled`). This replaces the hard-coded `en`, `ar` list in STORY-012.
- A new dispenser gets `languages = {<company default>}` and `default_language = <company default>`. The entity default `["en", "ar"]` and the seed values become `["en"]` (STORY-051), replacing ADR-008 §14 "languages en + ar".
- The dispenser's language toggle is shown only when it has two or more languages, and it uses `native_name` labels from `/device/config`. Layout direction comes from each language's `direction`.
- **Android:** `values/strings.xml` (English) is required. `values-<code>/` folders are added when a language becomes available. The existing `values-ar` requirement of the rules is dropped for phase 1.
- **Displays** (M6) show the company's enabled languages, so English only in phase 1. Whether a display alternates languages or shows them side by side, and whether a per-display language setting is needed, is decided in M6 (STORY-030 / 031) within this ADR.

### 11. Voice clips and tickets
- **Voice clips** are stored per language (`voice/<code>/…`). A display announces in its languages, default language first. A language is played only when its clip set ships. Phase 1 ships English clips only.
- **Tickets** print in **one language**: the language the customer selected on the dispenser, or at `/counter/issue` the staff member's choice (default: the company default).
  - Template texts (header, footer, fee line) are resolved by §6.
  - Fixed labels (service, zone, people ahead, date) come from per-language resources in `Qms.Application` (`TicketContentBuilder`). The English resource is required; other languages are added with their release.
  - A ticket printed in two languages at once is not part of phase 1 (open point in §13 of the spec, to decide when a second language is enabled).

### 12. Fonts and design system
- Instrument Sans (Latin) and IBM Plex Sans Arabic both stay **bundled** with the apps (no CDN). The web font files are declared with `unicode-range`, so a browser downloads the Arabic files only when Arabic-script text is rendered. No conditional loading code is needed, and no Arabic font traffic occurs while only English is enabled.
- The rule "IBM Plex Sans Arabic first in the font stack" now applies to rtl / Arabic-script languages, not to "Arabic selected".
- A language in a script the bundled fonts do not cover needs a design-system change first (rules §11), before it can be made available.

### 13. Phase 2 messaging
SMS / WhatsApp templates are stored per enabled language using the same pattern. A message is sent in the language chosen on the dispenser when the token was issued, else in the company default.

### 14. Where it lands
- **STORY-051**, in M2 right after STORY-005 and before STORY-048, builds:
  - the `language` table and the company settings;
  - the migration of every existing `*_en` / `*_ar` column (`_en` → default value, `_ar` → `translations.ar` only when it is non-empty and differs from the English text; STORY-005 stores an empty `name_ar` when no Arabic is given);
  - `counter.name_translations`;
  - the user language FK;
  - the dispenser language defaults;
  - the shared helper;
  - `GET /languages` and `GET` / `PUT /company-languages`;
  - the frontend switch driven by the API.
- Every later M2 story (048, 047, 049, 006–013) is built directly on the pattern, so STORY-048 creates `role.name` + `name_translations`, STORY-012 creates `consent_text` + `consent_text_translations`, and STORY-013 creates `fee_line` + `fee_line_translations`. No second migration is needed.

## Consequences
- Adding a language later is a release (translation files, Android strings, voice clips, `is_available = true`) plus a Company Admin enabling it. No schema or code change is needed.
- Admin screens (M4) show one required English field per translated text, plus one optional field per enabled extra language. In phase 1 that means only the English field.
- Phase 1 testers see English only. The mechanism is still tested: integration tests make `ar` available and enabled for a test company, and assert key validation, fallback, preservation of disabled-language keys, and the resolved device maps.
- JSON translations cannot be indexed or constrained per language in the database. That is accepted: there is no search by translated name in phase 1, and validation lives in the Application (rules §3 spirit: validation in `Qms.Application`).
- STORY-005 ships its contract (`name` + `translations`) with a temporary `name_ar` mapping. STORY-051 swaps the storage without changing the contract.
- The seed's Arabic names are kept as `translations.ar`. They are invisible until `ar` is available and enabled.

## Spec impact
Approved by the product owner (2026-11-08). Spec §2 (Web i18n, Display voice), §7 (dispenser languages, consent text, ticket), §8 (login language choice, admin screens), §9 (all `*_en` / `*_ar` columns, `language` table, company language settings, user language), §10 (`/languages`, `/company-languages`, device and token language), §11 (Languages), §12 (M2 STORY-051, phase 2 templates), §13 (Languages row resolved). Common rules §2, §5 and §7. ADR-001 (fonts note), ADR-007 B, ADR-008 §4, §8, §13 and §14, and ADR-009 §2 and §3 are amended to match.
