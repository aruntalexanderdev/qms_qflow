# TASK-013 — Localisation frontend: offered languages from the API, direction, Accept-Language

| Field | Value |
|---|---|
| ID | TASK-013 |
| Story | STORY-051 |
| Stage | 4 Frontend |
| Agent | frontend-developer |
| Status | Done |
| Estimate | M |
| Created | 2026-11-08 |

## 1. Goal
Make the web language handling data-driven: the offered languages, their direction and the default come from `GET /api/v1/languages`; the switch is hidden when only one language is offered (phase 1); the active language is sent as `Accept-Language`. No new screen.

## 2. Acceptance criteria covered
- AC-32 to AC-39 (from STORY-051). STORY-004 AC-1, AC-2, AC-4 to AC-16, AC-18 must keep passing (AC-38).

## 3. Technical details
Source: STORY-051 section 7 "Web (stage 4)", ADR-007 Amendment 1, ADR-010 §7.2, §8. Design system and rules 11 apply to anything styled; no new component style is expected.
- **`LanguageService`** (`frontend/src/app/core/i18n/language.service.ts`): remove `SUPPORTED_LANGUAGES`, `isLanguage` and the `Language = 'en' | 'ar'` type (`auth.models.ts`: `language: string`). Keep `DEFAULT_LANGUAGE = 'en'` only as the ultimate fallback when the list cannot be loaded (AC-39). Hold signals for `offered` (code, nativeName, direction), `defaultLanguage`, `current`, `direction` (from the active language's `direction`). Load the list anonymously at start-up (`init()`), and again after sign-in, session restore, user switch and a session adopted from another tab (the existing `effect` on `SessionStore.user()` and `syncWithUser()`). Precedence per BR-15 / AC-34; a stored `qms.lang` that is not offered is replaced and rewritten. `apply()` sets `<html lang>` and `<html dir>` from `direction`. `translate.addLangs` uses the offered codes; fallback stays `en`. `choose(code)` ignores a code that is not offered; it still sends no server request (the `PUT /auth/me/language` call is the M4 follow-up).
- **API client**: small typed `LanguagesApi` (`GET /api/v1/languages`) with a response model `{ defaultLanguage: string; languages: { code, nativeName, direction: 'ltr' | 'rtl' }[] }`. The anonymous call must work before a token exists; after sign-in it goes through the normal interceptor (token attached). Avoid circular injection between `LanguageService`, `SessionStore` and the interceptor.
- **`LanguageSwitchComponent`** (login page and shell): iterate the offered list, label by `nativeName` (not translated), render nothing when `offered().length < 2`; keep the existing accessibility behaviour (STORY-004 AC-8).
- **Auth interceptor**: add `Accept-Language: <current code>` to same-origin `/api/` requests only (AC-36); same origin rule as the bearer token.
- **Translations**: `public/i18n/ar.json` stays, not offered. `en.json` must be complete. Change the key-parity test so it requires only `en.json` to contain every key used and checks other files only for languages that are `is_available` in a fake registry fixture (AC-37). The test translation loader uses `en.json` only unless a test asks for `ar`.
- **Direction/RTL**: no `=== 'ar'` checks remain anywhere (grep). CSS logical properties stay as they are.
- **Do not add**: the `/auth/me` re-read after a switch (STORY-048), the `PUT /auth/me/language` call (M4), conditional font loading, any new screen or text.

## 4. Dependencies
- Needs: TASK-012 (`GET /api/v1/languages`; `Accept-Language` is accepted by every endpoint).
- Provides to: QA (stage 7): the ar-enabled stack procedure for AC-33, AC-35 (set `language.ar.is_available` true and enable `ar` for Company 1 by test SQL or test host; never in the shipped seed).

## 5. Done checklist
- [x] Code follows common project rules
- [x] Tests added and passing (list below)
- [x] User-facing text localised (translation keys; English required, ADR-010)
- [x] No business logic outside `Qms.Application` (the web only shows the list and direction the API returns)
- [x] Spec / ADR updated if behaviour changed (none; ADR-007 Amendment 1 and ADR-010 followed)
- [x] Handoff written in the story's Stage log

## 6. Tests
`npm run lint` clean; `npm test -- --watch=false`: 13 files, 115 tests green; `npm run build` green (454.96 kB initial raw).
- `language.service.spec.ts` (AC-32 to AC-35, AC-39): phase 1 reads the list once, English ltr, no switch, nothing stored; two languages, choose `ar` sets `lang`, `dir=rtl`, `qms.lang`, shares the active code, no request; fake rtl `xx` gives rtl and ltr gives ltr (from `direction`, not the code); a code that is not offered is ignored; precedence matrix (7 cases: offered `[en]` and `[en, ar]` x stored, user language, a stored `fr` not offered); stored `ar` with offered `[en]` before sign-in shows English; list re-read after sign-in, sign-out and another user; active `ar` replaced and `qms.lang` rewritten when the new list lacks it; restore makes one request per identity; list failure falls back to English with no switch and a later success updates it.
- `auth.interceptor.spec.ts` (AC-36): `Accept-Language` on same-origin `/api/` (also the auth calls), not on another origin or on `/i18n/en.json`, default `en`.
- `translations.spec.ts` (AC-37): `en.json` has every key used; with only `en` available `ar.json` is not checked; with `ar` available the shipped `ar.json` is complete and a missing key fails the check.
- `language-switch.component.spec.ts`, `login.component.spec.ts`, `shell.component.spec.ts` (AC-32, AC-33, AC-38): switch labelled by `nativeName`, no element at all with one language (login and top bar), focus ring not clipped; the STORY-004 specs were adapted only where they used the removed constants (a fake two-language list for the switch tests).
- e2e (Playwright, real API + postgres:18 + `ng serve`): all 14 checks passed. Phase 1: one anonymous `GET /languages` without a token, no switch, `lang=en dir=ltr`; after sign-in a second call with the token, English, no switch, `Accept-Language: en`. Ar-enabled (`UPDATE language SET is_available=true WHERE code='ar'`, `UPDATE company SET enabled_languages='{en,ar}' WHERE code='C1'`): the switch lists English and العربية, Arabic sets `lang=ar dir=rtl`, sidebar on the right, the switch makes no request, later calls send `Accept-Language: ar`, reload stays Arabic; then `{en}` again: stored `ar` is replaced by `en`, `qms.lang` rewritten, no switch. `/languages` answering 500: English, no switch, no error, login works.

## 7. Notes / result
- New: `LanguagesApi` (`GET /api/v1/languages`), `OfferedLanguage` / `LanguagesResponse` models, `LanguageState` (the active code as a plain signal, so the interceptor can read it without a circular injection with `LanguageService` -> HttpClient). `LanguageService` now holds `offered`, `defaultLanguage`, `current`, `direction` (from the list), `canSwitch` (more than one offered).
- The list is read anonymously at start-up (`init()`), then for every identity change (sign-in, restore, sign-out, user switch, session adopted from another tab) by an effect on the user id; one request per identity, and an older answer is dropped when a newer identity started a load.
- Precedence (ADR-007 A1): stored if offered, else `user.language` if offered, else the default; written to `qms.lang` only when signed in. Before sign-in a stored language that is not offered is simply not used (not rewritten).
- Removed: `Language = 'en' | 'ar'`, `SUPPORTED_LANGUAGES`, `isLanguage`, all `=== 'ar'` checks. `DEFAULT_LANGUAGE = 'en'` remains only as the fallback when the list cannot be loaded.
- The switch and its header/landmark are not rendered when one language is offered (login page and top bar).
- `Accept-Language` is added in the auth interceptor to same-origin `/api/` calls only (including login/refresh/logout); the bearer rules are unchanged.
- `ar.json` is kept and complete but not offered; `tsconfig.app.json` now excludes `src/testing/**` (test helpers use `vi`).
- Not added (as the task says): `/auth/me` re-read after a switch (STORY-048), `PUT /auth/me/language`, conditional font loading. The IBM Plex Sans Arabic font is still bundled.
