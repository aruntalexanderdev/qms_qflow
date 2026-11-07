# QMS Backend — ASP.NET Core API

REST API, SignalR hub, queue engine and background jobs for the Bank QMS. Spec: [`docs/PROJECT_SPEC.md`](../docs/PROJECT_SPEC.md) sections 2, 6, 9, 10.

> Status: not scaffolded yet. Built in milestone **M1 Foundation**.

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
  Qms.Tests/           xUnit + FluentAssertions + Testcontainers (PostgreSQL)
```

Dependencies point inward: Api → Infrastructure → Application → Domain.

## Stack

- ASP.NET Core Web API + SignalR
- EF Core 10 + Npgsql, code-first migrations, snake_case naming
- ASP.NET Core Identity + JWT (15-min access token, rotated refresh token); `X-Device-Key` for devices
- FluentValidation, RFC 7807 problem details
- Serilog (console + file) with correlation id
- OpenAPI (built-in `Microsoft.AspNetCore.OpenApi`) + Swagger UI / Scalar
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
dotnet ef migrations add <Name> -p backend/Qms.Infrastructure -s backend/Qms.Api
```

## Configuration

Keep secrets out of `appsettings*.json`. For local work use user-secrets:

```bash
dotnet user-secrets --project backend/Qms.Api set "ConnectionStrings:Qms" "Host=localhost;Database=qms;Username=qms;Password=<local>"
```

In containers, pass configuration as environment variables (for example `ConnectionStrings__Qms`, `Jwt__SigningKey`).
