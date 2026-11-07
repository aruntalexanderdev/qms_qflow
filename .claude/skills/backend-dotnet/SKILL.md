---
name: backend-dotnet
description: Senior .NET 10 backend method for the QMS — clean architecture, use cases, queue engine, validation, auth and branch scoping, SignalR events, background jobs, and testing. Use in stage 3.
---

# Backend (.NET) skill

## Layering
- `Qms.Domain`: entities, enums, value objects (e.g. `TokenNumber`), domain errors. No framework references.
- `Qms.Application`: one folder per feature (`Tokens/IssueToken/`, `Counters/CallNext/` …) with Command/Query, Handler, Validator, DTOs. The queue engine is a service here (`IQueueEngine`).
- `Qms.Infrastructure`: EF Core repositories/DbContext access, `IMessageSender` adapters (phase 2), `TimeProvider`, background jobs.
- `Qms.Api`: minimal controllers (`[ApiController]`, route `/api/v1/...`), `QmsHub`, auth, middleware, problem details.

## Patterns
- Handlers return `Result<T>`; controllers map errors to problem details (400 validation, 403 scope, 404, 409 conflict such as "service full").
- Every request DTO has a FluentValidation validator.
- Authorization: role policy + branch scope check (`ICurrentUser.CanAccessBranch(branchId)`) in the handler; device endpoints use `X-Device-Key` auth handler.
- Every state change writes `audit_log` in the same transaction.
- After commit, publish SignalR events through `IQmsNotifier` (groups `branch:`, `zone:`, `counter:`, `device:`) — never send events before commit.
- Use `TimeProvider` and branch timezone for business date; store UTC.

## Queue engine rules to implement exactly (spec 6)
- IssueToken: dispenser mapping → pair Available → under daily limit → branch open → atomic sequence → token + first `token_visit` → commit → `TokenIssued`, `QueueChanged`.
- CallNext: counter Open and free → candidate pairs in counter zone → order by priority_level, queued_at → `FOR UPDATE SKIP LOCKED LIMIT 1` → Called.
- Complete: close visit; apply flow (Auto/Ask/None); keep number and priority.
- Transfer, Skip (NoShow/Hold per branch setting), Recall (limit), Cancel, Reinstate.

## Tests
- Unit tests per rule (xUnit + FluentAssertions), fake `TimeProvider`.
- Integration tests with `WebApplicationFactory` + Testcontainers PostgreSQL, seeded with the Branch 1 example.
- Concurrency test: two counters call next at once → different tokens; 50 parallel issues → 50 unique numbers.

## Performance
Token issue and Call Next < 300 ms; avoid N+1 (project to DTOs), no lazy loading.
