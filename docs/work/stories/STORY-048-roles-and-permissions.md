# STORY-048 — Roles and permissions management (configurable roles per company)

| Field | Value |
|---|---|
| ID | STORY-048 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 8 (roles and permissions, default roles, `/admin/roles`), 9 (`role`, `role_permission`, `user_role`, Identity roles), 10 (`/roles`, `/permissions`, `/auth/me`), 11 (Security, Audit), 12 (M2), 13 (roles and permissions resolved) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **to define my company's roles, choose per role which modules and actions it may use, and give each user one or more roles**, so that **access matches our organisation without a software change**.

## 2. Background
Product owner decision 2026-11-08 replaced the fixed role matrix of spec §8 with configurable roles per company. The design is **ADR-009** (Accepted): company roles in `role` / `role_permission` / `user_role`; permission codes `<module>.<action>` from a catalog in code with a read-only endpoint; Company Admin, Branch Manager / Supervisor and Counter Staff seeded per company as editable defaults reproducing ADR-008 §9; Super Admin stays a fixed Identity role; one or more roles per user (union); branch scope from the role (`Company` / `Assigned`) plus `user_branch` / `user_counter`; permissions resolved from the database on every request; `/auth/me` returns the effective permissions.

STORY-005 (stage 3 in progress) already protects its endpoints with `[RequirePermission]` and an `IPermissionResolver` backed by the static `RolePermissionMap` (`StaticRolePermissionResolver`). This story replaces that resolver with the database-backed one, moves `IBranchScope` from `IsInRole(CompanyAdmin)` to the role's branch scope, and migrates existing users from Identity roles to `user_role`. It must run **right after STORY-005**, because every later M2 endpoint is declared with permissions and tested through roles.

Assigning roles to users through `/users` is STORY-011 (it uses this story's tables and rules); this story provides the role side, the resolver, the migration and the seed.

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA (source: ADR-009 §1–7).

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. The roles screen `/admin/roles` (permission grid module × action) is M4 (STORY-050). This story changes only the existing shell: navigation, route guards and the top-bar role label (section 7, Web).

## 7. Data and API impact
**Spec:** sections 8, 9, 10, 11, 13 — changed on 2026-11-08 with the product owner's approval (ADR-009). No queue rule changes.

**Amendment 2026-11-08 (localisation, ADR-010; ADR-009 Amendment 1). It supersedes the conflicting lines below.**
- **`role`** is created with `name` (varchar 200, not null) + `name_translations` (jsonb, not null, default `'{}'`, object check), **not** `name_en` / `name_ar`. Unique `(company_id, lower(name))`; there is no unique on translations. The `role.name_taken` catalog entry points at that index.
- The migration and `DefaultRoles` seed English names (Company Admin, Branch Manager / Supervisor, Counter Staff) with no translations.
- **Role API:** `{ name, translations?, branchScope, isActive, sortOrder, permissions[] }`, validated with `LocalizedTextRules`. Lists sort by `sortOrder`, then `name`. DTOs return only the keys of enabled languages.
- **Auth contract:** `roles: [{ id, name }]`, with `name` resolved for the request's `Accept-Language` through `IRequestLanguage` (STORY-051), instead of `{ id, nameEn, nameAr }`. Web: the top bar shows `name` as returned, and after a language switch the app re-reads `/auth/me` (ADR-007 Amendment 1).
- **Catalog:** add the module `company-languages` (view, edit; from STORY-051) to the default grants (Company Admin) and to the migration SQL.
- Permission labels are translated in `en.json` only (other languages through their files later).
- Now runs after STORY-051 (order 005 → 051 → 048).

**Database (stage 2)**
- New tables (ADR-009 §2, Amendment 1): `role` (company_id, name, name_translations, branch_scope `Company`/`Assigned`, is_system, system_code, is_active, sort_order; unique `(company_id, lower(name))`, `(company_id, system_code)` where not null; check `is_system = (system_code is not null)`; alternate key `(company_id, id)`), `role_permission` (company_id, role_id, permission; unique `(role_id, permission)`; cascade on role delete), `user_role` (company_id, user_id, role_id; unique `(user_id, role_id)`; composite FK to role (restrict); raw-SQL FK `fk_user_role_user_company` `(user_id, company_id)` → `asp_net_users (id, company_id)` cascade, like `fk_user_branch_user_company`). All `ITenantEntity` with `xmin`.
- Data migration (e.g. `ConfigurableRoles`): for every existing company insert the three system roles with the ADR-009 §4 default grants as explicit SQL (codes listed in the migration, not read from code); move each company user's Identity role (`CompanyAdmin`, `BranchManager`, `CounterStaff`) to `user_role`; delete those Identity roles and their `asp_net_user_roles` rows; keep `SuperAdmin`. Down migration restores the Identity roles from `system_code` (users with only custom roles lose access on rollback — acceptable, documented).
- Dev seeder: Company 1 system roles via `DefaultRoles`; seeded users get `user_role` rows. `PostgresErrorCatalog`: `role` name uniques → 409 `role.name_taken`; `user_role` FKs → 404 `user.not_found` / `role.not_found`; restrict on role delete → 409 `role.in_use`.

**Backend (stage 3)**
| Method | Route | Permission | Notes |
|---|---|---|---|
| GET | `/api/v1/permissions` | `roles.view` | catalog `[{ module, actions: [{ action, permission }] }]`, display order, codes only |
| GET | `/api/v1/roles` | `roles.view` | sorted `sortOrder`, `name`; `?isActive=`; each `{ id, name, translations, branchScope, isSystem, systemCode?, isActive, sortOrder, permissions[], userCount, version }` |
| GET | `/api/v1/roles/{id}` | `roles.view` | 404 `role.not_found` (other company = same answer) |
| POST | `/api/v1/roles` | `roles.create` | `{ name, translations?, branchScope, isActive, sortOrder, permissions[] }` → 201; custom role (`isSystem` false) |
| PUT | `/api/v1/roles/{id}` | `roles.edit` | all editable fields + `version`; system roles: names, scope, permissions editable, `isActive` must stay true (409 `role.system`) |
| DELETE | `/api/v1/roles/{id}` | `roles.delete` | custom role with no users only; system role → 409 `role.system`; held by users → 409 `role.in_use` |
- Validation: names as ADR-008 §8; permissions distinct, each in the catalog (400 `role.permission_unknown`), every non-view action needs its module's `view` (400 `role.view_required`); `branchScope` required.
- Lock-out rules (ADR-009 §7): 409 `role.cannot_change_self` (removing `roles.edit` / `users.edit` from the caller's own effective set), 409 `role.last_admin` (company would keep no active user with `roles.edit` + `users.edit` and `Company` scope); company row locked `FOR UPDATE` inside the audit transaction. Shared Application service (`IAdminContinuityGuard` or similar) so STORY-011 reuses it for user changes.
- `IPermissionResolver` database implementation (replaces `StaticRolePermissionResolver` and `RolePermissionMap`): one query per request (user active, company active, active roles, their permissions ∩ catalog), memoised per request, exposes `EffectiveAccess(permissions, branchScope)`; `BranchScope` uses `branchScope` instead of `IsInRole(CompanyAdmin)`. Super Admin: no company permissions; `[RequireSuperAdmin]` attribute (role check) for STORY-047.
- `DefaultRoles` (Application): creates the three system roles with default grants for a company; used by the dev seeder and STORY-047 company create.
- `Permissions` catalog: add every M2 module and action of ADR-009 §3 with display order (STORY-005 has service types and priority types). Stories 006–013 use the constants; a test asserts every `[RequirePermission]` code is in the catalog and every catalog code is granted to `CompanyAdmin` by default.
- Auth contract: `CurrentUserDto` (login, refresh, `GET /auth/me`) — remove `role`; add `isSuperAdmin`, `roles: [{ id, name }]` (name resolved for `Accept-Language`, ADR-010 §7), `permissions: string[]` (sorted), `branchScope` (`Company`/`Assigned`, null for Super Admin). JWT: `role` claim only for Super Admin; no permissions in the token (ADR-009 §6). `ICurrentUser.Role` → `IsSuperAdmin`.
- Login: a company user with no active role can still sign in (empty permissions, home page only).
- Audit: `role` create / update / delete (permissions in `data`).
- `SetupTestData` (ADR-008 §15): role users get `user_role` rows; add a "custom role" user for grant/revoke tests (ADR-008 Amendment 1 A).
- SignalR / jobs / `IDeviceConfigNotifier`: none.

**Web (stage 4)** — contract adaptation only:
- `auth.models.ts` `CurrentUser`: drop `role`, add `isSuperAdmin`, `roles`, `permissions`, `branchScope`.
- `navigation.ts`: entries declare a required permission (or `superAdmin`) instead of roles; `roleGuard` → `permissionGuard(...)` / `superAdminGuard`; top bar shows the role names (resolved `name`, joined) or "Super Admin" (translation key).
- After a 403 from the API, re-fetch `GET /auth/me` once and update the store (ADR-009 §6).
- Component tests for guards and navigation per permission set.

**Android / displays:** none.

**Cross-cutting:** company isolation (CA-C2 cannot read or change Company 1 roles: 404); permission changes effective on the next request (test: revoke `service-types.view` from the Branch Manager role → BM-B1 gets 403 on the next call without re-login); Super Admin 403 on `/roles` and `/permissions`; performance: resolver adds one indexed query per request.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-009; ADR-008 Amendment 1 |
| 1 BA | Yes | BA creates | |
| 2 Database | Yes | BA creates | `role` (`name` + `name_translations`, ADR-010), `role_permission`, `user_role` + raw-SQL FK; migration moving Identity roles to `user_role`; dev seed; catalog entries |
| 3 Backend | Yes | BA creates | Role CRUD, `/permissions`, DB resolver, `BranchScope` scope, `DefaultRoles`, lock-out guard, auth contract, full M2 catalog, tests |
| 4 Frontend | Yes | BA creates | `/auth/me` contract changes the shell (navigation, guards, role label); roles screen itself is M4 STORY-050 |
| 5 Android | N/A | — | Devices use device keys, not roles |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] Note for the BA: STORY-005 may already be merged with `StaticRolePermissionResolver`; its AC-25 (role matrix) must still pass after the swap, through the seeded default roles.
- [ ] Note: a company user with no active role signs in with no permissions (not blocked). If the product owner prefers to refuse sign-in, it is a one-line change in the login handler. Not blocking.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10); ADR-009 (new); ADR-008 (Amendment 1); `docs/PROJECT_SPEC.md` §8, §9, §10, §11, §12, §13 (approved by the product owner 2026-11-08)
Notes for next stage:
- Runs right after STORY-005; STORY-047, STORY-049 and every later M2 story depend on it.
- The tasks must keep STORY-005's public pieces (`Permissions` constants, `[RequirePermission]`, `IPermissionResolver`) and swap only the implementation.
- The migration writes the default grants as literal SQL; `DefaultRoles` in code must produce the same set (test compares them).
- Shared lock-out guard is reused by STORY-011.
Open questions: none blocking.

### Stage 0 — Architecture review (amendment: localisation) — project-architect — 2026-11-08
Result: Done
Changed: this story (sections 7, 8); ADR-010 (new); ADR-007 Amendment 1; ADR-008 Amendment 2; ADR-009 Amendment 1; `docs/PROJECT_SPEC.md` §2, §7–§13 (approved by the product owner 2026-11-08)
Notes for next stage:
- Stage 2 builds `role.name` + `name_translations` directly; seeds English role names; `company-languages` permissions join the default grants.
- Auth `roles` carry a resolved `name`. Order is now 005 → 051 → 048.
Open questions: none.
