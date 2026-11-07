# STORY-001 — Backend solution, API host, logging, OpenAPI, health and Docker Compose

| Field | Value |
|---|---|
| ID | STORY-001 |
| Status | Done |
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

### Stage 3 — Backend — backend-developer — 2026-11-07
Result: Done
Changed: backend/Qms.slnx, backend/Directory.Packages.props, backend/.dockerignore, backend/README.md, backend/Qms.Domain/**, backend/Qms.Application/** (Common: Result, Error, handlers, validation decorators, AddApplication), backend/Qms.Infrastructure/** (AddInfrastructure, TimeProvider), backend/Qms.Api/** (Program, correlation id middleware, problem details, ResultExtensions, OpenAPI, health, appsettings, Dockerfile), backend/Qms.Tests/** (35 tests), docker-compose.yml, .env.example, CLAUDE.md (Full stack command), docs/work/tasks/TASK-001-backend-foundation.md
Notes for next stage: Stages 4 and 5 are N/A for this story. Conventions for later stories: controllers derive from `QmsControllerBase` (`/api/v1/[controller]`), call `ICommandHandler`/`IQueryHandler` and return `result.ToActionResult(this)`; add handlers + validators under `Qms.Application/<Feature>/<UseCase>/` and they are picked up by `AddApplication()`; dependency health checks are added with tag `ready` (database check in STORY-002). Problem details carry `traceId`, `correlationId` and `code`. Tests: xUnit v2 + Shouldly; `QmsApiFactory` starts the host with test-only controllers. Swagger UI at `/swagger` and `/openapi/v1.json` only in Development (set `ASPNETCORE_ENVIRONMENT=Development` in `.env`). Outside Development the host logs a one-time warning "Failed to determine the https port" until an HTTPS port or forwarded headers are configured at deployment (STORY-039/040).
Open questions: none

### Stage 4 — Frontend — frontend-developer — 2026-11-07
Result: N/A — Angular shell is STORY-004 (stage plan)
Changed: none
Notes for next stage: none
Open questions: none

### Stage 5 — Android — android-developer — 2026-11-07
Result: N/A — No dispenser change (stage plan)
Changed: none
Notes for next stage: Ready for code review (stage 6).
Open questions: none

### Stage 6 — Code review — code-reviewer — 2026-11-07
Result: Approved
Builds/tests: backend ✅ (`dotnet build backend/Qms.slnx -c Release`: 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release`: 35 passed, 0 failed, 0 skipped) · web N/A · android N/A
ACs checked: AC-1 ✅, AC-2 ✅, AC-3 ✅, AC-4 ✅, AC-5 ✅, AC-6 ✅, AC-7 ✅ (checked by reading the Dockerfile and compose file and running `docker compose config` with `.env.example`; the image cannot be built in the review sandbox because of the NuGet proxy CA, and stage 3 reports a successful run), AC-8 ✅ (checked by reading the workflow: `global-json-file: backend/global.json`, and restore/build/test of `backend/Qms.slnx` from the repo root with `--no-build -c Release` match the layout)
Also checked: in a manual Production run, `/health/live` returned 200, the 404 problem details echoed `X-Correlation-Id: abc-123`, `/swagger` returned 404, and the log lines carried `CorrelationId`. The 500 response does not leak exception details (the test runs in Development, which is the stricter case). Correlation id input is validated (64 chars max, `[A-Za-z0-9-_.]`). Request logging records the method, path and status only, with no bodies or query strings. The Dockerfile is multi-stage, runs as non-root (`$APP_UID`), contains no secrets and maps `amd64`→`x64` correctly (`arm64` passes through). Compose has no secrets (`:?` guards, `.env` is git-ignored) and a `pg_isready` healthcheck. DI scanning works when one handler implements several interfaces (same scoped instance). NetArchTest's open-generic `ImplementInterface` match was confirmed to work, so the "no handlers in Api" test is meaningful.
Stage 3 deviations: all accepted. xUnit v2.9.3: the reason is documented, and v3 can be reconsidered if `global.json` moves to the repo root. Shouldly: the licence reason matches the ADR-002 reasoning for dropping MediatR. No Testcontainers yet: there is no database until STORY-002, which must add it together with the readiness DB check. ADR-002's `[Audited]` decorator is deferred until `audit_log` exists (STORY-002 or later).

Must fix
- none

Should fix
- S1 [stage 3] docker-compose.yml:36 + .env.example:10 — `api` publishes on all interfaces (`${API_PORT:-8080}:8080`), and `.env.example` sets `ASPNETCORE_ENVIRONMENT=Development` by default. A fresh local stack therefore exposes Swagger UI and `/openapi/v1.json` to the LAN. Postgres, by contrast, is bound to `127.0.0.1`. → Bind to `127.0.0.1` by default and document how to override it for dispenser-device testing, or make `Production` the default in `.env.example`.
- S2 [stage 3] backend/Qms.Application/Common/ValidationDecorator.cs:65 (+ Qms.Api/Common/ResultExtensions.cs:34) — `errors` keys are C# property names (`Name`, `Quantity`) but request JSON is camelCase (`name`, `quantity`). `[ApiController]` model-binding errors use yet another key style (`$.name`). Every later endpoint inherits this convention, and the web and Android apps need to map errors to fields. → Use camelCase keys (for example a FluentValidation `PropertyNameResolver`/`DisplayNameResolver`, or camel-case the keys in `ToProblem`) and assert them in `ProblemDetailsTests`.
- S3 [stage 3] backend/Qms.Tests/Support/QmsApiFactory.cs:25 — test hosts inherit the `appsettings.json` File sink. Each `WebApplicationFactory` host opens its own non-shared sink on the same `logs/qms-<date>.log` under the test output. Interleaved, corrupted lines were seen in `Qms.Tests/bin/Release/net10.0/logs`. → Override `Serilog:WriteTo` in the test host (in-memory sink only, or console).

Nice to have
- N1 backend/Qms.Application/Common/DependencyInjection.cs:25-52 — open-generic handlers are skipped silently. Two handlers for the same interface resolve last-wins with no warning. `AddScoped(implementation)` runs once per interface; `TryAddScoped` would do. → Fail fast on duplicate handler interfaces and add a test for a handler that implements two interfaces.
- N2 backend/Qms.Api/Common/ProblemDetailsSetup.cs:52 — client-aborted requests (`OperationCanceledException` with `RequestAborted` cancelled) are logged at Error and answered with 500. → Handle them separately (lower log level, status 499).
- N3 backend/Qms.Api/Program.cs:27-28 — an unknown route requested with `Accept: text/html` gets `text/plain` "Status Code: 404; Not Found" instead of problem details, because the default writer honours `Accept`. This is acceptable for an API; document it or always write JSON under `/api`.
- N4 backend/Qms.Api/Program.cs:30-34 — outside Development, `UseHttpsRedirection` will redirect HTTP health probes once an HTTPS port is set, and it needs forwarded headers behind a TLS-terminating ingress. Track this in STORY-039/040 (the stage 3 handoff already notes it).
- N5 backend/global.json — the SDK pin only applies when commands run from `backend/`. The `CLAUDE.md` command and CI run from the repo root; CI is unaffected because `setup-dotnet` reads the file. Consider moving the file to the repo root, which would also allow xUnit v3 with Microsoft.Testing.Platform.
- N6 backend/Qms.Tests/Api/CorrelationIdTests.cs:18 — the test asserts `CorrelationId` only on the "HTTP ..." request-completion lines and the exception line. A broader assertion (all events carrying `RequestPath`) would match AC-5's "every log line" more literally.
- N7 backend/.dockerignore — add `**/secrets.json` and `**/appsettings.*.local.json` as a defensive measure.
Notes for next stage: QA can run AC-1 through AC-6 with `dotnet test` and a local `dotnet run`. AC-7 needs a Docker host with normal registry and NuGet access. AC-8 needs a PR run of `.github/workflows/ci.yml`. Should fix and Nice to have items become follow-up tasks if they are not done.
Open questions: none

### Stage 7 — QA test cases and results — qa-tester — 2026-11-07
Build under test: working tree on top of stage 6 approval; .NET SDK 10.0.114, Linux amd64, Docker 29.9.2.

```
TC-001-1 | AC-1 | Release build has zero warnings
Steps: dotnet build backend/Qms.slnx -c Release
Expected: success, 0 warnings, 0 errors
Result: Pass (0 Warning(s), 0 Error(s))

TC-001-2 | AC-1 | Tests run, at least one per layer, architecture test present
Steps: dotnet test backend/Qms.slnx -c Release
Expected: all pass, none skipped; tests for Domain/Application/Infrastructure/Api + ArchitectureTests
Result: Pass (35 passed, 0 failed, 0 skipped). Folders: Application (Result, ValidationDecorator), Infrastructure (registration), Api (health, problem details, correlation id, hosting), Architecture. Domain is covered by the architecture tests (DomainAssembly marker; the layer has no logic yet).
Note: ArchitectureTests assert (a) NetArchTest "Domain has no dependency on Application/Infrastructure/Api", Application not on Infrastructure/Api, Infrastructure not on Api, and (b) a csproj ProjectReference whitelist per project. A forbidden reference would fail (a) or (b). A throwaway reference was not tried: every upward reference between the layers is circular and MSBuild would reject it before the tests run, so the guard is verified by reading the code only.

TC-001-3 | AC-2 | Live and ready, Development and Production
Steps: run Qms.Api (ASPNETCORE_ENVIRONMENT=Development, then Production); GET /health/live, /health/ready
Expected: 200, body Healthy
Result: Pass (both environments, both endpoints: 200 text/plain "Healthy", no-cache headers)

TC-001-4 | AC-3 | Unknown route is problem details
Steps: GET /nope (with and without X-Correlation-Id), both environments
Expected: 404 application/problem+json with status, title, traceId, correlationId, code, no stack
Result: Pass ({"type":...,"title":"Not Found","status":404,"traceId":"...","correlationId":"abc-123","code":"resource.not_found"})

TC-001-5 | AC-3 | Unhandled exception is a 500 problem without leaks
Steps: ProblemDetailsTests (test-host endpoint throws InvalidOperationException with "secret internal detail")
Expected: 500 problem+json, code server.unexpected_error; body lacks message, exception type, " at Qms."
Result: Pass (test exists, passes; assertions check both the leak and the shape; host runs in Development, the stricter case)

TC-001-6 | AC-4 | Validation errors map to 400
Steps: ProblemDetailsTests invalid request through test endpoint
Expected: 400 problem+json, code validation.failed, errors object per field, correlationId
Result: Pass (errors keys "Name", "Quantity" asserted; casing known as review S2, not an AC failure)

TC-001-7 | AC-5 | Correlation id echoed, generated, sanitised, logged
Steps: curl with X-Correlation-Id: abc-123; without; with "bad id<script>"; with 200 chars; read console and logs/qms-*.log
Expected: abc-123 echoed; new 32-hex id when absent; invalid/oversized replaced by a new id; log lines carry CorrelationId=<id>
Result: Pass. Echo abc-123 OK; generated e.g. a15b6d9c...; invalid and oversized replaced. Log lines (console and file): "CorrelationId=abc-123 HTTP GET /health/live responded 200", "CorrelationId=abc-123 HTTP GET /nope responded 404"; query string (?x=secret) not logged. Startup lines outside a request show an empty CorrelationId= (expected, no request). CorrelationIdTests (4) also pass.

TC-001-8 | AC-6 | Swagger in Development, absent in Production
Steps: GET /swagger, /swagger/index.html, /openapi/v1.json in both environments
Expected: Development 200 with API v1; Production 404
Result: Pass (Dev: /swagger 301 -> index.html 200, swagger-ui 5.33.3, openapi info "Bank QMS API", version "v1"; Prod: /swagger/index.html and /openapi/v1.json 404 problem+json)

TC-001-9 | AC-7 | Compose stack
Steps: .env from .env.example; docker compose up -d --build (image built with a temporary CA-bundle copy of the Dockerfile and compose override kept in the scratchpad, outside the repo; ports remapped to 18080/15432 only in the override); GET /health/live; inspect user; down -v
Expected: postgres healthy, api non-root, /health/live 200
Result: Pass. postgres (healthy), api Up; GET http://localhost:18080/health/live -> 200; container user app uid/gid 1654 (PID 1 Uid 1654); /app/logs owned by app and the log file was written. The stack was removed with down -v. linux/arm64 was not run (amd64 host); the arm64 mapping was checked by reading (TARGETARCH amd64->x64, arm64 unchanged).
Note: with .env.example (Development) the container logs Debug-level health-check lines; Swagger answered 200 inside the container (review S1 covers the exposure).

TC-001-10 | AC-8 | CI backend job
Steps: read .github/workflows/ci.yml; run the exact steps locally (restore, build --no-restore -c Release, test --no-build -c Release --logger trx)
Expected: steps match the layout and pass
Result: Pass (local simulation: 0 warnings, 35 passed). The workflow was not run on GitHub (no PR was created); the check is by reading plus local simulation. setup-dotnet uses backend/global.json; the artifact path TestResults is relative to the repo root and ignored by .gitignore.

TC-001-11 | negative | HEAD, OPTIONS, POST on /health/live
Result: Pass (all 200 on this endpoint, no crash, no body leak)
TC-001-12 | negative | 9000-character URL
Result: Pass (414 from Kestrel, no crash)
TC-001-13 | negative | Accept: application/xml on /health/live
Result: Pass (200 text/plain Healthy)
TC-001-14 | negative | Unknown route with Accept text/html
Result: Not tested separately; behaviour known as review N3.
```

AC results: AC-1 Pass, AC-2 Pass, AC-3 Pass, AC-4 Pass, AC-5 Pass, AC-6 Pass, AC-7 Pass, AC-8 Pass (by reading and local simulation)
Bugs: none. Follow-ups (not bugs): review S1-S3 and N1-N7. Extra observations for the backlog: the "Healthy" body is plain text, not JSON; a Development container writes Debug-level lines (appsettings.Development.json); `dotnet run` must start from the project folder or `appsettings.json` is not found (normal .NET behaviour).

### Stage 7 — QA — qa-tester — 2026-11-07
Result: Done
AC results: AC-1 Pass, AC-2 Pass, AC-3 Pass, AC-4 Pass, AC-5 Pass, AC-6 Pass, AC-7 Pass, AC-8 Pass
Bugs: none
Changed: docs/work/stories/STORY-001-backend-foundation.md (QA section only)
Manual device checks pending: none (no Android or display change). The first GitHub Actions run on a real PR still confirms AC-8.
Notes for next stage: Sign-off given. Story status set to Done. Review S1-S3 should become follow-up tasks (S3 and S2 before STORY-002 and STORY-003).
Open questions: none

### Stage 3 — Backend (TASK-006 review follow-ups) — backend-developer — 2026-11-07
Result: Done
Changed: docker-compose.yml, .env.example, backend/README.md, backend/Qms.Api/Common/ErrorKeyNaming.cs (new), backend/Qms.Api/Common/ResultExtensions.cs, backend/Qms.Api/Program.cs, backend/Qms.Tests/Support/QmsApiFactory.cs, backend/Qms.Tests/Support/TestThings.cs, backend/Qms.Tests/Api/ProblemDetailsTests.cs, docs/work/tasks/TASK-006-story-001-review-follow-ups.md
Notes for next stage: S1 api port bound to 127.0.0.1 (override documented in backend/README.md). S2 error keys are camelCase without `$.` prefix (`name`, `items[0].name`) for both FluentValidation and built-in model-binding errors; web and Android can map keys directly to JSON property names. S3 test host writes no log file. Build 0 warnings; 37 tests passed (run twice).
Open questions: none

### Stage 6 — Code review (TASK-006) — code-reviewer — 2026-11-07
Result: Approved
Builds/tests: backend ✅ (`dotnet build backend/Qms.slnx -c Release`: 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release`: 37 passed, 0 failed, 0 skipped; no `logs/` folder under `Qms.Tests/bin` after the run) · web N/A · android N/A
Scope: TASK-006 only (`git diff 7ba9f55 HEAD -- backend docker-compose.yml .env.example`).
ACs checked: AC-1 ✅, AC-2 ✅, AC-3 ✅, AC-4 ✅ (keys now `name`, `quantity`, `parts[1].name`; still 400 problem details with `errors` per field and `code`), AC-5 ✅ (the in-memory sink is untouched; console sink 0 unchanged; correlation id tests green), AC-6 ✅, AC-7 ✅ (`docker compose --env-file .env.example config` shows `host_ip: 127.0.0.1` for api and postgres; `http://localhost:8080/health/live` still matches the AC), AC-8 ✅ (no CI change).
Fixes verified:
- S1 fixed: api is published on `127.0.0.1`; `.env.example` and `backend/README.md` explain that Development/Swagger is local-only and how to open the port for device testing (with Production).
- S2 fixed: `ErrorKeyNaming.Normalize` is applied in `ResultExtensions.ToProblem` and in the wrapped `InvalidModelStateResponseFactory`. Probed cases: `""` → `""`, `$` → `""`, `$.a.b` → `a.b`, `Items[0].Name` → `items[0].name`, `Items[0][1].Name` → `items[0][1].name`, `name` → `name`, `ID` → `id`, `URL` → `url`, `DeviceID` → `deviceID`, `URLValue` → `urlValue`, `Dict[Key].Value` → `dict[Key].value`. It uses the same `JsonNamingPolicy.CamelCase` as the serializer, so keys match the JSON property names (acronyms included). That is acceptable. Built-in path probed in the test host with malformed JSON, an empty body, a root array and a wrong nested type: always 400 `application/problem+json` with `traceId`, `correlationId`, `code` (`request.invalid`) and camelCase keys. The FluentValidation path keeps `validation.failed` plus `detail`. The difference between the two codes existed before this change and is meaningful (body not bindable vs rule failed). The wrapper copies `ErrorMessage` only, which is safe: errors that carry only an exception still render the MVC default text.
- S3 fixed: the test host rewrites `Serilog:WriteTo:1` (File) to a Console sink at Fatal. No file is written and the in-memory capture still works. (`backend/Qms.Api/logs/` holds an older manual Production run. It is git-ignored and was not created by tests.)
AC-4 output changed only in key casing. TC-001-6's note ("Name", "Quantity") is superseded, and the exact keys are now asserted by `ProblemDetailsTests`, so no QA retest is needed.

Must fix
- none

Should fix
- none

Nice to have
- N8 backend/Qms.Api/Common/ErrorKeyNaming.cs:19-27 — whole-body errors (`$`) become the empty key `""` (the MVC model-level convention). A root-array path `$[0].Name` keeps its `$` (`$[0].name`). Both are acceptable. Document `""` = "whole request" in `backend/README.md` for the web and Android clients.
- N9 backend/Qms.Tests/Support/QmsApiFactory.cs:29-39 — the override targets sink index 1, so it breaks silently if `appsettings.json` reorders or adds sinks. Add a test that the built host has no File sink or that no `logs/` file appears, or key the sinks by name.
- N10 backend/Qms.Api/Program.cs:18 (built-in path, existing behaviour) — System.Text.Json formatter messages expose .NET type names and JSON paths (for example "could not be converted to Qms.Tests.Support.ThingPart. Path: $.parts[0].name"). Before public endpoints land (STORY-002+), consider `JsonOptions.AllowInputFormatterExceptionMessages = false` or a generic message.
- N11 the extra `command: ["The command field is required."]` entry on binding failures names the C# parameter, not a JSON field. Consider `[FromBody(EmptyBodyBehavior = ...)]` or filtering the parameter-name key in the factory wrapper.
Notes for next stage: none. QA already signed off STORY-001, and this follow-up does not change any AC enough to need a retest. Story status returned to Done. N8–N11 can be follow-ups with STORY-002.
Open questions: none
