# TASK-003 — Register the DbContext, dev migration, readiness check and tenant context

| Field | Value |
|---|---|
| ID | TASK-003 |
| Story | STORY-002 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | To do |
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
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] Bilingual text added (en + ar) if user-facing — N/A
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- …

## 7. Notes / result
