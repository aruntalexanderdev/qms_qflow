# STORY-003 — Sign-in: Identity, JWT, refresh token rotation, roles and seeded admin

| Field | Value |
|---|---|
| ID | STORY-003 |
| Status | Database |
| Priority | Must |
| Milestone | M1 |
| Spec sections | 2 (Auth), 8 (roles), 10 (`/auth/*`), 11 (Security) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-07 |

## 1. User story
As a **Company Admin**,
I want **to sign in with my username and password and stay signed in securely**,
so that **I can use the admin screens and every API call knows who I am, my company and my role**.

## 2. Background
Depends on STORY-001 and STORY-002 (Identity tables, `refresh_token`, `user_branch`, `user_counter`). Token design: ADR-004. Claims feed the tenant and branch scoping from ADR-003.

## 3. Scope
**In scope**
- ASP.NET Core Identity on `QmsDbContext` (user with `company_id`, display name, preferred language).
- Roles: `SuperAdmin`, `CompanyAdmin`, `BranchManager` (Branch Manager / Supervisor), `CounterStaff`; seeded.
- Seeded users (Development and test only): one Super Admin, and one Company Admin for Company 1. Usernames and passwords come from configuration (user-secrets or `.env`), never from Git.
- `POST /api/v1/auth/login` → access token (15 min) in the body, refresh cookie (ADR-004).
- `POST /api/v1/auth/refresh` → rotate the refresh token, new access token; reuse detection revokes the family.
- `POST /api/v1/auth/logout` → revoke the family, clear the cookie.
- `GET /api/v1/auth/me` → id, name, role, company, branches, counters, language.
- JWT bearer authentication; `[Authorize]` by default (fallback policy), with explicit `[AllowAnonymous]` on login, refresh and health.
- Lockout (5 failures → 15 min) and a rate limiter on login.
- `ICurrentUser` implementation (ADR-003) from claims.
- Swagger: bearer security scheme so authorised endpoints can be tried.

**In scope — amendment 2026-11-08 (STORY-002 review follow-ups, ADR-003 Amendment 1)**
- S2: tenant writes fail closed when there is no company; explicit cross-tenant scope (`ITenantScope`, `[CrossTenant]`, `CrossTenantDecorator`) restricted to Super Admin; the seeder runs in a cross-tenant section.
- S3: `user_branch` and `user_counter` carry `company_id` with composite FKs, so a cross-company or Super Admin assignment is rejected by the database (new additive migration); `IUserDirectory` validates user ids that come from request input.
- S5: `GlobalExceptionHandler` maps `DbUpdateConcurrencyException` and `TenantViolationException` to problem details.
- N7: one claims-based `RequestTenantContext` provides `ICurrentTenant` and `ITenantScope`, registered in `AddQmsAuthentication`.

**Out of scope**
- User management CRUD (STORY-011). Device keys (STORY-015). Active Directory / LDAP (later).
- Production endpoints that write tenant data or assignments (STORY-011 and later); the amendment is proven with endpoints registered only in the test host.
- Composite FKs on the actor-user columns (`token.issued_by_user_id`, `token.fee_settled_by_user_id`, `token_visit.user_id`, `counter.current_user_id`); they stay Application-only (ADR-003 Amendment 1 B.3).
- Web, Android and displays: no change.

## 4. Business rules
1. The access token is valid for 15 minutes; the refresh token is rotated on every use (spec 11).
2. Passwords are hashed by Identity (spec 11).
3. Roles and scopes are as in spec section 8.

Amendment 2026-11-08 (no new business rules; these implement spec 8, 9 and 11 and ADR-003 Amendment 1):

4. Tenant tables carry `company_id` so no row can reference another company's data (spec 9). This now includes `user_branch` and `user_counter` (listing `company_id` in spec 9 awaits product owner approval, section 10).
5. A Company Admin sees only their own company; Branch Manager and Counter Staff only their assigned branches or counters; Super Admin is not tied to a company and has no branch or counter assignments (spec 8). Assignments therefore only exist between a user and a branch or counter of the user's own company (spec 8, 9).
6. A write to tenant data needs a current company, or an explicit cross-tenant section opened only by the seeder, a background job, or a handler marked `[CrossTenant]` that only a Super Admin may call (spec 8, 11; ADR-003 A.4).
7. Actor columns (who issued, settled, served) are set only from the signed-in user, never from the request body. A user id that does come from the request must belong to the current company, otherwise the response is `user.not_found`, which does not reveal whether the user exists in another company (spec 11; ADR-003 B.3).
8. Error responses do not confirm that another company's data exists (404 `resource.not_found`) and never expose internal details on server misconfiguration (500 `server.unexpected_error`) (spec 11, RFC 7807 per project rules).

## 5. Acceptance criteria

**AC-1: Successful sign-in**
- Given the seeded Company Admin of Company 1
- When `POST /api/v1/auth/login` is called with the correct username and password
- Then the response is 200 with an access token that expires in 15 minutes and contains `sub`, `company_id`, role `CompanyAdmin` and `lang`; and a `Set-Cookie` refresh cookie with `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/v1/auth`.

**AC-2: Wrong password**
- Given the seeded Company Admin
- When login is called with a wrong password
- Then the response is 401 problem details with code `auth.invalid_credentials`, the same response as for an unknown username.

**AC-3: Lockout**
- Given 5 failed logins in a row for one user
- When the 6th attempt uses the correct password within 15 minutes
- Then the response is 401 with code `auth.locked_out`.

**AC-4: Refresh rotation**
- Given a signed-in user with refresh cookie R1
- When `POST /api/v1/auth/refresh` is called with R1
- Then a new access token and a new cookie R2 are returned, and R1 is marked replaced.

**AC-5: Reuse detection**
- Given R1 has already been rotated to R2
- When refresh is called again with R1
- Then the response is 401 with code `auth.refresh_reused`, and R2 no longer works either.

**AC-6: Protected by default**
- Given no access token, or an expired one
- When `GET /api/v1/auth/me` is called
- Then the response is 401; with a valid token it returns 200 with the user's company, role and language.

**AC-7: Logout**
- Given a signed-in user
- When `POST /api/v1/auth/logout` is called
- Then the refresh cookie is cleared, and refreshing with the old cookie returns 401.

**AC-8: Swagger**
- Given the API runs in Development
- When Swagger UI is opened
- Then it lists `/auth/login`, `/auth/refresh`, `/auth/logout` and `/auth/me`, and supports the bearer scheme (M1 done-when, spec 12).

Wording check for the amendment: AC-1 to AC-8 are unchanged; no fix needed.

### Amendment 2026-11-08 — S2, S3, S5, N7
Test hosts for AC-9 to AC-22 register test-only endpoints or call the use cases / DbContext directly; STORY-003 adds no production tenant-write endpoint.

**AC-9: Tenant write with no company is rejected (S2)**
- Given a request with no `company_id` (no company in the current tenant) and no cross-tenant section
- When a handler saves a new `branch` row, or modifies or deletes an existing `counter` row
- Then `SaveChanges` throws `TenantViolationException` with reason `NoTenant`, nothing is stored, and the same save for `asp_net_users`, `refresh_token` (login, refresh, logout, lockout) still succeeds.

**AC-10: Seeder and cross-tenant section still work (S2)**
- Given an empty database and no signed-in user
- When `MigrateAndSeedAsync` runs the development seeder
- Then Company 1 with Branch 1, Zones 1 and 2, Counters 1 to 4 and the example mappings are created; a second run changes nothing; and a new tenant row inside a cross-tenant section with `CompanyId = Guid.Empty` is rejected.

**AC-11: `[CrossTenant]` handler refuses a non-Super Admin (S2)**
- Given the seeded Company Admin of Company 1 is signed in
- When a handler marked `[CrossTenant]` is called
- Then the result is `Forbidden` (HTTP 403, code `auth.forbidden`), the handler body does not run, and no cross-tenant section is opened.

**AC-12: `[CrossTenant]` handler works for Super Admin (S2)**
- Given the seeded Super Admin is signed in (no company)
- When a handler marked `[CrossTenant]` creates a branch row for Company 2 with an explicit `CompanyId`
- Then the decorator opens the cross-tenant section, the row is saved with Company 2, and after the handler returns the section is closed (a following tenant write in the same scope fails as in AC-9). In a unit test of `ITenantScope`, disposing an inner handle does not end the outer section.

**AC-13: Company 2 user cannot be assigned to Branch 1 (S3)**
- Given a user of Company 2 and Branch 1 of Company 1
- When a `user_branch` row for that user and Branch 1 is inserted (with `company_id` of Company 1 or Company 2)
- Then the database rejects it: with Company 1 by `fk_user_branch_user_company`, with Company 2 by the composite FK `(company_id, branch_id)` to `branch`.

**AC-14: Company 2 user cannot be assigned to Counter 2 (S3)**
- Given a user of Company 2 and Counter 2 (Zone 1, Branch 1) of Company 1
- When a `user_counter` row for that user and Counter 2 is inserted (with `company_id` of either company)
- Then the database rejects it: with Company 1 by `fk_user_counter_user_company`, with Company 2 by the composite FK `(company_id, counter_id)` to `counter`.

**AC-15: Super Admin cannot be assigned (S3)**
- Given the seeded Super Admin (no company)
- When a `user_branch` row for the Super Admin and Branch 1, or a `user_counter` row for the Super Admin and Counter 2, is inserted
- Then the database rejects both rows (no `asp_net_users (id, company_id)` match), and the seeder created no assignments for the Super Admin or the Company Admin.

**AC-16: Same-company assignment is accepted (S3)**
- Given a Counter Staff user of Company 1, Branch 1 and Counter 2
- When `user_branch` (Branch 1) and `user_counter` (Counter 2) rows are inserted with `company_id` of Company 1
- Then both are saved; and changing that user's `company_id` to Company 2 while the rows exist is rejected by the database.

**AC-17: Request-supplied user id of another company (S3)**
- Given a signed-in Company Admin of Company 1 and a user of Company 2
- When a handler receives that user's id from the request and checks it with `IUserDirectory.IsInCompanyAsync(userId, Company 1)`
- Then the check returns false and the handler returns `NotFound` (HTTP 404, code `user.not_found`) with the same response as for a user id that does not exist; a Company 1 user id passes.

**AC-18: Concurrency conflict returns 409 (S5)**
- Given a test-host endpoint that updates Counter 2 and another request changed the same row first
- When the save raises `DbUpdateConcurrencyException`
- Then the response is 409 problem details with code `request.conflict`.

**AC-19: Cross-company access returns 404 (S5)**
- Given a signed-in Company Admin of Company 1 and a test-host endpoint that updates a Company 2 row, or changes the `company_id` of a Company 1 row
- When the save raises `TenantViolationException` with reason `OtherCompany` or `CompanyChanged`
- Then the response is 404 problem details with code `resource.not_found`, the body contains no entity values, and a Warning is logged without entity values.

**AC-20: Missing tenant returns 500 without details (S5)**
- Given a test-host endpoint that writes a tenant row with no company and no cross-tenant section
- When the save raises `TenantViolationException` with reason `NoTenant`
- Then the response is 500 problem details with code `server.unexpected_error`, the body contains no exception type, message, stack trace or entity values, and an Error is logged.

**AC-21: Tenant context comes from the sign-in claims (N7)**
- Given the seeded Company Admin of Company 1 signed in, and rows exist for Company 1 and Company 2
- When a test-host endpoint lists branches
- Then only Company 1 rows are returned; the same call with a Company 2 token returns only Company 2 rows; the tenant is registered outside `AddQmsHealthChecks` and `NoCompanyCurrentTenant` no longer exists.

**AC-22: Super Admin sees nothing tenant-scoped by default (N7)**
- Given the seeded Super Admin is signed in (no `company_id` claim)
- When a test-host endpoint lists branches without a cross-tenant read, and then through a `[CrossTenant]` handler that reads with `IgnoreQueryFilters()`
- Then the first call returns an empty list and the second returns the branches of Company 1 and Company 2.

**AC-23: Migration is additive and reversible (S3)**
- Given a database at `InitialSchema` with Company 1 data, and with one `user_branch` row whose user belongs to another company
- When the new migration `UserAssignmentCompany` is applied
- Then for same-company rows `company_id` is backfilled from `branch.company_id` / `counter.company_id`; with the mismatched row the migration fails and changes nothing; and on migrate down all added columns, keys and constraints are removed and `InitialSchema` data is intact.

## 6. UI / UX
No screen in this story; the login page is STORY-004. Error codes are translated by the web app.

## 7. Data and API impact
- Tables: Identity tables, `refresh_token`, `user_branch`, `user_counter` (all created in STORY-002); seeded roles and users.
- Endpoints: `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`, `GET /auth/me`.
- Configuration: `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey` (secret), `Seed:SuperAdmin:*`, `Seed:CompanyAdmin:*` (secrets).

**Scope amendment 2026-11-08 (STORY-002 review follow-ups S2, S3, S5, N7; design in ADR-003 Amendment 1)**
- Spec: sections 8, 9, 11. No business rule changes. Section 9 needs `company_id` listed on `user_branch` and `user_counter` (product owner approval, see Open questions).
- Database (S3), in a new additive migration, e.g. `UserAssignmentCompany`. `InitialSchema` is merged and must not be regenerated.
  - New columns `user_branch.company_id` and `user_counter.company_id`: uuid, not null.
    - Backfill from `branch.company_id` / `counter.company_id`.
    - The migration fails if an existing row's user belongs to another company.
    - Both entities become `ITenantEntity`: tenant filter, stamping, immutable `company_id`, concurrency token.
  - New alternate key `counter (company_id, id)`.
  - Composite FKs:
    - `user_branch (company_id, branch_id)` → `branch (company_id, id)`
    - `user_counter (company_id, counter_id)` → `counter (company_id, id)`
  - Raw SQL in the migration, because EF cannot model a key on the nullable `AppUser.CompanyId`:
    - unique `ux_asp_net_users_id_company_id` on `asp_net_users (id, company_id)`;
    - FKs `fk_user_branch_user_company` and `fk_user_counter_user_company` on `(user_id, company_id)` → `asp_net_users (id, company_id)`, on delete cascade.
    - Effect: a Super Admin (no company) can never have assignments, and a user's company cannot change while assignments exist.
  - The Down migration drops all of the above.
  - Tests:
    - a cross-company `user_branch` or `user_counter` row is rejected by each named constraint;
    - a Super Admin assignment is rejected;
    - a valid same-company row is accepted;
    - migrate down for this migration.
  - No change to the actor-user FKs (`token.issued_by_user_id`, `token.fee_settled_by_user_id`, `token_visit.user_id`, `counter.current_user_id`). They stay single-column (ADR-003 Amendment 1 B.3).
  - Seed: no new rows. The seeder creates no assignments; Super Admin and Company Admin need none.
- Backend:
  - S2 (fail-closed writes):
    - `ICurrentTenant.IsCrossTenant`.
    - New `ITenantScope.BeginCrossTenant()`, returning a nesting-safe `IDisposable`.
    - `QmsDbContext` throws `TenantViolationException` (reason `NoTenant`) on any tenant write when there is no company and no cross-tenant section.
    - `MigrateAndSeedAsync` wraps the seeder in a cross-tenant section.
    - `[CrossTenant]` attribute plus `CrossTenantDecorator`: the caller needs role `SuperAdmin`, otherwise `Forbidden` `auth.forbidden`. The decorator opens the section for the handler call.
    - `SettableCurrentTenant` and the existing persistence tests that write with no company are updated to use the flag.
  - N7:
    - One scoped `RequestTenantContext` in Qms.Api implements both `ICurrentTenant` and `ITenantScope`. Its `CompanyId` comes from `ICurrentUser` (the `company_id` claim).
    - It is registered in `AddQmsAuthentication`, and the registration is removed from `AddQmsHealthChecks`.
    - `NoCompanyCurrentTenant.cs` is deleted.
  - S3 (Application side):
    - `IUserDirectory.IsInCompanyAsync(userId, companyId)`: Application interface, Infrastructure implementation over `asp_net_users`. A failed check returns `NotFound` `user.not_found`.
    - Rule: actor columns are set only from `ICurrentUser.UserId`. It is documented in ADR-003, and STORY-011 and STORY-014 to STORY-020 must follow it.
    - Unit and integration tests.
  - S5 (exception mapping in `GlobalExceptionHandler`):
    - `DbUpdateConcurrencyException` → 409 `request.conflict`.
    - `TenantViolationException` with reason `OtherCompany` or `CompanyChanged` → 404 `resource.not_found` (Warning log, no entity values).
    - `TenantViolationException` with reason `NoTenant` → 500 `server.unexpected_error` (Error log).
    - One integration test per mapping. STORY-003 has no tenant-write endpoint, so the tests use endpoints registered only in the test host.
  - No new production endpoints and no SignalR events.
- Web: none. The auth contract is unchanged, and `request.conflict` and `resource.not_found` already exist as default codes.
- Android, displays: none.
- Cross-cutting:
  - Security: closes the fail-open write path and adds defence in depth for branch scope.
  - Performance: one extra FK lookup per assignment write; nothing on the token or Call Next path.
  - Audit: unchanged.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-004, ADR-003 (+ Amendment 1, 2026-11-08) |
| 1 BA | Yes | — | Done: AC-9 to AC-23 added; TASK-007 (stage 2) and TASK-008 (stage 3) created |
| 2 Database | Yes (amended 2026-11-08) | TASK-007 (AC-13 to AC-16, AC-23) | Additive migration: `company_id` and composite FKs on `user_branch` / `user_counter`; `counter (company_id, id)` alternate key; raw-SQL `asp_net_users (id, company_id)` key and user FKs. Entities become `ITenantEntity`. DB tests. Must run before stage 3 |
| 3 Backend | Yes | TASK-004 (AC-1 to AC-8) + TASK-008 (AC-9 to AC-12, AC-17 to AC-22; AC-13 to AC-16 and AC-23 depend on TASK-007) | Auth as before. Also: fail-closed tenant writes, `ITenantScope` / `[CrossTenant]`, claims-based tenant context (N7), `IUserDirectory`, exception mapping with integration tests |
| 4 Frontend | N/A | — | Login page is STORY-004; the amendment does not change the auth contract or error codes |
| 5 Android | N/A | — | Devices use keys (M3); no device API change |
| 6 Code review | Yes | — | Security focus: grep `BeginCrossTenant`, `[CrossTenant]`, `IgnoreQueryFilters`; check the raw-SQL constraints in the migration |
| 7 QA | Yes | — | Includes the negative cases for cross-company assignments and tenant writes |

## 9. Non-functional notes
- Security review focus: no user enumeration, constant-time compare of token hashes, the signing key at least 256 bits, no tokens in logs.
- `/auth/login` answers within 300 ms at p95 under normal load (excluding lockout delay).

## 10. Open questions
- [ ] Depends on ADR-004 (Proposed), accepted with the Phase 0 PR.
- [ ] Product owner: approve adding `company_id` to `user_branch` and `user_counter` in spec section 9 (ADR-003 Amendment 1, spec impact). This follows the section 9 tenant-table rule. It does not block analysis or starting stage 2, but it blocks merging the stage 2 migration (TASK-007) until approved.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-07
Result: Done
Changed: docs/adr/ADR-004-auth-tokens.md, this story (sections 7–8)
Notes for next stage: Stage 2 N/A because STORY-002 creates every auth table.
Open questions: none

### Stage 1 — Analysis — business-analyst — 2026-11-07
Result: Done
Changed: this story (sections 1–6, 9–10), docs/work/tasks/TASK-004-authentication-api.md
Notes for next stage: Start at stage 3 (TASK-004) once STORY-002 is Done.
Open questions: none

### Stage 0 — Architecture review (scope amendment) — project-architect — 2026-11-08
Result: Done
Changed: docs/adr/ADR-003-tenant-and-branch-scoping.md (Amendment 1), this story (status, sections 7, 8, 10)
Notes for next stage:
- Scope grows by the STORY-002 review follow-ups S2, S3, S5 and N7. The design is in ADR-003 Amendment 1; the impact is in section 7.
- S3 decision: a database guarantee on `user_branch` / `user_counter`, with Application validation on top.
  - Database: new `company_id` with composite FKs to `branch` / `counter`, plus a raw-SQL `(user_id, company_id)` FK to `asp_net_users (id, company_id)`.
  - Why: these rows feed `IBranchScope`, the query filter does not cover them today, and they are the only branch-level tables without a same-company key.
  - Super Admin is not affected: a Super Admin needs no assignments.
  - The four actor-user FKs stay Application-only: values come from `ICurrentUser.UserId`, and `IUserDirectory` checks user ids that come from input.
- Stage 2 now applies, as a new additive migration (`InitialSchema` is merged).
- Order is stage 2, then stage 3. Stage 3 owns:
  - S2: the `QmsDbContext` write guard, `ITenantScope`, the `[CrossTenant]` decorator, the seeder wrap and the test-fixture updates;
  - N7;
  - S5.
- BA: write Given/When/Then ACs for these cases, then create the stage 2 task and the stage 3 task (or extend TASK-004):
  - a tenant write with no company fails;
  - the seeder still seeds;
  - `[CrossTenant]` refuses a caller who is not a Super Admin;
  - cross-company `user_branch` / `user_counter` rows are rejected;
  - a Super Admin cannot be assigned;
  - 409 for a concurrency conflict;
  - 404 for a tenant violation;
  - 500 for a missing tenant;
  - `ICurrentTenant` comes from the `company_id` claim and is registered outside the health checks.
- Stages 4 and 5 stay N/A.
Open questions: the product owner must approve listing `user_branch.company_id` / `user_counter.company_id` in spec section 9. Not blocking for analysis; needed before the stage 2 merge.

### Stage 1 — Analysis (scope amendment) — business-analyst — 2026-11-08
Result: Done
Changed: this story (status, sections 3, 4, 5 AC-9 to AC-23, 8, 10), docs/work/tasks/TASK-007-user-assignment-company-migration.md, docs/work/tasks/TASK-008-tenant-guard-and-error-mapping.md
Notes for next stage:
- Stage 2 (TASK-007): AC-13 to AC-16 and AC-23. Start now; do not merge until the product owner approves `company_id` on `user_branch` / `user_counter` in spec section 9.
- Stage 3 (TASK-008) follows TASK-007; TASK-004 (AC-1 to AC-8) is unchanged.
- AC-1 to AC-8 unchanged. AC-9 to AC-22 use test-host-only endpoints or direct use-case calls; no production tenant-write endpoint exists in this story.
- Stages 4 and 5 stay N/A.
Open questions: product owner approval of spec section 9 (blocks the TASK-007 merge only).
