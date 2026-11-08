# Bank QMS — Claude Code project guide

Bank queue management system: .NET 10 (LTS) + PostgreSQL backend, Angular 22 web app (admin, counter console, supervisor dashboard, zone/counter displays), Kotlin Android token dispenser on Sunmi POS devices. Phase 1 = printed tokens; phase 2 = SMS / WhatsApp.

## Source of truth
- `docs/PROJECT_SPEC.md` — the full project specification (export of "Bank QMS – Project Specification" as Markdown). If code and spec disagree, the spec wins; if the spec is wrong, stop and raise it (see workflow rules).
- `docs/design/` — design system: `DESIGN_SYSTEM.md` (how) and `tokens.json` (values) for colours, fonts and spacing. Every UI change follows it (rules section 11).
- `docs/adr/` — architecture decisions taken after the spec.
- `docs/work/` — stories, tasks and bugs. Every change belongs to one of them. `docs/work/BACKLOG.md` lists every phase, milestone and story.

## Rules (always loaded)
@docs/rules/common-project-rules.md
@docs/rules/workflow-rules.md

## Team (agents, run in this order)
| # | Agent | Owns |
|---|---|---|
| 0 | `project-architect` | Impact, design, ADRs, stage plan for each story |
| 1 | `business-analyst` | Story, acceptance criteria, task breakdown |
| 2 | `database-architect` | Schema, EF Core entities and migrations, seed data |
| 3 | `backend-developer` | .NET API, queue engine, SignalR, backend tests |
| 4 | `frontend-developer` | Angular screens, i18n/RTL, SignalR client |
| 5 | `android-developer` | Kotlin dispenser app, Sunmi printing, kiosk mode |
| 6 | `code-reviewer` | Review against spec, rules and security |
| 7 | `qa-tester` | Test cases, test run, bug reports, sign-off |

Run a full story with the `run-feature` skill: `/run-feature STORY-001`.

## Repository layout
```
backend/   Qms.Domain, Qms.Application, Qms.Infrastructure, Qms.Api, Qms.Tests
frontend/  Angular web app
android/   Kotlin dispenser app
docs/      PROJECT_SPEC.md, design/, rules/, templates/, work/, adr/
docker-compose.yml
```

## Commands
Fill these in during milestone M1 and keep them correct.
- Backend build / test: `dotnet build backend/Qms.slnx` · `dotnet test backend/Qms.slnx`
- Migrations (from the repository root): `dotnet tool restore` (once), then `dotnet ef migrations add <Name> -p backend/Qms.Infrastructure -s backend/Qms.Api`
- Web: `cd frontend && npm ci && npm run build && npm test -- --watch=false`
- Android: `cd android && ./gradlew assembleDebug testDebugUnitTest`
- Full stack: `cp .env.example .env` (once; set `JWT_SIGNING_KEY`, optionally the `SEED_*` admin users), then `docker compose up --build` (API at http://localhost:8080/health/live)
- Local API (Development): needs `ConnectionStrings:Qms` and `Jwt:SigningKey` (user-secrets or env, see `backend/README.md`, "Authentication settings")
