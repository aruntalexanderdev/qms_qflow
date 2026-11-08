# STORY-011 — Users, roles, branch and counter assignment (+ STORY-003 / STORY-004 follow-ups)

| Field | Value |
|---|---|
| ID | STORY-011 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 2 (Auth, Web i18n "language per user"), 5.1 step 10, 8 (roles, `/admin/users`, `/login` language), 9 (Identity user columns, `user_branch`, `user_counter`), 10 (`/users`), 11 (Security, Audit), 12 (M2), 13 (Languages per user) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **to create and maintain my company's users, give each one role, and assign Branch Managers to branches and Counter Staff to branches and counters**, so that **every person signs in with exactly the access their job needs**.

## 2. Background
Spec §5.1 step 10. Users are ASP.NET Identity rows (`asp_net_users`, `AppUser` in Infrastructure) with `company_id`, `display_name`, `language`, `is_active`; assignments are `user_branch` / `user_counter`, which feed `IBranchScope` (ADR-003 decision 4) and carry `company_id` with database guarantees (ADR-003 Amendment 1 B). This is the first story that writes users and assignments.

Follow-ups folded in (from earlier stage logs):
- **STORY-003 stage 3 / TASK-004**: password policy is still the Identity default — decide and configure it here.
- **STORY-003 review N9 / TASK-008**: `asp_net_users` is not an `ITenantEntity`, so the tenant guard does not protect it — every user query and write must filter by the current company in the handler.
- **STORY-003 stage 3 / TASK-008**: map residual SQLSTATE 23503 on `fk_user_branch_user_company`, `fk_user_counter_user_company` (→ 404 `user.not_found`) and `fk_user_branch_branch_company_id_branch_id`, `fk_user_counter_counter_company_id_counter_id` (→ 404 `resource.not_found`). The catalog entry is built in STORY-005 (ADR-008 §7); this story proves it through a real endpoint.
- **STORY-003 ADR-003 B.3**: request-supplied user ids are checked with `IUserDirectory.IsInCompanyAsync`; actor columns only from `ICurrentUser.UserId`.
- **STORY-003 stage 3 follow-up**: a deactivated user's access token lives up to 15 minutes — deactivation revokes the user's refresh-token families so the session ends at the next refresh.
- **STORY-004 / ADR-007 B.4**: endpoint for a signed-in user to save their own language.

Depends on STORY-005 (plumbing), STORY-007 (branches) and STORY-009 (counters).

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (Users screen is M4 STORY-026; the shell language switch that calls the new endpoint is a frontend follow-up, see section 8.)

## 7. Data and API impact
**Spec:** sections 2, 8, 9, 10, 11, 13. No business rule changes. Password policy is a security setting (open question 1).

**Database**
- Tables: `asp_net_users` (company_id, display_name, language `ck_asp_net_users_language`, is_active, user_name unique — Identity normalises user names **globally**, across companies), `asp_net_user_roles`, `user_branch`, `user_counter` (tenant rows; composite and raw-SQL FKs from `UserAssignmentCompany`), `refresh_token` (revoke on deactivate / password reset), `audit_log`.
- **Schema change: none.** Password policy and Identity options are configuration in `Qms.Infrastructure/DependencyInjection.cs` (stage 3, as in STORY-003).

**Backend**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/v1/users` | CA | paged (ADR-008 §4): `?page&pageSize&search&role&branchId&isActive` → `{ items, page, pageSize, totalCount }`; **only the caller's company** |
| GET | `/api/v1/users/{id}` | CA | 404 `user.not_found` for unknown and other-company ids (same answer) |
| POST | `/api/v1/users` | CA | `{ userName, displayName, password, role, language, isActive, branchIds[], counterIds[] }` → 201 (no password in the response); 409 `user.user_name_taken` |
| PUT | `/api/v1/users/{id}` | CA | same without `userName`/`password`, plus `version` (Identity `ConcurrencyStamp`); replaces role and assignments |
| DELETE | `/api/v1/users/{id}` | CA | refused while referenced by token history (actor FKs restrict) → 409 `user.in_use` (deactivate instead); assignments and refresh tokens cascade |
| POST | `/api/v1/users/{id}/password` | CA | admin sets a new password `{ password }`; policy errors → 400 `user.password_weak` with Identity error codes; revokes the user's refresh tokens |
| PUT | `/api/v1/auth/me/language` | any signed-in user (incl. Super Admin) | `{ language: "en" \| "ar" }` → 204; `GET /auth/me` and the next refresh return it (ADR-007 B.4) |
- Rules for the BA to state:
  - Roles assignable: CompanyAdmin, BranchManager, CounterStaff. SuperAdmin → 400 `user.role_invalid`. One role per user (`Roles`, STORY-003).
  - `branchIds` only for BranchManager and CounterStaff; `counterIds` only for CounterStaff; Company Admin has none (scope = all branches, spec §8) → 400 `user.assignment_invalid`.
  - Branch / counter ids must be in the caller's company (filtered DbSet; else 404 `resource.not_found`; DB backstop catalog).
  - Default (open question 3): each assigned counter's branch must be one of the user's `branchIds` → 400 `user.counter_outside_branches`.
  - A Company Admin cannot deactivate, delete or change the role of their own account (default, open question 4) → 409 `user.cannot_change_self`.
  - Deactivation (`isActive = false`) revokes every refresh-token family of the user (session ends at the next refresh; the access token expires within 15 minutes, accepted in STORY-003).
  - Changing a user's company is never possible through this API (company comes from the caller).
- Ports: `IUserAdministration` (Application; Infrastructure implementation over `UserManager<AppUser>` and the same `QmsDbContext`, so Identity writes, assignment rows and the audit row share the ADR-008 §11 transaction). Language save extends `IUserAccounts` or a small `IUserPreferences` port.
- Audit: `user` create / update / delete / `password_reset` (password `[NotAudited]`). Language save is a personal preference, not a setup change, and is **not** audited (a Super Admin has no `company_id` for `audit_log`).
- SignalR / jobs: none.

**Web / Android / displays:** none in this story. The shell language switch keeps writing `localStorage` only (ADR-007 B.4) until the frontend calls `PUT /auth/me/language` — add that to M4 (STORY-023 or STORY-026) as a follow-up.

**Cross-cutting:** company scope enforced in every user handler by an explicit `company_id = current company` predicate (no query filter on `asp_net_users`); integration tests: CA-C2 cannot list, read, update, delete, reset the password of, or assign a Company 1 user (404), and assigning a Company 2 branch/counter to a Company 1 user gives 404; Super Admin and BM/CS get 403 on `/users`; passwords never logged or audited; password policy tests.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008; ADR-003 Amendment 1 B; ADR-007 B.4 |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | All user and assignment columns and constraints exist (STORY-002, STORY-003 `UserAssignmentCompany`); password policy is configuration, not schema |
| 3 Backend | Yes | BA creates | User CRUD, assignments, password policy and reset, language endpoint, 23503 mapping proof, tests |
| 4 Frontend | N/A | — | Users screen is M4 (STORY-026); calling the language endpoint from the shell switch is an M4 follow-up (BACKLOG note) |
| 5 Android | N/A | — | No device API change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] **Product owner — password policy** (STORY-003 follow-up). Today: Identity default (6 characters, upper, lower, digit, symbol). Proposal for a bank: at least 12 characters with upper case, lower case and a digit; symbols optional; no forced expiry; lockout unchanged (5 / 15 min, ADR-004). Stage 3 applies the proposal unless the product owner decides otherwise before stage 3. Not blocking analysis.
- [ ] **Product owner — who creates Company Admins of a new company** (see STORY-005 question on companies). This story lets a Company Admin manage users of their own company only; Super Admin user management needs the companies story. Not blocking.
- [ ] Product owner — counter assignments must lie inside the user's assigned branches (default above). Confirm. Not blocking.
- [ ] Product owner — self-protection (a Company Admin cannot deactivate, delete or demote themselves, so a company always keeps an admin). Confirm. Not blocking.
- [ ] Note: user names are unique across all companies (Identity normalised user name). A bank deployment has one company in practice; confirm this is acceptable. Not blocking.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends on STORY-005 (plumbing, catalog), STORY-007 (branches) and STORY-009 (counters, `GET /branches/{id}/counters`).
- Follow-ups folded in: password policy; user-table company scope (handler predicates, tests); assignment 23503 mapping proven through `/users`; `IUserDirectory` for request-supplied user ids; refresh revocation on deactivate; language-save endpoint (ADR-007 B.4).
- `asp_net_users` writes bypass the tenant guard: the reviewer will grep every user query for the company predicate.
- The ADR-008 audit decorator transaction must enclose the `UserManager` calls (same scoped `QmsDbContext`).
Open questions: password policy (default applies), Company Admin provisioning, counter-inside-branches rule, self-protection, global user names; none blocks analysis.
