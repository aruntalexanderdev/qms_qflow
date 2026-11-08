# ADR-002 — Application layer: plain command/query handlers and Result<T>

Status: Accepted (merged with the Phase 0 PR, aruntalexanderdev/qms_qflow#3, 2026-11-07)
Date: 2026-11-07
Story: STORY-001

## Context
The spec puts use cases in `Qms.Application` ("commands/queries") and asks for FluentValidation on every request and RFC 7807 errors, but it does not say how handlers are dispatched or how failures travel to the API. MediatR, the usual choice, has required a commercial licence since v13 (2025).

## Decision
1. **No MediatR.** Each use case is a folder under `Qms.Application/<Feature>/<UseCase>/` with `Command` (or `Query`), `Handler`, `Validator` and DTOs.
2. Handlers implement `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` and are registered in DI by assembly scanning at startup. Controllers inject the handler interface they need.
3. **Validation:** a `ValidationDecorator<,>` around every handler runs the FluentValidation validators before the handler. One more decorator writes `audit_log` rows for commands marked `[Audited]`.
4. **Result<T>:** handlers never throw for expected failures. They return `Result<T>` with an `Error(Code, Type, Message, Details?)`, where `Type` is one of `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unauthorized`. Error codes are stable strings (`token.service_full`, `token.fee_due`, `mapping.pair_not_in_branch`).
5. **API mapping:** `Qms.Api` maps `Result` errors to problem details: Validation → 400 (with `errors` per field), Unauthorized → 401, Forbidden → 403, NotFound → 404, Conflict → 409. The error code goes in the `code` extension so web and Android can show translated messages. Unexpected exceptions → 500 problem details with the correlation id and no stack trace.
6. **Controllers** stay thin: bind the request, call the handler, map the result. No business logic (common rules section 4).

## Consequences
- No third-party licence risk, and the call path is explicit and easy to debug.
- A little boilerplate per use case. Templates in the backend skill keep it consistent.
- Web and Android translate errors by `code`, which keeps messages bilingual.

## Spec impact
None (implementation detail of sections 2 and 10).
