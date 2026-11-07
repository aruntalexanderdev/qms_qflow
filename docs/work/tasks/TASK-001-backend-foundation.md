# TASK-001 — Backend solution, API host and Docker Compose

| Field | Value |
|---|---|
| ID | TASK-001 |
| Story | STORY-001 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | To do |
| Estimate | M |
| Created | 2026-11-07 |

## 1. Goal
Create the .NET 10 solution, the API host with the cross-cutting plumbing (ADR-002), the API Dockerfile and the root Docker Compose stack.

## 2. Acceptance criteria covered
- AC-1 to AC-8 (STORY-001)

## 3. Technical details
- `backend/Qms.slnx`; projects `Qms.Domain`, `Qms.Application`, `Qms.Infrastructure`, `Qms.Api` (web), `Qms.Tests` (xUnit v3, FluentAssertions, `Microsoft.AspNetCore.Mvc.Testing`, Testcontainers.PostgreSql, NetArchTest or similar).
- `backend/Directory.Build.props` exists (Phase 0). Add `backend/Directory.Packages.props` (central package management) with the 10.0.x versions.
- `Qms.Application/Common/`: `Result`, `Result<T>`, `Error`, `ErrorType`, `ICommandHandler<,>`, `IQueryHandler<,>`, `ValidationDecorator<,>`, `DependencyInjection.AddApplication()` (scan and register handlers and validators, decorate).
- `Qms.Api`: `Program.cs` with Serilog (`ReadFrom.Configuration`, console + file), `CorrelationIdMiddleware`, `AddProblemDetails` + an exception handler, a `ResultExtensions.ToActionResult()` mapper, `AddOpenApi()` + Swagger UI (Development only), health checks at `/health/live` and `/health/ready`, `TimeProvider.System`, route prefix `api/v1`.
- `backend/Qms.Api/Dockerfile`: `mcr.microsoft.com/dotnet/sdk:10.0` build → `aspnet:10.0` runtime, `USER app`, port 8080, multi-arch.
- Root `docker-compose.yml`: `postgres` (`postgres:18`, volume `pgdata`, healthcheck `pg_isready`), `api` (depends on postgres healthy, env from `.env`). Root `.env.example` with placeholders only.
- Tests: an architecture test for the layer references, health endpoint tests, problem-details tests (404, 500, 400 validation), a correlation id test.

## 4. Dependencies
- Needs: nothing (first task).
- Provides to: TASK-002/003 (solution and DI entry points), TASK-004 (API host), TASK-005 (compose file to extend).

## 5. Done checklist
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] Bilingual text added (en + ar) if user-facing — N/A
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] `CLAUDE.md` commands and `backend/README.md` match reality
- [ ] Handoff written in the story's Stage log

## 6. Tests
- …

## 7. Notes / result
