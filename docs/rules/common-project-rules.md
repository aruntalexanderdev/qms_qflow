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
- Database: PostgreSQL, snake_case names, uuid keys, `created_at`/`updated_at`, `company_id` on tenant tables, `name_en` + `name_ar` for bilingual text.
- Angular: standalone components, signals, typed reactive forms, ngx-translate keys (no hard-coded UI text), RTL-safe CSS (logical properties: `margin-inline-start`, not `margin-left`).
- Kotlin: Jetpack Compose, MVVM, coroutines + Flow, Retrofit/OkHttp, no blocking calls on the main thread, strings in `strings.xml` (en + ar).
- Names: English, descriptive, no abbreviations except agreed ones (Qms, Api, Id, Dto).

## 6. Security and privacy
- No secrets, keys or passwords in code or Git; use environment variables / user secrets.
- Every endpoint checks role and branch scope; device endpoints check the device key.
- Phone numbers are personal data: mask in UI (`+971 50 *** **67`) except at the serving counter; never log them.
- Customer data stays in the deployment's own database; no calls to external CDNs or services from displays or dispensers.

## 7. Bilingual
Every user-facing text exists in English and Arabic: web (ngx-translate), Android (`values/` + `values-ar/`), tickets, displays, voice clips, database names (`name_en`, `name_ar`).

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
