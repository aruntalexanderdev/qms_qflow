# STORY-003 — Sign-in: Identity, JWT, refresh token rotation, roles and seeded admin

| Field | Value |
|---|---|
| ID | STORY-003 |
| Status | Backend |
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

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-004, ADR-003 |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | All tables are created in STORY-002 |
| 3 Backend | Yes | TASK-004 | |
| 4 Frontend | N/A | — | Login page is STORY-004 |
| 5 Android | N/A | — | Devices use keys (M3) |
| 6 Code review | Yes | — | Security focus |
| 7 QA | Yes | — | |

## 9. Non-functional notes
- Security review focus: no user enumeration, constant-time compare of token hashes, the signing key at least 256 bits, no tokens in logs.
- `/auth/login` answers within 300 ms at p95 under normal load (excluding lockout delay).

## 10. Open questions
- [ ] Depends on ADR-004 (Proposed), accepted with the Phase 0 PR.

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
