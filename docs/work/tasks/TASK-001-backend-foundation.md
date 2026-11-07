# TASK-001 — Backend solution, API host and Docker Compose

| Field | Value |
|---|---|
| ID | TASK-001 |
| Story | STORY-001 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | Done |
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
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] Bilingual text added (en + ar) if user-facing — N/A
- [x] No business logic outside `Qms.Application`
- [x] Spec / ADR updated if behaviour changed
- [x] `CLAUDE.md` commands and `backend/README.md` match reality
- [x] Handoff written in the story's Stage log

## 6. Tests
35 tests in `backend/Qms.Tests`, all green (`dotnet test backend/Qms.slnx -c Release`).
- Architecture (`Architecture/ArchitectureTests.cs`): Domain has no dependency on Application/Infrastructure/Api (NetArchTest); Application not on Infrastructure/Api; Infrastructure not on Api; project references per `.csproj` follow Domain <- Application <- Infrastructure <- Api (4 cases); no handlers inside Api.
- Application (`Application/`): `ResultTests` (4), `ValidationDecoratorTests` (5: invalid/valid command, invalid/valid query, `AddApplication`).
- Infrastructure: `InfrastructureRegistrationTests` (TimeProvider registered).
- Api integration (`Api/`, `WebApplicationFactory<Program>`): `HealthTests` (live 200, ready 200, ready 503 with failing check while live stays 200), `ProblemDetailsTests` (404, unhandled exception 500 without leak, 400 with `errors` + `code`, 409 with code, 404 from query, 200 success), `CorrelationIdTests` (echo + `CorrelationId` on every request log line, exception log line, new id per request, unsafe id replaced), `HostingTests` (Swagger UI + OpenAPI in Development, not in Production, HSTS outside Development only).

## 7. Notes / result
- AC-1 to AC-8 met. Release build: 0 warnings, 0 errors. Tests: 35 passed, 0 failed, 0 skipped.
- Docker Compose verified: postgres healthy, api runs as `app` (uid 1654), `GET http://localhost:8080/health/live` returns 200, then `docker compose down -v`.
- No Scrutor: plain reflection scanning in `Qms.Application/Common/DependencyInjection.cs`.
- xUnit v2 (2.9.3) instead of v3: v3 on SDK 10 requires the Microsoft.Testing.Platform opt-in via `global.json`, which is not picked up by `dotnet test backend/Qms.slnx` from the repository root. Shouldly instead of FluentAssertions (licence). Testcontainers is not added yet (no database until STORY-002).
- Serilog uses `AddSerilog(..., preserveStaticLogger: true)` and `UseSerilogRequestLogging(Logger = DI logger)` so several hosts in one process (tests) do not share a logger.
