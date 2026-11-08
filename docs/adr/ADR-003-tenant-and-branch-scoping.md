# ADR-003 — Tenant (company) and branch scoping

Status: Proposed
Date: 2026-11-07
Story: STORY-002, STORY-003

## Context
The spec says all data is filtered by company id and every endpoint checks role and branch scope. It does not say how.

## Decision
1. **Current context:** `ICurrentUser` (from JWT claims: `sub`, `company_id`, roles) and `ICurrentDevice` (from `X-Device-Key` → `device` → branch → company) are scoped services in `Qms.Application`, implemented in `Qms.Api`.
2. **Company filter:** every tenant entity implements `ITenantEntity { CompanyId }`. `QmsDbContext` applies a global query filter `CompanyId == currentCompanyId` and sets `CompanyId` on insert. A save that writes another company's row throws.
3. **Super Admin:** has no `company_id` claim. Only handlers in the `Companies` feature, marked `[CrossTenant]`, call `IgnoreQueryFilters()`.
4. **Branch scope:** Branch Manager / Supervisor and Counter Staff carry their branch ids (`user_branch`) and counter ids (`user_counter`), loaded per request and cached for the request. Handlers that take a `branchId`, or reach one through a zone, counter or token, call `IBranchScope.EnsureAccess(branchId)`, which returns `Forbidden`. Company Admin passes for every branch in the company.
5. **Devices:** device endpoints act only on their own branch and their mapped zones and services.
6. **Tests:** an integration test per protected endpoint proves a user from another company or branch gets 403/404.

## Consequences
- Forgetting a filter is hard: it is on by default and the escape hatch is explicit and greppable (`[CrossTenant]`).
- Branch checks are explicit in handlers, so the code reviewer can see them (code-review skill, item 3).

## Spec impact
None (implements sections 4, 8 and 11).

## Amendment 1 — Explicit cross-tenant writes, user assignment consistency, error mapping (2026-11-08, STORY-003)
Follow-ups S2, S3, S5 and N7 from the STORY-002 code reviews. This refines decisions 1–3 above; it does not replace them.

### A. Tenant writes fail closed (S2)
1. `ICurrentTenant` (Qms.Application) gains `bool IsCrossTenant`. A new scoped `ITenantScope` (Qms.Application) has one method, `IDisposable BeginCrossTenant()`. Disposing the handle ends the cross-tenant section (nesting counts, so an inner dispose does not end an outer section).
2. `QmsDbContext.SaveChanges` rules, in this order:
   - A tenant row never changes company (unchanged, always checked).
   - **Current company set:** stamp new rows and refuse rows of another company (unchanged).
   - **No company and `IsCrossTenant`:** nothing is stamped and nothing is tenant-checked. A new tenant row must carry its `CompanyId` explicitly; `Guid.Empty` throws.
   - **No company and not cross-tenant:** any Added, Modified or Deleted `ITenantEntity` or `Company` row throws `TenantViolationException` (reason `NoTenant`). Before this amendment the save went through unchecked.
   Non-tenant tables (`asp_net_*`, `refresh_token`) are not affected, so login, refresh, logout and lockout need no cross-tenant section.
3. Reads are unchanged. The query filter still uses the company only; cross-tenant code reads with `IgnoreQueryFilters()`. `IsCrossTenant` does **not** switch the filter off, so every unfiltered read stays visible in the code.
4. Who may open a cross-tenant section:
   - the development seeder (`MigrateAndSeedAsync` wraps `DevSeeder.SeedAsync`);
   - background jobs (ADR-005), around each run;
   - handlers marked `[CrossTenant]`. A `CrossTenantDecorator` (ADR-002 decorator chain, after validation) checks that the caller has the `SuperAdmin` role and returns `Forbidden` (`auth.forbidden`) otherwise. Only then does it open the section for the duration of the handler.
   No other code calls `BeginCrossTenant()`. The code reviewer greps for it.
5. One scoped implementation in Qms.Api provides both `ICurrentTenant` and `ITenantScope`. `CompanyId` comes from the `company_id` claim of the authenticated user (`ICurrentUser`); STORY-015 adds the device's company. It is registered in the authentication setup (`AddQmsAuthentication`), not in `AddQmsHealthChecks` (N7). `NoCompanyCurrentTenant` is deleted. Outside an HTTP request (seeder, jobs) there are no claims, so `CompanyId` is null.

### B. User assignments cannot cross companies (S3)
1. **Database, not only Application.** `user_branch` and `user_counter` are the source of the branch scope (decision 4). They are also the only branch-level tables without a same-company key, and the query filter does not cover them. A wrong row would make `IBranchScope` pass, so they get the same database guarantee as every other branch-level table (spec §9: "tenant tables carry company_id … so no row can reference another company's data"):
   - `user_branch.company_id` and `user_counter.company_id` (uuid, not null). Both become `ITenantEntity`, so they get the tenant filter, stamping, the immutable `company_id` and the concurrency token.
   - FK `(company_id, branch_id)` → `branch (company_id, id)`. FK `(company_id, counter_id)` → `counter (company_id, id)`, using a new alternate key on `counter`.
   - FK `(user_id, company_id)` → `asp_net_users (id, company_id)`, using a new unique constraint `ux_asp_net_users_id_company_id`. PostgreSQL allows a unique constraint on a nullable column, and the referencing `company_id` is never null. So only a user of the same company matches, and a Super Admin (no company) can never get an assignment. Super Admin and Company Admin need none: their scope is all companies, or every branch of their company (decision 4, spec §8).
   - EF Core cannot model an alternate key on the nullable `AppUser.CompanyId`. So this unique constraint and this FK are written as raw SQL in the migration, with fixed names (`fk_user_branch_user_company`, `fk_user_counter_user_company`, cascade on delete like the existing user FK). An integration test proves they exist and reject a cross-company row. Because they are not in the EF model, the model-drift test is not affected.
   - Side effect, intended: a user's `company_id` cannot change while the user still has branch or counter assignments. STORY-011 must remove them first.
2. **Application** still validates assignments for friendly errors. STORY-011 is the first story that writes them; the database is the backstop.
3. **Actor columns** (`token.issued_by_user_id`, `token.fee_settled_by_user_id`, `token_visit.user_id`, `counter.current_user_id`) stay single-column FKs, enforced in Application:
   - Handlers set them only from `ICurrentUser.UserId`, never from a request body. That user's company is the current tenant by construction (A.5).
   - When a user id does come from input (assignments, report filters, supervisor actions), the handler checks it with `IUserDirectory.IsInCompanyAsync(userId, companyId)`. If it fails, the handler returns `NotFound` `user.not_found`, which does not reveal whether the user exists elsewhere.
   - No composite FK here, for two reasons. These columns record who acted, and the value comes from the token, not from client input. And a database rule would write "Super Admin never acts on a token" into the schema, although spec §8 only implies it.

### C. Exception mapping (S5)
`GlobalExceptionHandler` maps:
- `DbUpdateConcurrencyException` → 409 `request.conflict`. A 0-row update or delete can be a concurrent change or a forged detached row; neither is a success. A handler that wants 404 re-reads through the filtered DbSet itself.
- `TenantViolationException`:
  - reason `OtherCompany` or `CompanyChanged` → 404 `resource.not_found`. This does not confirm that the row exists, and it is logged at Warning as a security event without entity values.
  - reason `NoTenant` → 500 `server.unexpected_error`, logged at Error. It means the server is set up wrong, not that the client made a mistake.

`TenantViolationException` gets a `Reason` property for this. Each mapping has an integration test.

### Consequences (amendment)
- A forgotten tenant context can no longer write silently; it fails loudly.
- The escape hatch is still explicit and greppable: `[CrossTenant]`, `BeginCrossTenant()`, `IgnoreQueryFilters()`.
- Tests that write through a context with no company must open a cross-tenant section, or set `IsCrossTenant` on `SettableCurrentTenant`.
- One raw-SQL constraint pair sits outside the EF model; the migration and an integration test document it.

### Spec impact (amendment)
Section 9 lists `user_branch (user_id, branch_id)` and `user_counter (user_id, counter_id)`. Add `company_id` to both. This follows the section 9 rule for tenant tables and changes no business rule. Needs the product owner's approval before the stage 2 migration is merged, as with the earlier §9 additions.
