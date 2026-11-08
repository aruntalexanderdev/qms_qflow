# ADR-009 — Configurable roles and permissions, per-company password policy

Status: Accepted (product owner decision, Alexa, 2026-11-08)
Date: 2026-11-08
Story: STORY-048 (roles and permissions), STORY-049 (password policy), STORY-047 (companies); affects STORY-005 to STORY-013 and every later story with an endpoint

## Context
Spec §8 had a fixed role matrix: four roles, each with a fixed list of screens. ADR-008 §9 turned it into a per-endpoint role table. On 2026-11-08 the product owner decided:
- Roles are managed **per company** by the Company Admin. A role grants **permissions per module (page or API area) and action**: view, create, edit, delete, print, and feature operations such as approvals. The system defines which actions each module has.
- Company Admin, Branch Manager / Supervisor and Counter Staff are **seeded per company as editable defaults** with today's §8 rights. **Super Admin stays a fixed system role** (cross-company: companies and global settings only).
- Branch and counter scope (`user_branch`, `user_counter`, `IBranchScope`, ADR-003 decision 4) still applies on top of permissions.
- The password policy is configurable per company by the Company Admin (defaults 12 characters, upper + lower + digit, no expiry).
- Smaller rules: a Company Admin cannot demote, deactivate or delete their own account; usernames are unique across all companies.

STORY-005 stage 3 already protects its endpoints with `[RequirePermission("<module>.<action>")]`, resolved by `IPermissionResolver`, backed for now by a static role → permission map (`RolePermissionMap`) that reproduces ADR-008 §9. This ADR fixes the target design so STORY-048 only swaps the resolver and later stories add their modules.

## Decision

### 1. Two kinds of role
1. **System role — Super Admin.** Stays an ASP.NET Identity role (`asp_net_roles` / `asp_net_user_roles`, name `SuperAdmin`) and the `role` claim in the JWT. It is not stored in `role`, has no company and no permissions from the catalog. Super Admin endpoints (companies, later licences and global settings) check the role (`[RequireSuperAdmin]`, plus `[CrossTenant]` handlers, ADR-003 Amendment 1 A.4), never a permission. A company role can never grant cross-company rights.
2. **Company roles.** Rows of the new tenant table `role`, one set per company, managed by users holding the `roles.*` permissions. After the STORY-048 migration the Identity roles `CompanyAdmin`, `BranchManager` and `CounterStaff` no longer exist; `asp_net_roles` holds only `SuperAdmin`.

### 2. Data model (spec §9; built by STORY-048 stage 2)
| Table | Columns | Keys and rules |
|---|---|---|
| `role` | `company_id`, `name_en`, `name_ar`, `branch_scope` (`Company` / `Assigned`), `is_system`, `system_code` (`CompanyAdmin` / `BranchManager` / `CounterStaff`, null for custom roles), `is_active`, `sort_order`, `created_at`, `updated_at` | unique `(company_id, lower(name_en))`, unique `(company_id, name_ar)`; unique `(company_id, system_code)` where not null; check `is_system = (system_code is not null)`; alternate key `(company_id, id)` |
| `role_permission` | `company_id`, `role_id`, `permission` (text, catalog code) | unique `(role_id, permission)`; FK `(company_id, role_id)` → `role (company_id, id)`, cascade on role delete |
| `user_role` | `company_id`, `user_id`, `role_id` | unique `(user_id, role_id)`; FK `(company_id, role_id)` → `role (company_id, id)` (restrict); FK `(user_id, company_id)` → `asp_net_users (id, company_id)` as raw SQL with a fixed name (`fk_user_role_user_company`, cascade), exactly like `user_branch` (ADR-003 Amendment 1 B). A Super Admin (no company) can therefore never hold a company role |

- All three are `ITenantEntity` (tenant filter, stamping, `xmin` version).
- `role_permission.permission` is free text in the database. The catalog in code (decision 3) is the source of truth: the Application rejects unknown codes on save, and the resolver ignores codes that are no longer in the catalog. A migration that removes a permission from the catalog also deletes its rows.
- `branch_scope` replaces the hard-coded "Company Admin sees every branch": `Company` = every branch of the company, no assignments; `Assigned` = only the branches in `user_branch` (and counters in `user_counter`).

### 3. Permission catalog
- **Codes:** `<module>.<action>`, kebab-case, stable, never renamed (they are stored in `role_permission`). Example `service-types.edit`. This is the format STORY-005 uses.
- **Where:** constants in `Qms.Application/Auth/Permissions.cs` (STORY-005 starts it) plus a catalog description (module, its actions, display order, the stage-1 "view" action) in the same place. Each story that adds an endpoint adds its module and actions there. No catalog table.
- **Read-only endpoint:** `GET /api/v1/permissions` (any user holding `roles.view`) returns `[{ module, actions: [{ action, permission }] }]` in display order. Codes only: the web app translates `permissions.modules.<module>` and `permissions.actions.<action>` (en + ar), like error codes.
- **Actions:** `view`, `create`, `edit`, `delete`, `print`, and feature operations named after the operation (`approve`, `registration-code`, `reset-password`, later `cancel`, `reinstate`, `waive-fee`, `operate` …). The catalog says which actions each module has.
- **Rule:** any action of a module requires that module's `view` in the same role (400 `role.view_required`), so a role never edits what it cannot see.

**M2 catalog** (STORY-048 adds the modules not yet added by STORY-005; each M2 story uses the codes for its endpoints):

| Module | Actions | Endpoints |
|---|---|---|
| `service-types` | view, create, edit, delete | STORY-005 |
| `priority-types` | view, create, edit, delete | STORY-005 |
| `service-mapping` | view, edit | STORY-006 company service mapping |
| `branches` | view, create, edit, delete | STORY-007 branch details |
| `branch-hours` | view, edit | STORY-007 hours and calendar |
| `branch-settings` | view, edit | STORY-007 settings |
| `branch-fees` | view, edit | STORY-007 priority fee override |
| `branch-service-mapping` | view, edit | STORY-008 |
| `zones` | view, create, edit, delete | STORY-009 |
| `counters` | view, create, edit, delete | STORY-009 (incl. `GET /branches/{id}/counters`) |
| `counter-service-mapping` | view, edit | STORY-009 |
| `service-flows` | view, create, edit, delete | STORY-010 |
| `users` | view, create, edit, delete, reset-password | STORY-011 |
| `roles` | view, create, edit, delete | STORY-048 (incl. `GET /permissions`) |
| `password-policy` | view, edit | STORY-049 |
| `devices` | view, create, edit, delete, registration-code | STORY-012 device CRUD and code |
| `dispenser-settings` | view, edit | STORY-012 settings and zone + service mapping |
| `ticket-templates` | view, create, edit, delete | STORY-013 (preview = view) |

Planned for later milestones (names are guidance; the owning story adds them with their defaults): `counter-console.operate`, `tokens.issue`, `tokens.cancel`, `tokens.reinstate`, `tokens.settle-fee`, `tokens.change-priority`, `dashboard.view`, `reports.view`, `reports.print` (Excel export), `audit-log.view`.

### 4. Default roles (seeded per company; reproduce spec §8 and ADR-008 §9)
| Module | Company Admin (`Company` scope) | Branch Manager / Supervisor (`Assigned`) | Counter Staff (`Assigned`) |
|---|---|---|---|
| service-types, priority-types, service-mapping, ticket-templates | all actions | view | — |
| branches, branch-hours, branch-settings, branch-fees | all actions | view (own branches) | — |
| branch-service-mapping, zones, counters, counter-service-mapping, devices, dispenser-settings | all actions | all actions (own branches) | — |
| service-flows | all actions | view | — |
| users, roles, password-policy | all actions | — | — |

- Counter Staff has no setup permission in M2; M3/M5 add `counter-console.operate`, `tokens.issue` … to it.
- Defaults are editable. Open questions STORY-007 (Branch Manager edits hours/settings?) and STORY-010 (who edits flows?) are resolved by configuration: the defaults above stay; a company that wants it grants the permission to its Branch Manager role.
- Seeding: one Application component (`DefaultRoles`) creates the three roles with these grants. It runs when a company is created (STORY-047) and in the dev seeder. The STORY-048 migration inserts the same rows as explicit SQL for every existing company (a migration never calls changing application code).
- **New permissions later:** a story that adds a module also adds a migration granting its new codes to the system roles (by `system_code`) of every company, per its default column, and extends `DefaultRoles`. Edited roles get the new codes too; nobody could have removed a code that did not exist.
- System roles (`is_system`): name, permissions and scope are editable; they cannot be deleted or deactivated (409 `role.system`). Custom roles: full CRUD; delete only while no user holds them (409 `role.in_use`); deactivating is allowed (an inactive role grants nothing).

### 5. Users get one or more roles
- A company user holds **one or more** company roles (`user_role`); effective permissions = the **union** of the permissions of their **active** roles. At least one role is required when saving a user (400 `user.role_required`; STORY-011).
- Effective branch scope = `Company` if any active role has `Company` scope, else `Assigned`. Assignments (`branchIds`, `counterIds`) are accepted only for a user whose effective scope is `Assigned` (400 `user.assignment_invalid`); every assigned counter's branch must be one of the user's branches (400 `user.counter_outside_branches`, confirmed 2026-11-08).
- Permission and scope are both checked: `[RequirePermission]` on the endpoint (403 `auth.forbidden`), then `IBranchScope.EnsureAccessAsync` in the handler (ADR-008 §10, unchanged order: 404 then 403).

### 6. Resolution, JWT and `/auth/me`
- **The JWT carries no permissions and no company roles.** Claims stay `sub`, `company_id`, `name`, `lang`, and `role` only for a Super Admin. Reasons: about 70 permissions would add ~2 KB to every request and to the SignalR connection, and a token would keep stale rights for up to 15 minutes.
- **Server resolution per request.** `IPermissionResolver` (Application port, STORY-005) gets a database implementation in STORY-048: one indexed query per request (user active, company active, `user_role` → active `role` → `role_permission`, filtered to the catalog), memoised for the request, returning `EffectiveAccess(Permissions, BranchScope)`. `BranchScope` (Infrastructure) reads the scope from it instead of `IsInRole(CompanyAdmin)`. Cost is one small query (< 1 ms) per authenticated request, inside the 300 ms budget of token issue and Call Next. A cross-request cache with a company "access version" may be added only if the load test (STORY-042) needs it.
- **A permission change takes effect on the next API request** (immediately): role edits, role deactivation, user role changes and user deactivation. As a side effect, a deactivated user or a user of a deactivated company loses every permission at once, not only at the next refresh (the access token itself stays valid for endpoints without a permission, such as `/auth/me`, until it expires).
- **Web app.** Login, refresh and `GET /auth/me` return the `user` object with `isSuperAdmin`, `roles: [{ id, nameEn, nameAr }]`, `permissions: string[]` (sorted), `branchScope` (`Company` / `Assigned`; null for Super Admin), `branchIds`, `counterIds`. The single `role` field is removed. The web app builds menus and route guards from `permissions` / `isSuperAdmin` (never by decoding the JWT, ADR-007 B.5). It sees a change at the next refresh (≤ 15 min) or at once when it calls `GET /auth/me` — it does so after any 403 from the API. The UI is a convenience; the server is the authority.

### 7. Lock-out protection
- No user can change their own roles, deactivate or delete their own account through `/users` (409 `user.cannot_change_self`; confirmed 2026-11-08 for the Company Admin, applied to everyone).
- A role save may not remove `roles.edit` or `users.edit` from the caller's own effective permissions (409 `role.cannot_change_self`).
- Any role or user change that would leave the company without an active user holding both `roles.edit` and `users.edit` with `Company` scope is rejected (409 `role.last_admin`). The check runs inside the ADR-008 §11 transaction after locking the company row (`SELECT … FOR UPDATE`), so two admins cannot remove each other at once.
- The last resort is the Super Admin: `POST /companies/{id}/admin-users` (STORY-047) creates a user with the company's system role `CompanyAdmin`.

### 8. Password policy per company (STORY-049)
- New table `password_policy` (one row per company, unique `company_id`): `min_length` (8–128, default 12), `require_upper` (true), `require_lower` (true), `require_digit` (true), `require_symbol` (false), `expiry_days` (0 = never, 0–365, default 0), `history_count` (0 = off, 0–24, default 0), `lockout_threshold` (3–20, default 5), `lockout_minutes` (1–1440, default 15). No row = the defaults, so no company needs a row before its admin saves one. `GET` / `PUT /api/v1/password-policy` acts on the caller's company (permissions `password-policy.view` / `.edit`); audited.
- All five areas are in scope for phase 1: length, character classes, expiry, history and lockout. Banks audit these.
- Supporting schema: `asp_net_users.password_changed_at` (timestamptz, null = use `created_at`); `password_history` (`company_id`, `user_id`, `password_hash`, `created_at`; only the newest `history_count` rows are kept per user; FK `(user_id, company_id)` raw SQL like `user_role`).
- Where it applies: every place a password is set for a company user — `POST /users`, `POST /users/{id}/password` (STORY-011), `POST /companies/{id}/admin-users` (STORY-047, the target company's policy), `POST /auth/change-password`. One Application validator (`IPasswordPolicyValidator`) checks length, classes and history; errors → 400 `user.password_weak` with rule codes (`password.too_short`, `password.requires_upper`, …, `password.reused`).
- Super Admin has no company: Identity's global `PasswordOptions` and lockout (ADR-004 §6) are set to the same defaults and apply to Super Admin only.
- **Lockout:** login applies the user's company threshold and duration (Identity's global lockout stays as a backstop for Super Admin). The login rate limiter is unchanged.
- **Expiry:** when `expiry_days > 0` and the password is older, a correct password at `POST /auth/login` returns 403 `auth.password_expired` with no session. The user changes it with `POST /auth/change-password` `{ userName, currentPassword, newPassword }` (anonymous, rate-limited like login), which on success returns a session like login. Signed-in users use the same endpoint to change their password. Admin-set passwords reset the expiry clock.
- A policy change applies to the next password set and the next login; existing passwords are not invalidated.

### 9. Audit
`role` create / update / delete (with the permission list in `data`), user role changes inside the `user` commands (STORY-011), `password_policy` update, `company` and `company_admin_user` writes by the Super Admin (`audit_log.company_id` = the target company). Passwords and hashes are `[NotAudited]`.

## Consequences
- Banks can shape roles to their organisation without a release; the system still guarantees that no role crosses companies and that a company always keeps an administrator.
- Authorization is one rule everywhere: a permission on the endpoint plus branch scope in the handler. ADR-008 §9's role table becomes the default grant table above.
- One extra query per request; permission changes are immediate on the server.
- `CurrentUserDto` loses `role` and gains `isSuperAdmin`, `roles`, `permissions`, `branchScope`; the Angular shell (navigation, `roleGuard`, top bar role label) changes in STORY-048 stage 4.
- Identity roles shrink to `SuperAdmin`; existing company users are migrated to `user_role` by STORY-048. Tests seed users through roles, not Identity roles, from STORY-048 on.
- Every new endpoint story must add its catalog entries, default grants and the grant migration — the code reviewer checks this.

## Spec impact
- §8: role table replaced by the roles-and-permissions model with the default roles; new screens `/admin/roles`, `/admin/password-policy`; `/admin/companies` creates the first Company Admin; `/admin/users` assigns one or more roles.
- §9: tables `role`, `role_permission`, `user_role`, `password_policy`, `password_history`; column `asp_net_users.password_changed_at`; Identity roles only `SuperAdmin`.
- §10: `CRUD /roles`, `GET /permissions`, `GET/PUT /password-policy`, `POST /auth/change-password`, `POST /companies/{id}/admin-users`; `/auth/me` returns permissions.
- §13: Resolved rows. Applied to the spec on 2026-11-08 with the product owner's approval.
