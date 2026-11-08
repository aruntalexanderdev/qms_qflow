# Common project rules

These apply to every agent and every change.

## 1. Scope
- Work only on the story, task or bug you were given. No extra features, refactors or "while I'm here" changes; note them as a follow-up in the work item instead.
- `docs/PROJECT_SPEC.md` is the source of truth. Never invent business rules. If something is missing or contradictory, set the work item to **Blocked**, write the question under "Open questions", and stop.
- Phase 2 items (SMS, WhatsApp, PhoneOnly mode, feedback) are not built in phase 1 unless a story says so. Keep the interfaces ready (`IMessageSender`).

## 2. Terminology (use exactly these words in code, UI and docs)
| Use | Never use | Meaning |
|---|---|---|
| Zone | Floor, area, section | Area of a branch where counters sit |
| Service type | Service category, department | What the customer comes for; 1-letter prefix |
| Priority type | Customer type, class | Normal, VIP ...; 1-letter prefix and priority level (1 = highest) |
| Token / token number | Ticket number, queue number | e.g. `VB0001` |
| Token dispenser | Kiosk (in code) | Android device issuing tokens |
| Counter | Desk, window | Where staff serve tokens |
| Zone display / counter display | TV, screen | Display devices |

Token number format is fixed: `[Priority prefix][Service prefix][4-digit sequence]`, e.g. `VB0001`.

## 3. Mapping chain (never bypass)
Company service mapping → Branch service mapping → Counter service mapping (zone + counter + service + priority) → Dispenser zone + service mapping. Each level may only use what the level above allows. Validation lives in `Qms.Application` and is backed by foreign keys.

## 4. Architecture
- Backend clean architecture: Domain (no dependencies) ← Application (use cases, queue engine) ← Infrastructure (EF Core, external services) ← Api (controllers, hubs). No business logic in controllers, hubs, Angular components or the Android app.
- The Android app and displays hold no business rules; they show what the API returns.
- All live updates go through the SignalR hub `/hubs/qms` with groups `branch:{id}`, `zone:{id}`, `counter:{id}`, `device:{id}`.
- Schema changes are made only by the database-architect stage (agent 2). Other agents request them in the work item.

## 5. Coding standards
- C#: .NET 10, nullable enabled, async all the way, `CancellationToken` on every async API, FluentValidation for every request, problem details (RFC 7807) for errors, no `DateTime.Now` (use an injected `TimeProvider`, store UTC).
- Database: PostgreSQL, snake_case names, uuid keys, `created_at`/`updated_at`, `company_id` on tenant tables, translated text as `<field>` (default language, English, required) + `<field>_translations` (jsonb, language code → text) (ADR-010). Never add `_en` / `_ar` column pairs.
- Angular: Angular 22 (latest stable; upgrade with `ng update` on each major), standalone components, signals, typed reactive forms, ngx-translate keys (no hard-coded UI text), RTL-safe CSS (logical properties: `margin-inline-start`, not `margin-left`).
- Kotlin: Jetpack Compose, MVVM, coroutines + Flow, Retrofit/OkHttp, no blocking calls on the main thread, strings in `strings.xml` (English required; other languages per rules §7).
- Names: English, descriptive, no abbreviations except agreed ones (Qms, Api, Id, Dto).

## 6. Security and privacy
- No secrets, keys or passwords in code or Git; use environment variables / user secrets.
- Every endpoint checks role and branch scope; device endpoints check the device key.
- Phone numbers are personal data: mask in UI (`+971 50 *** **67`) except at the serving counter; never log them.
- Customer data stays in the deployment's own database; no calls to external CDNs or services from displays or dispensers.

## 7. Localisation
English is the default language; other languages are configurable (ADR-010). Every user-facing text goes through translation keys or translated fields, never hard-coded:
- Web: ngx-translate keys; every key in `en.json` (required); other `<code>.json` files are optional and fall back to English. Android: `values/strings.xml` (English, required); `values-<code>/` when a language ships. Tickets, displays and voice clips: per language, English required.
- Master data: required English value + optional translations (`<field>_translations`), keys only from the company's enabled languages, English fallback.
- Offer only enabled languages; hide the language switch when only one is enabled; layout direction (RTL) comes from the language's direction.
- Phase 1 ships English only. Do not add Arabic (or other) texts as a requirement; the existing `ar.json` is kept but not offered.

## 8. Testing
- Every rule in spec section 6 has a unit test in `Qms.Tests`.
- Every endpoint has at least one integration test (Testcontainers PostgreSQL).
- Angular: component tests for every screen's main action. Android: ViewModel unit tests.
- A change is not done while any test fails or is skipped.

## 9. Git
- Branch per work item: `feature/STORY-012-short-name`, `task/TASK-034-short-name`, `fix/BUG-007-short-name`.
- Commit message: `STORY-012: <what changed>` (or TASK-/BUG-), present tense, one logical change per commit.
- Never commit to `main` directly; never force-push shared branches.

## 10. Definition of done (story)
- All acceptance criteria met and checked by QA.
- Code reviewed with no open "Must fix" items.
- Tests added and passing; build green for backend, web and Android (whichever were touched).
- Spec or ADR updated if behaviour changed.
- Work item status = Done, with every stage's handoff filled in.

## 11. UI design and theming
- The design system in `docs/design/DESIGN_SYSTEM.md` and `docs/design/tokens.json` is the only source for colours, fonts, font sizes, spacing, radii, shadows and motion. Look up a token there before styling anything.
- Web: style only with `var(--qms-*)` variables from `frontend/src/styles/_tokens.scss` and the Angular Material theme. No hex, `rgb()`, `hsl()` or named colours, and no raw `px` font sizes, in component styles or templates (Stylelint enforces this).
- Android: colours, type and shapes come only from `ui/theme/` (`MaterialTheme.colorScheme`, `QmsColors`). No `Color(0x…)` outside `ui/theme/`.
- Token state, priority and counter status use the badge colours from the design system, always with a text label. VIP also shows a ★.
- Fonts (Instrument Sans, IBM Plex Sans Arabic) are bundled with the app. No Google Fonts or other CDN links.
- WCAG 2.1 AA: text contrast at least 4.5:1, UI borders and large text at least 3:1, a visible focus ring, never colour alone.
- A new colour, font or component style changes `docs/design/` first (with the user's approval), then the theme files, then the screens.
