# QMS Backend — ASP.NET Core API

REST API, SignalR hub, queue engine and background jobs for the Bank QMS. Spec: [`docs/PROJECT_SPEC.md`](../docs/PROJECT_SPEC.md) sections 2, 6, 9, 10.

> Status: foundation scaffolded (STORY-001). Use cases, database and auth arrive with later M1 stories.

## Versions

| Item | Version |
| --- | --- |
| .NET SDK | **10.0.x** (LTS, supported to Nov 2028), pinned in [`global.json`](global.json) |
| ASP.NET Core / EF Core | 10.0.x |
| Npgsql EF Core provider | 10.0.x |
| PostgreSQL | 16 or later (18 recommended for new installs) |
| Solution format | `Qms.slnx` (the default for `dotnet new sln` in SDK 10) |

.NET 11 is a Standard Term Support (STS) release. Stay on .NET 10 LTS unless an ADR approves a move.

## Structure

```
backend/
  Qms.slnx
  global.json
  Qms.Domain/          entities, enums, value objects, domain rules (no framework refs)
  Qms.Application/     commands/queries, handlers, validators, DTOs, queue engine
  Qms.Infrastructure/  EF Core DbContext, migrations, repositories, messaging adapters
  Qms.Api/             controllers (/api/v1), SignalR hub (/hubs/qms), auth, middleware
  Qms.Tests/           xUnit v2 + Shouldly + NetArchTest + WebApplicationFactory (Testcontainers PostgreSQL from STORY-002)
  Directory.Build.props / Directory.Packages.props   shared settings and central package versions
```

Dependencies point inward: Api → Infrastructure → Application → Domain.

## Stack

- ASP.NET Core Web API + SignalR
- EF Core 10 + Npgsql, code-first migrations, snake_case naming
- ASP.NET Core Identity + JWT (15-min access token, rotated refresh token); `X-Device-Key` for devices
- FluentValidation, RFC 7807 problem details
- Serilog (console + rolling file under `logs/`) with `CorrelationId` on every line; `X-Correlation-Id` in and out
- OpenAPI (built-in `Microsoft.AspNetCore.OpenApi`, `/openapi/v1.json`) + Swagger UI at `/swagger`, Development only
- Health: `/health/live` (process) and `/health/ready` (dependencies, tagged `ready`)
- Application layer: plain handlers + `Result<T>` + validation decorator, no MediatR (ADR-002)
- Hosted services for daily reset, no-show timeout and the phase 2 message queue

## Prerequisites (macOS / Apple Silicon)

```bash
brew install --cask dotnet-sdk
```

```bash
dotnet --version   # must be 10.0.x
```

```bash
dotnet tool install --global dotnet-ef
```

Docker Desktop (arm64) is needed for PostgreSQL and for Testcontainers.

## Commands

Run these from the repository root:

```bash
dotnet build backend/Qms.slnx
```

```bash
dotnet test backend/Qms.slnx
```

```bash
dotnet run --project backend/Qms.Api
```

```bash
cp .env.example .env && docker compose up --build
```

The stack serves the API on `http://localhost:8080` (for example `/health/live`). The API port is published on `127.0.0.1` only, like PostgreSQL, so the stack is not reachable from the LAN by default. `.env.example` sets `ASPNETCORE_ENVIRONMENT=Development`, which enables Swagger UI at `/swagger` and `/openapi/v1.json`; that is for the local stack only, and a deployed environment must use `Production`.

To reach the local API from another device (for example a token dispenser on the same network), change the port mapping in `docker-compose.yml` to `"0.0.0.0:${API_PORT:-8080}:8080"` for that session and set `ASPNETCORE_ENVIRONMENT=Production` in `.env` so Swagger is not exposed.

Validation errors (`errors` in problem details) use camelCase keys without a `$.` prefix, for example `name` or `items[0].name`, for both FluentValidation failures and model-binding failures.

```bash
dotnet ef migrations add <Name> -p backend/Qms.Infrastructure -s backend/Qms.Api
```

## Configuration

Keep secrets out of `appsettings*.json`. For local work use user-secrets:

```bash
dotnet user-secrets --project backend/Qms.Api set "ConnectionStrings:Qms" "Host=localhost;Database=qms;Username=qms;Password=<local>"
```

In containers, pass configuration as environment variables (for example `ConnectionStrings__Qms`, `Jwt__SigningKey`).

## Testing notes

- xUnit v2 is used on purpose: xunit v3 on .NET SDK 10 needs the Microsoft.Testing.Platform runner opt-in in a `global.json` at the directory where `dotnet test` runs, which does not work for `dotnet test backend/Qms.slnx` from the repository root (and CI). Revisit when the repo can opt in.
- Shouldly is the assertion library (FluentAssertions v8+ needs a commercial licence).
- Test-only endpoints, handlers and validators live in `Qms.Tests/Support` and are added only to the test host (`QmsApiFactory`).
