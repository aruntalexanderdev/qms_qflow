# TASK-003 — Register the DbContext, dev migration, readiness check and tenant context

| Field | Value |
|---|---|
| ID | TASK-003 |
| Story | STORY-002 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | Done |
| Estimate | S |
| Created | 2026-11-07 |

## 1. Goal
Wire `QmsDbContext` into the API: configuration, Development-only migrate + seed on startup, the PostgreSQL readiness check, and the `ICurrentTenant` plumbing the query filter reads.

## 2. Acceptance criteria covered
- AC-7, AC-8 (STORY-002); keeps AC-7 of STORY-001 green with the database.

## 3. Technical details
- `Qms.Infrastructure/DependencyInjection.AddInfrastructure(IConfiguration)`: `AddDbContext<QmsDbContext>` with Npgsql, connection string `ConnectionStrings:Qms`, retry on failure, snake_case.
- `ICurrentTenant` in `Qms.Application/Common`; a temporary implementation in `Qms.Api` (no company until STORY-003 adds claims; tests set it explicitly).
- Startup: in Development, `Database.MigrateAsync()` then `DevSeeder.SeedAsync()`; never in Production.
- `AddHealthChecks().AddNpgSql(...)` tagged `ready`; `/health/ready` only runs `ready` checks, `/health/live` none.
- `docker-compose.yml`: pass `ConnectionStrings__Qms` to `api` from `.env`.
- Integration test with `WebApplicationFactory` + Testcontainers: `/health/ready` is 200 with the database up, 503 with it stopped.

## 4. Dependencies
- Needs: TASK-001, TASK-002.
- Provides to: TASK-004.

## 5. Done checklist
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] Bilingual text added (en + ar) if user-facing — N/A
- [x] No business logic outside `Qms.Application`
- [x] Spec / ADR updated if behaviour changed
- [x] Handoff written in the story's Stage log

## 6. Tests
- `Qms.Tests/Api/DatabaseHostingTests.cs` (Testcontainers): ready 200 with DB up; 503 with an unreachable DB (live stays 200); 200 then 503 after the container is stopped; Development startup migrates a blank DB and seeds it; second startup creates no duplicates; Production startup creates no tables; missing connection string fails startup with a clear message; scoped `ICurrentTenant` (Company 1, then Company 2) limits the request-scoped context (AC-7); default scope has no company and sees nothing.
- `Qms.Tests/Infrastructure/InfrastructureRegistrationTests.cs`: context and seeder resolve; missing/blank connection string message.
- Suite: Passed 110, Failed 0, Skipped 0 (Release, run twice).

## 7. Notes / result
- Readiness check is a small custom `DatabaseHealthCheck` in Qms.Api (`CanConnectAsync`, 5 s timeout), no extra package.
- Retry-on-failure is off: it conflicts with explicit transactions (queue engine, `FOR UPDATE SKIP LOCKED`) unless each is wrapped in an execution strategy.
- Migrate + seed run in Development only, and can be switched off with `Database:MigrateOnStartup=false`. `QmsApiFactory` without a connection string uses that switch and drops the database ready check, so the STORY-001 tests need no Docker.
- `docker-compose.yml` already passed `ConnectionStrings__Qms` from `.env` values; verified with `docker compose --env-file .env.example config`. `CLAUDE.md` and `backend/README.md` updated.
- Manually verified against `postgres:18`: `/health/ready` 200, then 503 after stopping it; `/health/live` 200 throughout.
