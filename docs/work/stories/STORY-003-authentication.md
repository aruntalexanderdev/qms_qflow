# STORY-003 — Sign-in: Identity, JWT, refresh token rotation, roles and seeded admin

| Field | Value |
|---|---|
| ID | STORY-003 |
| Status | Analysis |
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

**Out of scope**
- User management CRUD (STORY-011). Device keys (STORY-015). Active Directory / LDAP (later).

## 4. Business rules
1. The access token is valid for 15 minutes; the refresh token is rotated on every use (spec 11).
2. Passwords are hashed by Identity (spec 11).
3. Roles and scopes are as in spec section 8.

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
| 1 BA | Yes | — | Add ACs for S2/S3/S5/N7. Create the stage 2 task (suggested TASK-007). For S2, S3 (Application side), S5 and N7, extend TASK-004 or add a stage 3 task (suggested TASK-008) |
| 2 Database | Yes (amended 2026-11-08) | to be created by BA (TASK-007) | Additive migration: `company_id` and composite FKs on `user_branch` / `user_counter`; `counter (company_id, id)` alternate key; raw-SQL `asp_net_users (id, company_id)` key and user FKs. Entities become `ITenantEntity`. DB tests. Must run before stage 3 |
| 3 Backend | Yes | TASK-004 (+ TASK-008, or TASK-004 extended) | Auth as before. Also: fail-closed tenant writes, `ITenantScope` / `[CrossTenant]`, claims-based tenant context (N7), `IUserDirectory`, exception mapping with integration tests |
| 4 Frontend | N/A | — | Login page is STORY-004; the amendment does not change the auth contract or error codes |
| 5 Android | N/A | — | Devices use keys (M3); no device API change |
| 6 Code review | Yes | — | Security focus: grep `BeginCrossTenant`, `[CrossTenant]`, `IgnoreQueryFilters`; check the raw-SQL constraints in the migration |
| 7 QA | Yes | — | Includes the negative cases for cross-company assignments and tenant writes |

## 9. Non-functional notes
- Security review focus: no user enumeration, constant-time compare of token hashes, the signing key at least 256 bits, no tokens in logs.
- `/auth/login` answers within 300 ms at p95 under normal load (excluding lockout delay).

## 10. Open questions
- [ ] Depends on ADR-004 (Proposed), accepted with the Phase 0 PR.
- [ ] Product owner: approve adding `company_id` to `user_branch` and `user_counter` in spec section 9 (ADR-003 Amendment 1, spec impact). This follows the section 9 tenant-table rule. It does not block analysis, but it must be approved before the stage 2 migration is merged.

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
