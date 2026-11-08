# TASK-008 — Fail-closed tenant writes, claims-based tenant context, user directory, error mapping

| Field | Value |
|---|---|
| ID | TASK-008 |
| Story | STORY-003 (scope amendment 2026-11-08) |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | Done |
| Estimate | L |
| Created | 2026-11-08 |

## 1. Goal
Close the fail-open tenant write path (S2), derive the tenant from the sign-in claims (N7), validate request-supplied user ids (S3, Application side) and map concurrency and tenant exceptions to problem details (S5). TASK-004 stays the auth task.

## 2. Acceptance criteria covered
- AC-9, AC-10, AC-11, AC-12 (S2)
- AC-17 (S3 Application side); AC-13 to AC-16 and AC-23 are proven by TASK-007 and re-checked here through the entities
- AC-18, AC-19, AC-20 (S5)
- AC-21, AC-22 (N7)

## 3. Technical details
- S2:
  - `ICurrentTenant.IsCrossTenant`; new `ITenantScope.BeginCrossTenant()` returning a nesting-safe `IDisposable` (inner dispose does not end an outer section).
  - `QmsDbContext.SaveChanges` order: company never changes; company set → stamp and refuse other company; no company + cross-tenant → no stamping, `Guid.Empty` company throws; no company + not cross-tenant → any Added/Modified/Deleted `ITenantEntity` or `Company` row throws `TenantViolationException` (reason `NoTenant`). Non-tenant tables (`asp_net_*`, `refresh_token`) unaffected.
  - `TenantViolationException.Reason` (`NoTenant`, `OtherCompany`, `CompanyChanged`).
  - `MigrateAndSeedAsync` wraps `DevSeeder.SeedAsync` in a cross-tenant section.
  - `[CrossTenant]` attribute + `CrossTenantDecorator` (ADR-002 chain, after validation): caller must have role `SuperAdmin`, else `Forbidden` `auth.forbidden`; then opens the section for the handler call.
  - Update `SettableCurrentTenant` and existing persistence tests that write with no company to use the flag.
- N7:
  - One scoped `RequestTenantContext` in `Qms.Api` implements `ICurrentTenant` and `ITenantScope`; `CompanyId` from `ICurrentUser` (`company_id` claim); null outside HTTP requests.
  - Register in `AddQmsAuthentication`; remove from `AddQmsHealthChecks`; delete `NoCompanyCurrentTenant.cs`.
- S3 (Application):
  - `IUserDirectory.IsInCompanyAsync(userId, companyId, CancellationToken)` in `Qms.Application`; Infrastructure implementation over `asp_net_users`; a failed check returns `NotFound` `user.not_found`.
  - Document the rule "actor columns come only from `ICurrentUser.UserId`" in ADR-003 for STORY-011 and STORY-014 to STORY-020.
- S5 in `GlobalExceptionHandler`:
  - `DbUpdateConcurrencyException` → 409 `request.conflict`.
  - `TenantViolationException` `OtherCompany` / `CompanyChanged` → 404 `resource.not_found` (Warning log, no entity values).
  - `TenantViolationException` `NoTenant` → 500 `server.unexpected_error` (Error log, no details in the body).
  - STORY-003 has no tenant-write endpoint: integration tests use endpoints registered only in the test host.
- Tests (project rule 8): unit tests for `ITenantScope` nesting, `QmsDbContext` guard, `CrossTenantDecorator`, `IUserDirectory`; one Testcontainers integration test per AC above.
- Code-review hooks: only the seeder, jobs and `CrossTenantDecorator` call `BeginCrossTenant()`.

## 4. Dependencies
- Needs: TASK-007 (migration and `ITenantEntity` on `user_branch` / `user_counter`); TASK-004 for `ICurrentUser`, JWT and the seeded Super Admin / Company Admin (AC-11, AC-12, AC-21, AC-22). Spec section 9 approval blocks the TASK-007 merge, not this task's start.
- Provides to: code review (stage 6), QA (stage 7), and STORY-011 and later write endpoints.

## 5. Done checklist
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] Bilingual text added (en + ar) if user-facing — error codes only; texts in the web app
- [x] No business logic outside `Qms.Application`
- [x] Spec / ADR updated if behaviour changed (ADR-003 Amendment 1 already holds the actor-column rule B.3; no further edit)
- [x] Handoff written in the story's Stage log

## 6. Tests
- `Auth/TenantGuardTests` (integration, DbContext only): AC-9 (branch add; counter modify and delete; Company row; `asp_net_users` and `refresh_token` still save; reads unaffected), AC-10 (`MigrateAndSeedAsync` twice gives identical counts: 1 company, 1 branch, 2 zones, 4 counters, 7 counter mappings; `Guid.Empty` company rejected inside a section; company change rejected inside a section; the request scope after seeding is not cross-tenant), stamping and `OtherCompany` / `CompanyChanged` reasons.
- `Auth/CrossTenantAndErrorMappingTests` (integration, test-host endpoints in `Support/TenantTestEndpoints.cs`): AC-11 (Company Admin, Branch Manager, anonymous; spy proves the body did not run), AC-12 (Super Admin creates a Company 2 branch; section closed afterwards, a follow-up write fails `NoTenant`), AC-17 (foreign and unknown user give the same 404 `user.not_found`; `IsInCompanyAsync` truth table), AC-18 (409 `request.conflict`), AC-19 (`OtherCompany` and `CompanyChanged` give 404 `resource.not_found`; body and Warning log carry no ids), AC-20 (500 `server.unexpected_error`, no details in the body, Error logged with the exception), AC-21 (Company 1 and Company 2 tokens see only their branches; `NoCompanyCurrentTenant` is gone; no `ICurrentTenant` in `ApplicationBuilderExtensions`), AC-22 (Super Admin lists none, then both companies through `[CrossTenant]`).
- `Auth/TokenAndScopeUnitTests`, `Auth/HandlerUnitTests` (unit): nested `ITenantScope` handles (inner and double dispose), `RequestTenantContext`, `CrossTenantDecorator` for every non-Super-Admin role (handler not run, no section), section open for exactly the handler call and closed after an exception, only `[CrossTenant]` handlers are wrapped.
- AC-13 to AC-16 and AC-23 are proven by TASK-007 (`Persistence/UserAssignmentTests`, through EF) and stay green; one assertion there changed (see notes).

## 7. Notes / result
Result: Done.
- **S2**: `ICurrentTenant.IsCrossTenant`; `ITenantScope.BeginCrossTenant()` (nest-counted `CrossTenantSection`); `QmsDbContext.EnforceTenantRules` per ADR-003 A.2 with `TenantViolationReason` {`NoTenant`, `OtherCompany`, `CompanyChanged`}; `MigrateAndSeedAsync` wraps `DevSeeder` and `IdentitySeeder` in a section; `[CrossTenant]` + `CrossTenantDecorator` / `CrossTenantQueryDecorator` (after validation, Super Admin only, `auth.forbidden`). `BeginCrossTenant()` is called in exactly two places in production code: `MigrateAndSeedAsync` and the decorators (background jobs arrive with ADR-005).
- **N7**: `RequestTenantContext` (Qms.Api/Auth) registered in `AddQmsAuthentication`; `NoCompanyCurrentTenant` deleted and removed from `AddQmsHealthChecks`. The design-time factory's private `NoTenant` got `IsCrossTenant => false`.
- **S3 (Application)**: `IUserDirectory.IsInCompanyAsync` (Infrastructure `UserAccounts`), `UserErrors.NotFound`; login, refresh and `/me` read `user_branch` / `user_counter` with `IgnoreQueryFilters()` filtered by the user id.
- **S5**: `GlobalExceptionHandler` maps `DbUpdateConcurrencyException` to 409 `request.conflict` (Warning), `OtherCompany` / `CompanyChanged` to 404 `resource.not_found` (Warning with the reason, method and path only), everything else including `NoTenant` to 500 `server.unexpected_error` (Error with the exception).
- **Not mapped (note for STORY-011)**: the four SQLSTATE 23503 constraints (`fk_user_branch_user_company`, `fk_user_counter_user_company`, `fk_user_branch_branch_company_id_branch_id`, `fk_user_counter_counter_company_id_counter_id`). No production endpoint writes assignments yet. STORY-011 should validate in the handler first (`IUserDirectory`, `IBranchScope`) and map a residual 23503 on those names to 404 `user.not_found` / `resource.not_found`.
- **Changes outside the Stage 3 folders** (named in this task): `Persistence/QmsDbContext.cs`, `Persistence/TenantViolationException.cs`, `Persistence/QmsDbContextFactory.cs`.
- **Test changes**: `SettableCurrentTenant` now also implements `ITenantScope` with `ForceCrossTenant`; `PostgresFixture.CreateContext` runs with no company in a cross-tenant section by default (like the seeder) and takes `crossTenant: false` to test the fail-closed path. `UserAssignmentTests.Ac15` last case now expects `TenantViolationException(NoTenant)` for an empty `CompanyId` (the guard now stops it before the database; the earlier expectation was a database FK error). AC-18 uses a throwaway counter (Counter 2 has dependants, so a raw delete would hit a foreign key).
