# QFlow — Bank Queue Management System

A multi-branch bank queue management system. Customers take a token at an Android dispenser, staff call and serve tokens from a web counter console, and zone and counter displays announce each call in Arabic and English. Phase 1 uses printed tokens; phase 2 adds SMS and WhatsApp.

The full specification is in [`docs/PROJECT_SPEC.md`](docs/PROJECT_SPEC.md). Where the code and the spec disagree, the spec wins.

## Architecture

```
 Android dispenser ──REST──┐
 (Sunmi POS + printer)     │
                           ▼
 Angular web app ──REST──► .NET 10 API ──► PostgreSQL
 (admin, counter,  ◄─SignalR─  (queue engine,
  dashboard, displays)          background jobs)
```

It is a modular monolith: one API and one database, with clean-architecture layers inside the backend.

## Repository layout

| Folder | Project | README |
| --- | --- | --- |
| [`backend/`](backend/) | ASP.NET Core 10 Web API, SignalR, EF Core, PostgreSQL | [backend/README.md](backend/README.md) |
| [`frontend/`](frontend/) | Angular 22 web app: admin, counter console, dashboard, displays | [frontend/README.md](frontend/README.md) |
| [`android/`](android/) | Kotlin + Jetpack Compose token dispenser for Sunmi devices | [android/README.md](android/README.md) |
| [`docs/`](docs/) | Spec, rules, ADRs, templates, stories, tasks, bugs | — |
| `.claude/` | Claude Code agents and skills for the stage-by-stage workflow | [CLAUDE.md](CLAUDE.md) |

## Tech stack and versions

| Layer | Technology | Version |
| --- | --- | --- |
| Backend | .NET / ASP.NET Core / EF Core | **10.0 LTS** |
| Database | PostgreSQL (Npgsql EF Core provider 10.x) | 16+ (18 recommended) |
| Real-time | ASP.NET Core SignalR / `@microsoft/signalr` | 10.x |
| Web | Angular / TypeScript / Node.js | **22.x** / 6.0 / 24 LTS |
| Web i18n | ngx-translate (English + Arabic, RTL) | 18.x |
| Mobile | Kotlin / Jetpack Compose / Retrofit | 2.4.x / latest stable BOM |
| Hosting | Docker, Docker Compose, Nginx reverse proxy, HTTPS | — |

## Getting started

Prerequisites on macOS (Apple Silicon): .NET 10 SDK, Node.js 24 (from `.nvmrc`), Docker Desktop, and Android Studio for the dispenser app.

Start the full stack (available after milestone M1):

```bash
docker compose up --build
```

To build and run each project on its own, follow the README in its folder.

## Milestones (phase 1)

| # | Milestone | # | Milestone |
| --- | --- | --- | --- |
| M1 | Foundation (solution, Docker, schema, auth) | M5 | Counter console |
| M2 | Setup API | M6 | Displays |
| M3 | Queue engine + SignalR | M7 | Android dispenser |
| M4 | Admin web | M8 | Dashboard and reports |

## Development workflow

Every change belongs to a story, task or bug in `docs/work/`. It passes through the stages in [`docs/rules/workflow-rules.md`](docs/rules/workflow-rules.md): architect → BA → database → backend → frontend → Android → code review → QA. With Claude Code, run `/run-feature STORY-001`.
