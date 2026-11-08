# TASK-010 — Shared setup-API plumbing and service type / priority type endpoints

| Field | Value |
|---|---|
| ID | TASK-010 |
| Story | STORY-005 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | Done |
| Estimate | L |
| Created | 2026-11-08 |

## 1. Goal
Build the ADR-008 plumbing used by all M2 stories (part A), then the CRUD endpoints for service types and priority types on top of it (part B), with unit and integration tests. Do part A first and commit it separately.

## 2. Acceptance criteria covered
- Part A (plumbing): AC-26, AC-27, AC-28, AC-29, AC-30, AC-31, AC-32, AC-33, AC-34, AC-35, AC-37
- Part B (endpoints): AC-1 to AC-25 (and AC-26, AC-27, AC-33 again through the real endpoints)

## 3. Technical details
> Superseded in parts (read with the notes in section 7): policies `SetupRead`/`BranchSetup` are replaced by permission-based `[RequirePermission]` (ADR-009); the DTOs expose `name` + `translations`, not `nameEn`/`nameAr` (ADR-010).
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
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] Bilingual text added (en + ar) if user-facing — N/A, API returns codes only
- [x] No business logic outside `Qms.Application`
- [x] Spec / ADR updated if behaviour changed (spec 10 item routes after PO acceptance) — not edited by stage 3; see notes
- [x] Handoff written in the story's Stage log

## 6. Tests
- Unit (`Qms.Tests`): validators (every case of AC-5 and AC-17), audit decorator (AC-31), version helpers (AC-32), error catalog incl. "every catalog name exists in model or migration" (AC-29), `BranchScope` (AC-30), notifier trigger rules (AC-33), architecture tests (AC-34).
- Integration (Testcontainers, `SetupTestData`, per ADR-008 §15): every endpoint with happy path plus audit row, 400, 401, 403 for CS and SA (and BM-B1 on writes), cross-company 404 with CA-C2, 409 stale version, 409 in use (one per reference kind), prefix taken, concurrent create race; test-host endpoints for enum strings, error catalog and branch scope (AC-28 to AC-30); `SetupTestData` idempotence (AC-35).
- Existing tests stay green: `dotnet test backend/Qms.slnx`.

## 7. Notes / result
Done by backend-developer, 2026-11-08. No schema change; Persistence/ touched once (see decisions).

**Tests added** (`Qms.Tests`; 254 existing + 123 new = 377 green):
- `Setup/ServiceTypesApiTests`, `Setup/PriorityTypesApiTests`: AC-1 to AC-24 (list/filter/get, create + Location, every validation case, prefix unique + concurrent race, update, stale/missing/garbage version, deactivate, delete unused, in-use per reference kind, cross-company 404, translations rules).
- `Setup/AccessAndAuditTests`: AC-25 role matrix (CA, BM-B1/B2, CS, SA, anonymous), AC-26/27 audit rows and failed writes, AC-33 notifier trigger rules, AC-37 problem+json format.
- `Setup/PlumbingTests`: AC-28 enums, AC-29 error catalog (test-host raises each error; real unique, check and FK errors; catalog names exist in the migrated schema; every FK into service_type/priority_type is in the catalog), AC-30 branch scope, AC-35 `SetupTestData`.
- `Application/AuditDecoratorTests` (AC-31, AC-27, post-commit), `Application/BranchScopeTests`, `Application/SetupPlumbingUnitTests` (version helpers AC-32, validators AC-5/AC-17, permission map and resolver, catalog defaults, localized text, production notifier no-op), `Architecture/ArchitectureTests` (AC-34 and controllers).

**Files**
- Application: `Common/IQmsDbContext, AuditedAttribute (+NotAudited), AuditDecorator (+AuditRules), PostCommitActions, IBranchScope, IDeviceConfigNotifier, ErrorCodes, RowVersionCodec, CommonRules, LocalizedText, Unit`, `Common/DependencyInjection` (audit decorator in chain), `Auth/Permissions` (constants, `RolePermissionMap`, `IPermissionResolver`), `ServiceTypes/*`, `PriorityTypes/*`.
- Infrastructure: `Scope/BranchScope`, `Errors/PostgresErrorCatalog`, `Notifications/NoOpDeviceConfigNotifier`, `Authorization/StaticRolePermissionResolver`, `DependencyInjection`; `Persistence/QmsDbContext` (only `: IQmsDbContext` + `BeginTransactionAsync`).
- Api: `Auth/RequirePermission`, `Controllers/ServiceTypesController, PriorityTypesController`, `Common/ProblemDetailsSetup` (DbUpdateException via catalog), `Common/ResultExtensions` (created/no-content), `Program` (string enums).
- Tests support: `SetupTestData`, `SetupHost`, `SetupReferences`, `SetupTestEndpoints`, `QmsApiFactory` (recording notifier, SQL log).

**Decisions / deviations from ADR-008**
1. Authorization is permission based (product owner change, ADR-009): `[RequirePermission(Permissions.X.Y)]` + one handler + `IPermissionResolver` backed by a static role map reproducing the section 9 matrix. The named policies `SetupRead`/`BranchSetup` of the task text do not exist. Handler passes `RequestAborted`. `BranchScope` keeps the only `IsInRole(CompanyAdmin)`.
2. `IDeviceConfigNotifier.ConfigChangedAsync(DeviceConfigChange(CompanyId, BranchId?, DeviceId?))` instead of a bare id: type names/fees are company-wide. Called through `IPostCommitActions`, which `AuditDecorator` runs after the commit (the handler itself runs inside the decorator transaction).
3. Product owner change: names are `name` + `translations` (only `ar` accepted; other languages 400 `translation.language_not_enabled`, key `en` 400 `translation.default_language_key`, errors keyed `translations.<code>`; empty = absent). Temporary mapping in `LocalizedText` (`name`->`name_en`, `ar`->`name_ar`, absent -> empty string since `name_ar` is NOT NULL). Validation runner now returns a shared specific dotted error code instead of `validation.failed` (ADR-008 section 6).
4. `IBranchScope.EnsureAccessAsync` also answers 404 itself (company-admin: branch must exist in tenant; manager not assigned: exists -> 403 else 404), so a handler that forgets the pre-load is still safe.
5. Update compares the client version with the loaded row first (409 even if values are unchanged, because EF sends no UPDATE then) and also sets `OriginalValue`. Delete loads the row, then removes it.
6. Version string = base64url of the 4 xmin bytes (6 chars); set version = SHA-256 of sorted (id, xmin) pairs, 22 chars.
7. Pre-checks for delete also cover branch/counter mappings and token visits/sequences. `PostgresErrorCatalog` lists 12 FK names (service/priority type references) + 4 user-assignment FKs + the 2 prefix indexes.
8. Not done: the optional live curl run (covered by the integration tests on the real host).

**Review loop 1 (2026-11-08)** — all Must fix and Should fix items done:
- M1: tests added for `en` key on priority types, missing `name`, PUT with `translations: {}` (clears Arabic) / `fr` / `en`, create with `{}` and `{ "ar": "" }`.
- S1 PUT rule (for ADR-008 section 5/8): a `PUT` is a full replacement and never defaults a missing field. The request/command fields that are bool or int are nullable (`bool? IsActive`, `bool? IsPaid`, `int? SortOrder`, `int? PriorityLevel`) with a `Sent()` (`NotNull`) validator, so a missing value is 400 `validation.failed` keyed by the field. Optional-by-meaning fields (`targetMinutes`, `feeAmount` when unpaid, `translations`) may be null and null means "clear". Create keeps `isActive` default true; `isPaid` and `priorityLevel` are required on create too. Every later `PUT` copies this.
- S2/S3: post-commit actions run with their own token and a 30 s timeout (all failures logged, including cancellation); `IPostCommitActions.Enqueue` throws `InvalidOperationException` when the command is not run by `AuditDecorator`, so an action is never silently dropped.
- S4: `IAuditCompanyTarget.AuditCompanyId` on a command (or result) names the audit company when there is no current tenant (Super Admin cross-tenant); the tenant always wins; neither present is a loud `InvalidOperationException`.
- S5: reflection test over every public controller in Qms.Api (only `AuthController` exempt) plus a role-map invariant (any held action implies `<module>.view`) and a catalog-membership test.
- S6: `PostgresFixture.NewSetupDatabaseAsync()` builds one template per run (seed + roles + admin users + `SetupTestData`, hashing paid once) and clones it per test with `CREATE DATABASE ... TEMPLATE`; `SetupHost.StartAsync` uses it (`AuthHost.StartOn(connectionString)` for a host on an existing database). Later stories keep calling `SetupHost.StartAsync(postgres)` and get the speed-up for free. Test suite 4 m 29 s (377 tests) -> 2 m 40 s (392 tests). A shared host per class was not added (xUnit class fixtures cannot take the collection's `PostgresFixture`).
- S7: a bare `PostgresException` from raw SQL is mapped through `PostgresErrorCatalog` (test-host raw insert -> 409 `service_type.prefix_taken`).
- N1: `UnsupportedLanguages` removed. N2: translation keys differing only by case are rejected (400 keyed `translations.<code>`). N5 (a PUT with equal values and a matching version writes an audit row but no UPDATE) is accepted behaviour for ADR-008 section 11. N6: superseded note added at the top of section 3. N3/N4 not done (follow-ups).
