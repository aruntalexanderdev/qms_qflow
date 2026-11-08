# TASK-008 — Fail-closed tenant writes, claims-based tenant context, user directory, error mapping

| Field | Value |
|---|---|
| ID | TASK-008 |
| Story | STORY-003 (scope amendment 2026-11-08) |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | To do |
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
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] Bilingual text added (en + ar) if user-facing — error codes only; texts in the web app
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed (ADR-003 actor-column rule)
- [ ] Handoff written in the story's Stage log

## 6. Tests
- AC-9 to AC-12, AC-17 to AC-22 as unit and/or integration tests; AC-13 to AC-16 and AC-23 constraint behaviour re-confirmed through EF.

## 7. Notes / result
