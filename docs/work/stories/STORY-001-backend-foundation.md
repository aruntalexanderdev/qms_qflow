# STORY-001 — Backend solution, API host, logging, OpenAPI, health and Docker Compose

| Field | Value |
|---|---|
| ID | STORY-001 |
| Status | Backend |
| Priority | Must |
| Milestone | M1 |
| Spec sections | 2, 3, 3.1, 10 (conventions), 11, 12 (M1) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-07 |

## 1. User story
As a **developer on the QMS team**,
I want **a running .NET 10 API skeleton with logging, error handling, OpenAPI, health checks and a Docker Compose stack**,
so that **every later story builds on one consistent, runnable foundation**.

## 2. Background
First story of M1 ("walking skeleton", Phase 0 plan). The database schema (STORY-002), sign-in (STORY-003) and the Angular shell (STORY-004) build on it. Patterns: ADR-002 (handlers, `Result<T>`, problem details).

## 3. Scope
**In scope**
- `backend/Qms.slnx` with `Qms.Domain`, `Qms.Application`, `Qms.Infrastructure`, `Qms.Api`, `Qms.Tests`; project references follow clean architecture (Domain ← Application ← Infrastructure ← Api).
- Shared build settings from `backend/Directory.Build.props` (net10.0, nullable, warnings as errors); central package versions in `backend/Directory.Packages.props`.
- Application plumbing from ADR-002: `ICommandHandler`/`IQueryHandler`, `Result<T>`/`Error`, validation decorator, handler registration by scanning.
- API host: controllers under `/api/v1`, problem details for all errors (incl. 500 without stack trace), Serilog (console + rolling file) with a correlation id per request (`X-Correlation-Id` in and out), built-in OpenAPI + Swagger UI in Development, `TimeProvider` registered.
- Health: `GET /health/live` (process) and `GET /health/ready` (dependencies; the database check is added in STORY-002).
- `backend/Qms.Api/Dockerfile`: multi-stage, non-root, works on linux/amd64 and linux/arm64 (Apple Silicon).
- Root `docker-compose.yml`: `api`, `postgres` (PostgreSQL 18, named volume, healthcheck). `web` is added by STORY-004. A `.env.example` for local values.
- Update the `CLAUDE.md` commands and `backend/README.md` if anything differs.

**Out of scope**
- DbContext, entities and migrations (STORY-002). Authentication (STORY-003). Angular (STORY-004). Azure deployment (STORY-039/040).

## 4. Business rules
None (technical foundation). Common rules sections 4, 5 and 6 apply.

## 5. Acceptance criteria

**AC-1: Solution builds and tests run**
- Given a clean clone on macOS (Apple Silicon) or Linux with the .NET 10 SDK
- When `dotnet build backend/Qms.slnx` and `dotnet test backend/Qms.slnx` run
- Then both succeed with zero warnings, and at least one test exists per layer project (an architecture test asserting Domain has no reference to Application, Infrastructure or Api).

**AC-2: Health endpoints**
- Given the API is running
- When `GET /health/live` is called
- Then it returns 200 with status `Healthy`; and `GET /health/ready` returns 200 while all registered checks pass.

**AC-3: Errors are problem details**
- Given the API is running
- When an unknown route is requested, or an endpoint throws an unexpected exception (test-only endpoint in the test host)
- Then the response is `application/problem+json` with `status`, `title`, `traceId` and the correlation id, and no stack trace or exception message leaks.

**AC-4: Validation errors map to 400**
- Given a test handler with a FluentValidation validator (in `Qms.Tests`)
- When it is called with an invalid request through a test endpoint
- Then the response is 400 problem details with an `errors` object per field and an error `code`.

**AC-5: Correlation id and logging**
- Given a request with header `X-Correlation-Id: abc-123`
- When it is processed
- Then the response carries `X-Correlation-Id: abc-123`, and every log line written for the request includes `CorrelationId=abc-123`. A request without the header gets a new id.

**AC-6: OpenAPI**
- Given the API runs in Development
- When `/swagger` is opened
- Then Swagger UI lists the API with version `v1`. In Production, Swagger UI is not served.

**AC-7: Docker Compose**
- Given Docker Desktop on Apple Silicon and a `.env` copied from `.env.example`
- When `docker compose up --build` runs from the repository root
- Then `postgres` becomes healthy, `api` starts as a non-root user, and `GET http://localhost:8080/health/live` returns 200.

**AC-8: CI**
- Given a pull request that changes `backend/**`
- When the CI workflow runs
- Then the backend job restores, builds and tests the solution and passes.

## 6. UI / UX
None.

## 7. Data and API impact
- Endpoints: `GET /health/live`, `GET /health/ready` (not versioned, anonymous).
- Conventions for all later endpoints: `/api/v1/...`, problem details with a `code` extension (ADR-002), `X-Correlation-Id`.
- No tables, no SignalR events.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-002 |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | No schema; DbContext arrives in STORY-002 |
| 3 Backend | Yes | TASK-001 | Solution, host, plumbing, Docker |
| 4 Frontend | N/A | — | Angular shell is STORY-004 |
| 5 Android | N/A | — | No dispenser change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
- Security: no secrets in `appsettings*.json` or `docker-compose.yml` (use `.env`, ignored by Git); container runs as non-root; HTTPS redirection and HSTS outside Development.
- Logs never contain request bodies (future phone numbers).

## 10. Open questions
- [ ] ADR-002 status is Proposed; it is accepted when the Phase 0 PR merges.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-07
Result: Done
Changed: docs/adr/ADR-002-application-layer-pattern.md, this story (sections 7–8)
Notes for next stage: Foundation only; stage 2 N/A (no schema yet). Follow ADR-002 for the handler and error shape.
Open questions: none

### Stage 1 — Analysis — business-analyst — 2026-11-07
Result: Done
Changed: this story (sections 1–6, 9–10), docs/work/tasks/TASK-001-backend-foundation.md
Notes for next stage: Stage 2 is N/A; start at stage 3 with TASK-001. `run-feature` appends the stage 2 N/A handoff.
Open questions: none

### Stage 2 — Database — database-architect — 2026-11-07
Result: N/A — No schema; DbContext arrives in STORY-002 (stage plan)
Changed: none
Notes for next stage: Start stage 3 with TASK-001.
Open questions: none
