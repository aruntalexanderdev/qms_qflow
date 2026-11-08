# TASK-010 — Shared setup-API plumbing and service type / priority type endpoints

| Field | Value |
|---|---|
| ID | TASK-010 |
| Story | STORY-005 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | To do |
| Estimate | L |
| Created | 2026-11-08 |

## 1. Goal
Build the ADR-008 plumbing used by all M2 stories (part A), then the CRUD endpoints for service types and priority types on top of it (part B), with unit and integration tests. Do part A first and commit it separately.

## 2. Acceptance criteria covered
- Part A (plumbing): AC-26, AC-27, AC-28, AC-29, AC-30, AC-31, AC-32, AC-33, AC-34, AC-35, AC-37
- Part B (endpoints): AC-1 to AC-25 (and AC-26, AC-27, AC-33 again through the real endpoints)

## 3. Technical details
**Part A — plumbing (ADR-008)**
- `Qms.Application/Common`: `IQmsDbContext` (DbSets of Domain entities, `SaveChangesAsync`, `BeginTransactionAsync`; add the `Microsoft.EntityFrameworkCore` package, no reference to Infrastructure); `AuditedAttribute(entity, action)`, `NotAuditedAttribute` (type or property); `AuditDecorator` (chain validation, cross-tenant, audit, handler; one transaction; one `audit_log` row with company, user, entity, entity id from result `Id` else command `Id`, action, `TimeProvider` time, `data` JSON without `[NotAudited]` properties); register it in `DependencyInjection.RegisterDecorated`; `IBranchScope` (`EnsureAccessAsync`, `AccessibleBranchIdsAsync`); `IDeviceConfigNotifier` + no-op implementation; shared error codes (ADR-008 §13); `Version` helpers (`xmin` to opaque string and back; set hash of sorted (row id, `xmin`) pairs; bad string = validation error).
- `Qms.Infrastructure`: `QmsDbContext : IQmsDbContext`; `BranchScope` (reads `user_branch` once per request with `IgnoreQueryFilters()` by user id); `PostgresErrorCatalog` (constraint name to status and code, including the two prefix unique indexes and the four STORY-003 user-assignment constraints; default codes `resource.in_use`, `resource.duplicate`, `request.invalid`).
- `Qms.Api`: `JsonStringEnumConverter` globally; policies `SetupRead` (CompanyAdmin, BranchManager), `CompanyAdmin`, `BranchSetup` (CompanyAdmin, BranchManager); extend `GlobalExceptionHandler` for `DbUpdateException` with `PostgresException` 23001 / 23503 / 23505 / 23514 via the catalog (never expose messages); keep `DbUpdateConcurrencyException` to 409 `request.conflict`. Super Admin and Counter Staff get 403 `auth.forbidden` from the policies.
- Test support in `Qms.Tests`: `SetupTestData` (Company 2 with service type prefix A, priority type N, branch, zone, counter, dispenser; Branch 2 in Company 1 with zone, counter, dispenser; users CA-C1, CA-C2, BM-B1, BM-B2, CS, SA; token helper; idempotent). Test-host endpoints for AC-28 to AC-31 (enum, error catalog, `IBranchScope`).

**Part B — endpoints (`/api/v1`)**
| Method | Route | Roles | Codes |
|---|---|---|---|
| GET | `/service-types` (`?isActive=`) | CA, BM | — |
| GET | `/service-types/{id}` | CA, BM | 404 `service_type.not_found` |
| POST | `/service-types` | CA | 201; 409 `service_type.prefix_taken` |
| PUT | `/service-types/{id}` | CA | `version` required; 409 `request.conflict`, `service_type.prefix_taken` |
| DELETE | `/service-types/{id}` | CA | 204; 409 `service_type.in_use` |
| GET | `/priority-types` (`?isActive=`) | CA, BM | includes `currencyCode` |
| GET | `/priority-types/{id}` | CA, BM | 404 `priority_type.not_found` |
| POST | `/priority-types` | CA | 201; 409 `priority_type.prefix_taken` |
| PUT | `/priority-types/{id}` | CA | `version` required; 409 `request.conflict` |
| DELETE | `/priority-types/{id}` | CA | 204; 409 `priority_type.in_use` |
- Api: `ServiceTypesController`, `PriorityTypesController`; request records `Create/UpdateServiceTypeRequest`, `Create/UpdatePriorityTypeRequest`; no logic in controllers.
- Application: commands/queries per endpoint with FluentValidation (rules BR-3 to BR-7, ADR-008 §8: names 1 to 200 trimmed, prefix one char A-Z after trim, target minutes greater than 0 or null, sort order 0 or more, level 1 or more, fee 0 to 9 999 999 999.99 max 2 decimals, paid requires fee, unpaid forbids fee, `version` required on PUT); writes marked `[Audited("service_type"|"priority_type", "create"|"update"|"delete")]`; pre-checks for prefix and references (service type: company mapping, flows, dispenser mapping, tokens; priority type: company mapping, branch fees, tokens); unique and FK database errors stay as backstop.
- `IDeviceConfigNotifier.ConfigChangedAsync` after commit when name, prefix, `isActive`, `isPaid` or fee changes.
- DTOs: `ServiceTypeDto { id, nameEn, nameAr, prefix, targetMinutes?, sortOrder, isActive, version }`, `PriorityTypeDto { id, nameEn, nameAr, prefix, priorityLevel, isPaid, feeAmount?, currencyCode, isActive, version }`.
- Tables: `service_type`, `priority_type`, `company` (currency), `audit_log` (write). No schema change.

## 4. Dependencies
- Needs: TASK-009 (`xmin` row version mapped, migration applied), STORY-002 schema, STORY-003 auth and tenant guard.
- Provides to: STORY-006 to STORY-013 (all use the plumbing), stage 6 review, stage 7 QA; STORY-023 (web) uses the endpoints.
- ADR-008 must be accepted by the product owner before the stage 3 merge (section 10 of the story).

## 5. Done checklist
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] Bilingual text added (en + ar) if user-facing — N/A, API returns codes only
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed (spec 10 item routes after PO acceptance)
- [ ] Handoff written in the story's Stage log

## 6. Tests
- Unit (`Qms.Tests`): validators (every case of AC-5 and AC-17), audit decorator (AC-31), version helpers (AC-32), error catalog incl. "every catalog name exists in model or migration" (AC-29), `BranchScope` (AC-30), notifier trigger rules (AC-33), architecture tests (AC-34).
- Integration (Testcontainers, `SetupTestData`, per ADR-008 §15): every endpoint with happy path plus audit row, 400, 401, 403 for CS and SA (and BM-B1 on writes), cross-company 404 with CA-C2, 409 stale version, 409 in use (one per reference kind), prefix taken, concurrent create race; test-host endpoints for enum strings, error catalog and branch scope (AC-28 to AC-30); `SetupTestData` idempotence (AC-35).
- Existing tests stay green: `dotnet test backend/Qms.slnx`.

## 7. Notes / result
<Filled by the agent when done.>
