# TASK-013 — Localisation frontend: offered languages from the API, direction, Accept-Language

| Field | Value |
|---|---|
| ID | TASK-013 |
| Story | STORY-051 |
| Stage | 4 Frontend |
| Agent | frontend-developer |
| Status | To do |
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
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] User-facing text localised (translation keys / translated fields; English required, ADR-010)
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
Component and service tests (Vitest/Jasmine as the project uses); `npm run lint`, `npm test -- --watch=false` and `npm run build` green.
- AC-32: list with only `en` → no switch element; one anonymous `GET /languages`; `<html lang="en" dir="ltr">`.
- AC-33: fake list with `ar` rtl → switch lists "English" and "العربية"; choosing `ar` sets `lang`, `dir="rtl"`, `qms.lang`, no request from the switch; fake rtl `xx` gives rtl; ltr gives ltr.
- AC-34: precedence matrix for offered `[en]` and `[en, ar]` with stored/user combinations, including a stored language that is not offered, and a pre-sign-in stored `ar` with offered `[en]`.
- AC-35: list re-read after sign-in, restore, user switch, adopted session; Company 1 vs Company 2 vs Super Admin lists; active `ar` replaced when the new list lacks it.
- AC-36: `Accept-Language` only on same-origin `/api/`.
- AC-37: parity check with only `en` available, and with a fake available `ar` missing a key (fails).
- AC-38: existing STORY-004 specs adapted only where they used the removed constants; AC-3 and AC-17 specs run with a fake two-language list.
- AC-39: list request error → English, no switch, login works, list updated after sign-in.
- Optional e2e on the ar-enabled stack for AC-33 and AC-35 (QA repeats manually).

## 7. Notes / result
<Filled by the agent when done.>
