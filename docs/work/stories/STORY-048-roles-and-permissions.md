# STORY-048 — Roles and permissions management (configurable roles per company)

| Field | Value |
|---|---|
| ID | STORY-048 |
| Status | Backend |
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
**In scope**
- Database: `role`, `role_permission`, `user_role`; migration that seeds the three system roles for every existing company (explicit SQL), moves existing users' Identity roles to `user_role`, and leaves only `SuperAdmin` in `asp_net_roles`; dev seed through `DefaultRoles`.
- Backend: permission catalog for the whole M2 (ADR-009 §3) and `GET /permissions`; `/roles` CRUD; database-backed `IPermissionResolver` (replaces `StaticRolePermissionResolver` and `RolePermissionMap`); `BranchScope` from the role's branch scope; shared lock-out guard (`role.cannot_change_self`, `role.last_admin`); `DefaultRoles`; auth contract change (`isSuperAdmin`, `roles[]`, `permissions[]`, `branchScope`; `role` removed); audit of role writes; `SetupTestData` through roles.
- Web shell (contract adaptation only): `CurrentUser` model, permission-based navigation and route guards, role names in the top bar, re-read of `GET /auth/me` after a 403 and after a language switch, error-code and permission-label texts in `en.json`.
- Regression: STORY-005 and STORY-051 endpoints give the same outcomes for the seeded users through the seeded default roles.

**Out of scope**
- Assigning roles to users (`roleIds` on `/users`, `user.role_required`, `user.cannot_change_self`, `role.inactive`, user filter by role): STORY-011 (ADR-009 §5, STORY-011 section 7). This story provides `user_role`, the role-side rules and the shared guard that STORY-011 reuses.
- The `/admin/roles` permission-grid screen: STORY-050 (M4). Password policy: STORY-049. Companies and the rescue admin user (`POST /companies/{id}/admin-users`): STORY-047.
- Android, devices, displays, SignalR, background jobs: none.
- Permissions of later milestones (`counter-console.operate`, `tokens.*`, `dashboard.view`, `reports.*`, `audit-log.view`): added by their own stories with a grant migration. Sign-in refusal for a user with no active role is not built (see section 10).

## 4. Business rules
| # | Rule | Source |
|---|---|---|
| R1 | Roles are managed per company by users holding the `roles.*` permissions. Super Admin is a fixed Identity role (`SuperAdmin`), not stored in `role`, holds no company permissions and gets 403 on `/roles` and `/permissions`. | Spec 8; ADR-009 §1 |
| R2 | A permission is `<module>.<action>` (kebab-case, stable). The catalog lives in code; `GET /permissions` returns it in display order, codes only. | ADR-009 §3 |
| R3 | Any action of a module needs that module's `view` in the same role (400 `role.view_required`). Unknown codes are rejected (400 `role.permission_unknown`); codes must be distinct. | ADR-009 §3, §2 |
| R4 | Each role has a branch scope: `Company` (every branch of the company) or `Assigned` (only `user_branch` / `user_counter`). | Spec 8; ADR-009 §2 |
| R5 | Three system roles per company, seeded as editable defaults: Company Admin (`Company`, all 60 permissions of the M2 catalog), Branch Manager / Supervisor (`Assigned`, 28 permissions), Counter Staff (`Assigned`, none). Default grants are the table in ADR-009 §4. | Spec 8 (default roles); ADR-009 §4 |
| R6 | System roles: name, translations, scope and permissions are editable; they cannot be deleted or deactivated (409 `role.system`). Custom roles: full CRUD; delete only while no user holds the role (409 `role.in_use`); deactivating is allowed and an inactive role grants nothing. | ADR-009 §4 |
| R7 | Role name: default-language `name` required, trimmed, 1-200 characters, unique per company ignoring case (409 `role.name_taken`); `translations` optional, validated by the localized-text rule. Lists sort by `sortOrder`, then `name`. | ADR-008 §8 and Amendment 2; ADR-009 Amendment 1; ADR-010 §5 |
| R8 | Effective permissions of a user = union of the permissions of their **active** roles (only codes still in the catalog). Effective branch scope = `Company` if any active role has `Company`, else `Assigned`. | ADR-009 §5 |
| R9 | The server resolves permissions from the database on every request (one query, memoised per request). A change to a role, to its active flag, to a user's roles or active flag, or to the company's active flag is effective on the next request, without a new login. The JWT carries no permissions and no company roles; `role` claim only for Super Admin. | ADR-009 §6 |
| R10 | Order of checks stays: `[RequirePermission]` (403 `auth.forbidden`), then in the handler 404 for a missing or other-company target, then `IBranchScope.EnsureAccessAsync` (403). A company user without any permission still signs in (empty permissions). | ADR-009 §5, §6; ADR-008 §10 |
| R11 | Lock-out: a role save may not remove `roles.edit` or `users.edit` from the caller's own effective permissions (409 `role.cannot_change_self`). Any role change that would leave the company without an active user holding both `roles.edit` and `users.edit` with effective scope `Company` is rejected (409 `role.last_admin`); the check runs after locking the company row `FOR UPDATE` in the audit transaction. The last resort is Super Admin creating a Company Admin (STORY-047). | ADR-009 §7 |
| R12 | Role `PUT` is a full replacement with `version`; missing field 400, stale or missing version as ADR-008 §5 and Amendment 3 A; a no-op `PUT` succeeds, is audited and sends no `UPDATE`. `POST` defaults only `isActive` (true) and `sortOrder` (0); `name`, `branchScope` and `permissions` must be sent. | ADR-008 §5, Amendment 3 A, B |
| R13 | Role `create`, `update`, `delete` are audited (`entity` `role`, permissions inside `data`). Reads are not. | Spec 11; ADR-009 §9 |
| R14 | Login, refresh and `GET /auth/me` return `user` with `isSuperAdmin`, `roles: [{ id, name }]` (name resolved for `Accept-Language`), `permissions` (sorted), `branchScope` (`Company`/`Assigned`, null for Super Admin), `branchIds`, `counterIds`; `role` is removed. | ADR-009 §6 and Amendment 1; ADR-010 §7 |
| R15 | The web app builds menus and guards from `permissions` / `isSuperAdmin`, never from the JWT; it re-reads `GET /auth/me` after any 403 and after a language switch. The UI is a convenience; the server decides. | ADR-009 §6; ADR-007 B.5 and Amendment 1 |
| R16 | Permission matrix of this story's endpoints: `GET /permissions` and `GET /roles[/{id}]` = `roles.view`; `POST` = `roles.create`; `PUT` = `roles.edit`; `DELETE` = `roles.delete`. | ADR-009 §3 |

## 5. Acceptance criteria
Test users (ADR-008 §15, `SetupTestData`): CA-C1 / CA-C2 (Company Admin of Company 1 / 2), BM-B1 (Branch Manager of Branch 1), BM-B2 (Branch Manager of Branch 2, Company 1), CS (Counter Staff, Counter 1), SA (Super Admin), plus CU-C1, a Company 1 user holding only the custom role "Type Viewer" (`Assigned`, permissions `service-types.view`). "Seeded roles" = the three system roles of the company.

### Database and migration (TASK-014)
**AC-1: Tables and constraints**
- Given the migration is applied, Then `role`, `role_permission` and `user_role` exist (snake_case, uuid keys, `created_at` / `updated_at`, `company_id` on all three, `xmin` version on the entities). `role` has `name` (varchar 200, not null), `name_translations` (jsonb, not null, default `'{}'`, object check), `branch_scope` limited to `Company` / `Assigned`, `is_system`, `system_code` limited to `CompanyAdmin` / `BranchManager` / `CounterStaff`, `is_active`, `sort_order`; unique `(company_id, lower(name))`; unique `(company_id, system_code)` where not null; check `is_system = (system_code is not null)`; alternate key `(company_id, id)`; there is no `name_en` / `name_ar` and no unique on translations. `role_permission` is unique `(role_id, permission)` and cascades on role delete. `user_role` is unique `(user_id, role_id)`, has a restrict composite FK `(company_id, role_id)` to `role`, and the raw-SQL FK `fk_user_role_user_company` `(user_id, company_id)` to `asp_net_users (id, company_id)` with cascade.
- When a Super Admin user id (company null) is inserted into `user_role`, Then the database rejects it; when a user of Company 1 is linked to a role of Company 2, Then the database rejects it; when a user is deleted, Then their `user_role` rows are deleted.

**AC-2: Migration seeds the three system roles per existing company**
- Given a database at the previous migration with Company 1 and Company 2, When the migration runs, Then each company has exactly three `role` rows: "Company Admin" (`CompanyAdmin`, `Company`), "Branch Manager / Supervisor" (`BranchManager`, `Assigned`), "Counter Staff" (`CounterStaff`, `Assigned`), all `is_system` true, `is_active` true, `name_translations` `{}`, `sort_order` 1, 2, 3.
- Then Company Admin has 60 `role_permission` rows (every code of ADR-009 §3 including `company-languages.view` / `.edit`), Branch Manager has 28 (view of `service-types`, `priority-types`, `service-mapping`, `ticket-templates`, `branches`, `branch-hours`, `branch-settings`, `branch-fees`, `service-flows`; all actions of `branch-service-mapping`, `zones`, `counters`, `counter-service-mapping`, `devices`, `dispenser-settings`), Counter Staff has 0. The codes are written as literals in the migration SQL.

**AC-3: Existing users move from Identity roles to `user_role`**
- Given before the migration CA-C1 and CA-C2 hold Identity role `CompanyAdmin`, BM-B1 and BM-B2 `BranchManager`, CS `CounterStaff`, SA `SuperAdmin`, When the migration runs, Then CA-C1 has one `user_role` row to Company 1's Company Admin role (CA-C2 to Company 2's), BM-B1 and BM-B2 to Company 1's Branch Manager role, CS to Company 1's Counter Staff role, and SA has no `user_role` row. Their `user_branch` / `user_counter` rows are unchanged.
- Then `asp_net_roles` contains only `SuperAdmin`, `asp_net_user_roles` contains only SA's row, and SA still signs in with role claim `SuperAdmin`.

**AC-4: Down migration**
- Given the migrated database, When the migration is reverted, Then the Identity roles `CompanyAdmin`, `BranchManager`, `CounterStaff` exist again, each user holding a system role gets the matching Identity role from `system_code`, users holding only custom roles get none (documented in the migration), and the three tables are dropped.

**AC-5: Dev seed and `DefaultRoles` agree with the migration**
- Given an empty database seeded for development, Then Company 1 has the three system roles and the seeded Company Admin, Branch Manager and Counter Staff users hold them through `user_role`, with no Identity role other than `SuperAdmin`. A test creates the roles with `DefaultRoles` for a new company and asserts that names, scopes, `sort_order` and the permission sets are identical to the ones the migration wrote (60 / 28 / 0).

### Permission catalog and `GET /permissions` (TASK-015)
**AC-6: Catalog endpoint**
- Given CA-C1, When it calls `GET /api/v1/permissions`, Then 200 with `[{ module, actions: [{ action, permission }] }]` in display order, containing the 19 modules of ADR-009 §3 plus `company-languages` (for example `{ module: "service-types", actions: [view, create, edit, delete] }`, `users` with `reset-password`, `devices` with `registration-code`), 60 codes in total, codes only (no labels). The response has no translated text.

**AC-7: Catalog invariants**
- A test asserts: every `[RequirePermission]` code on any controller is in the catalog; every catalog code is granted to the seeded Company Admin role; every non-`view` code has its module's `view` in the catalog; no catalog code is duplicated.

### Role endpoints (TASK-015)
**AC-8: List roles**
- Given Company 1 has the three seeded roles and the custom role "Type Viewer", When CA-C1 calls `GET /api/v1/roles`, Then 200 with four rows sorted by `sortOrder`, then `name`; each has `id`, `name`, `translations`, `branchScope`, `isSystem`, `systemCode` (null for custom), `isActive`, `sortOrder`, `permissions[]`, `userCount` (CA-C1 = 1 on Company Admin; BM-B1 and BM-B2 = 2 on Branch Manager) and `version`. `?isActive=false` returns only inactive roles. The response contains no Company 2 role.

**AC-9: Get one role; not found and cross-company**
- Given the Company 1 Branch Manager role, When CA-C1 calls `GET /roles/{id}`, Then 200 with the 28 permissions. When CA-C2 calls `GET`, `PUT` or `DELETE` on a Company 1 role id, or anyone calls an unknown id, Then 404 `role.not_found`, and the Company 1 role is unchanged.

**AC-10: Create a custom role**
- Given CA-C1, When it posts `{ name: "Zone Supervisor", branchScope: "Assigned", permissions: ["zones.view","zones.edit"] }` (no `isActive`, no `sortOrder`), Then 201 with `isSystem` false, `systemCode` null, `isActive` true, `sortOrder` 0, `userCount` 0, `translations` `{}` and a `version`; `GET /roles` lists it. When `translations` holds a language enabled for Company 1 (for example `ar`) with a value, Then it is saved and returned; a key for a language that is not enabled → 400 `translation.language_not_enabled`.

**AC-11: Role validation**
- Given CA-C1, When it posts or puts: empty or 201-character `name`; missing `branchScope`; `branchScope` "Region"; missing `permissions`; a code not in the catalog (`zones.fly`); the same code twice; `["zones.edit"]` without `zones.view`; Then 400 `validation.failed` for the first four, 400 `role.permission_unknown`, 400 for duplicates, 400 `role.view_required` for the last, and nothing is saved and no audit row is written. `permissions: []` is valid.

**AC-12: Role name unique per company**
- Given Company 1 has "Counter Staff", When CA-C1 creates or renames a role to "counter staff", Then 409 `role.name_taken`. The uniqueness is per company: "Zone Supervisor" created by CA-C1 in Company 1 and by CA-C2 in Company 2 succeeds in both, while CA-C2 creating "Counter Staff" in Company 2 (seeded there) is 409 `role.name_taken`.

**AC-13: Update a role (full replacement, version)**
- Given the custom role "Zone Supervisor" at version v1, When CA-C1 puts `{ name: "Zone Supervisor", branchScope: "Assigned", isActive: true, sortOrder: 5, permissions: ["zones.view","zones.edit","counters.view"], version: v1 }`, Then 200, the role has exactly those three permissions (the previous set is replaced, not merged), and `version` differs from v1 even though only permissions changed.
- When the same `PUT` is sent again with the old v1, Then 409 `request.conflict`; when `version` is missing, Then 400; when a required field (`isActive`, `branchScope`, `permissions`) is missing, Then 400 `validation.failed` keyed by the field and nothing changes; a `PUT` with equal values and the current version succeeds, writes an audit row and keeps `version`.

**AC-14: System roles are editable but protected**
- Given the Company 1 Branch Manager role, When CA-C1 renames it "Supervisor", adds `users.view`, and changes `branchScope` to `Company` (all in one `PUT` with the current `version`), Then 200 and the changes apply. When CA-C1 puts `isActive: false` on it, Then 409 `role.system` and nothing changes. When CA-C1 deletes it, Then 409 `role.system`. `systemCode` and `isSystem` cannot be changed through the API.

**AC-15: Deactivate a custom role**
- Given CU-C1 holds only "Type Viewer", When CA-C1 puts it with `isActive: false`, Then 200 and on CU-C1's next request `GET /service-types` returns 403 `auth.forbidden` and `GET /auth/me` shows `roles` without it and `permissions: []`. When reactivated, CU-C1's next request is allowed again.

**AC-16: Delete a custom role**
- Given "Zone Supervisor" with no users, When CA-C1 deletes it, Then 204, its `role_permission` rows are gone and it is no longer listed. Given CU-C1 holds "Type Viewer", When CA-C1 deletes it, Then 409 `role.in_use` and the role and its users are unchanged.

**AC-17: Audit entry for every role write**
- Given CA-C1, When it creates "Zone Supervisor", updates it, and deletes it, Then `audit_log` gains three rows, each with `company_id` Company 1, `user_id` CA-C1, `device_id` null, `entity` `role`, `action` `create` / `update` / `delete`, `entity_id` the role id, `at` from `TimeProvider`, and `data` holding the command JSON including the `permissions` list. Failed writes (400, 404, 409 including `role.last_admin`) write no audit row and no role change. Reads write none.

**AC-18: Postgres error catalog**
- A test asserts that `PostgresErrorCatalog` maps the `role` name unique index to 409 `role.name_taken`, the `user_role` user FK to 404 `user.not_found`, the `user_role` role FK to 404 `role.not_found`, and the restrict on deleting a role that has `user_role` rows to 409 `role.in_use`, and that every catalog constraint name exists in the model or migration.

### Effective access (TASK-015)
**AC-19: Union of active roles**
- Given a user in Company 1 holds "Type Viewer" (`service-types.view`) and a custom role "Priority Editor" (`priority-types.view`, `priority-types.edit`), When they call `GET /auth/me`, Then `permissions` is the sorted union of the three codes and `roles` has both roles. When "Priority Editor" is deactivated, Then the next `GET /auth/me` shows only `service-types.view`.

**AC-20: Branch scope is the widest of the active roles**
- Given a user holds Branch Manager (`Assigned`) and a custom role with `Company` scope, Then `branchScope` is `Company`; when the `Company` role is deactivated, Then it is `Assigned`. A user with no active role has `branchScope` `Assigned`.

**AC-21: Changes take effect on the next request**
- Given BM-B1 is signed in and `GET /service-types` returns 200, When CA-C1 removes `service-types.view` from the Branch Manager role, Then BM-B1's next `GET /service-types` (same access token, no login) returns 403 `auth.forbidden`; when CA-C1 grants it again, Then the next call returns 200. The same holds for deactivating the role, for deactivating BM-B1's user, and for deactivating Company 1: each loses every permission on the next request; `GET /auth/me` still answers while the token is valid.

**AC-22: Database-backed resolver**
- Given any authenticated request to an endpoint with `[RequirePermission]` and a handler that asks the resolver and the branch scope again, Then the database is queried once for the effective access (a test counts the commands); a user of an inactive company, an inactive user, or a Super Admin (no company) gets no permissions. Codes stored in `role_permission` that are not in the catalog are ignored. `StaticRolePermissionResolver` and `RolePermissionMap` no longer exist.

**AC-23: Branch scope comes from the role**
- Given BM-B1 (`Assigned`, Branch 1) and a user with the custom role "Region Admin" (`Company` scope, no `user_branch` rows), Then `IBranchScope` lets "Region Admin" pass for Branch 1 and Branch 2 and returns `null` (all branches) from `AccessibleBranchIdsAsync`; BM-B1 passes for Branch 1 only and Branch 2 returns 403 `auth.forbidden`; an unknown or other-company branch id is 404 for both. After CA-C1 changes the Branch Manager role to `Company` scope, BM-B1's next call passes for Branch 2 too. The code no longer checks `IsInRole(CompanyAdmin)`.

### Lock-out protection (TASK-015)
**AC-24: Cannot remove own role-management rights**
- Given CA-C1 holds only the Company Admin role, When CA-C1 puts that role without `roles.edit` (or without `users.edit`), Then 409 `role.cannot_change_self` and nothing changes. When CA-C1 deactivates a custom role that is the only source of its own `roles.edit`, Then 409 `role.cannot_change_self`. Removing a permission other than these two, or editing a role CA-C1 does not hold, is allowed.

**AC-25: The company keeps an administrator**
- Given Company 1 whose only active user with `roles.edit`, `users.edit` and `Company` scope is CA-C1, and a second user RA holding a custom role with `roles.edit` and `users.view` only, When RA puts the Company Admin role without `users.edit` (RA is not affected by `cannot_change_self`), Then 409 `role.last_admin`. The same for changing the Company Admin role's scope to `Assigned`, or deactivating a role that gives CA-C1 those rights. When a second Company-scope administrator exists, the same changes succeed.
- Given two administrators A and B who would each remove the other, When both requests run at the same time, Then exactly one succeeds and the other gets 409 `role.last_admin` (company row locked `FOR UPDATE` inside the audit transaction; test with two concurrent requests).
- The guard is a shared Application service so that STORY-011 can call it for user changes; a unit test covers it directly (no active admin, one admin, inactive admin user, admin only through an inactive role).

### Permission matrix and regression (TASK-015)
**AC-26: Permission matrix for `/roles` and `/permissions`**
- Given the seeded roles, Then: CA-C1 may call `GET /permissions`, `GET /roles`, `GET /roles/{id}` (200), `POST` (201), `PUT` (200) and `DELETE` (204 or 409 in use). BM-B1, BM-B2 and CS get 403 `auth.forbidden` on all six endpoints, including both `GET`s. SA gets 403 `auth.forbidden` on all six. No token → 401 `auth.unauthorized`.
- Given CU-C1 with a custom role holding `roles.view` only, Then `GET /permissions`, `GET /roles` and `GET /roles/{id}` are 200 and `POST` / `PUT` / `DELETE` are 403; with `roles.view` and `roles.create` the `POST` is 201 and `PUT` still 403; likewise `roles.edit` opens `PUT` and `roles.delete` opens `DELETE`, each after the role save, on the next request, and closes again when revoked.

**AC-27: STORY-005 and STORY-051 regression through the seeded roles**
- Given the seeded Company Admin, Branch Manager and Counter Staff roles, Then the AC-25 matrix of STORY-005 gives the same outcomes as before the swap: CA-C1 may use all ten service-type / priority-type endpoints; BM-B1 and BM-B2 get 200 on the `GET`s and 403 `auth.forbidden` on `POST` / `PUT` / `DELETE`; CS and SA get 403 on all ten; anonymous gets 401. The `company-languages` endpoints of STORY-051 (`company-languages.view` / `.edit`) allow CA-C1 only (BM-B1, CS, SA 403). All existing STORY-005 and STORY-051 integration tests pass with users seeded through `user_role` and no Identity company roles.
- Given a custom role user CU-C1 ("Type Viewer"), Then `GET /service-types` is 200 and `POST /service-types` is 403; after CA-C1 adds `service-types.create` and `service-types.view` stays, the next `POST` is 201 (grant and revoke test of ADR-008 Amendment 1 A).

### Auth contract (TASK-015)
**AC-28: Login, refresh and `/auth/me` return the new user object**
- Given CA-C1 (Company 1, `Accept-Language` absent), When it signs in, refreshes, and calls `GET /auth/me`, Then each response `user` has `isSuperAdmin` false, `roles: [{ id, name: "Company Admin" }]`, `permissions` = the 60 codes sorted ordinally, `branchScope` "Company", `branchIds` [] and `counterIds` []; there is no `role` field. BM-B1 returns `roles: [{ id, name: "Branch Manager / Supervisor" }]`, 28 permissions, `branchScope` "Assigned", `branchIds` [Branch 1]. CS returns `permissions: []` and `counterIds` [Counter 1]. SA returns `isSuperAdmin` true, `roles` [], `permissions` [], `branchScope` null, `companyId` null.

**AC-29: Role names resolved for the request language**
- Given Company 1 has Arabic enabled and the Company Admin role has translation `ar` "مدير الشركة", When CA-C1 calls `GET /auth/me` with `Accept-Language: ar`, Then `roles[0].name` is "مدير الشركة"; with `en` or an unsupported language, or a role without that translation, it is "Company Admin" (default-language fallback, ADR-010 §7).

**AC-30: Token content**
- Given CA-C1 signs in, Then the access token has claims `sub`, `company_id`, `name`, `lang` and no `role` or permission claim; SA's token has `role` = `SuperAdmin`. `ICurrentUser` exposes `IsSuperAdmin` instead of `Role`.

**AC-31: User with no active role**
- Given CU-C1 whose only role "Type Viewer" is inactive, When CU-C1 signs in with the correct password, Then 200 with `roles` [], `permissions` [], `branchScope` "Assigned"; every endpoint with `[RequirePermission]` returns 403 `auth.forbidden`, `GET /auth/me` and `PUT /auth/me/language` work.

### Test data (TASK-015)
**AC-32: Shared test data uses roles**
- `SetupTestData` creates CA-C1, CA-C2, BM-B1, BM-B2, CS and SA, the company system roles through `DefaultRoles`, and CU-C1 with the custom role "Type Viewer"; role users get `user_role` rows, not Identity roles. Helpers exist to add or remove a permission on a role and to create a custom role, so each later endpoint story can write its grant / revoke test.

### Web shell (TASK-016)
**AC-33: Contract model**
- Given the web `CurrentUser` model, Then it has no `role` and no `UserRole`-based logic; it has `isSuperAdmin`, `roles: { id, name }[]`, `permissions: string[]`, `branchScope: 'Company' | 'Assigned' | null`. A session restored from a refresh uses the same fields; component tests use fakes with the new shape.

**AC-34: Navigation by permission**
- Given the navigation entries declare a required permission (or `superAdmin`), When the shell renders for: CA-C1 (60 permissions), then Service types, Priority types, Service mapping, Branches, Users and Templates entries that are `live` appear (each by its `*.view` code: `service-types.view`, `priority-types.view`, `service-mapping.view`, `branches.view`, `users.view`, `ticket-templates.view`); BM-B1, then only entries whose view code BM-B1 holds (Service types, Priority types, Service mapping, Branches, Templates) and not Users; CS (no permissions), then Home only; SA, then Home and Companies only; a user with only `users.view`, then Home and Users only. Entries that are not `live` stay hidden. Section headings with no visible entry are dropped.

**AC-35: Route guards**
- Given `permissionGuard('users.view')` and `superAdminGuard`, When CS opens `/admin/users`, Then redirected to `/home`; when CA-C1 opens it, allowed; when SA opens `/admin/companies`, allowed, and CA-C1 is redirected to `/home`; with no session, redirected to `/login`. `roleGuard` and the role-name table (`UserRole`) are removed.

**AC-36: Top bar shows the role names**
- Given CA-C1, Then the top bar shows "Company Admin"; a user with two roles shows both names joined with a comma in the order returned; SA shows "Super Admin" from a translation key; a user with no role shows no role label. Names are shown exactly as returned (resolved by the server).

**AC-37: Re-read `/auth/me` after a 403**
- Given BM-B1 is signed in and the Branch Manager role lost `service-types.view`, When any API call returns 403 `auth.forbidden`, Then the app calls `GET /auth/me` once, updates the store (navigation and guards use the new `permissions`) and shows the 403 message; several concurrent 403s trigger one re-read; a failing `/auth/me` (401) follows the normal expired-session path and a re-read is not repeated in a loop.

**AC-38: Re-read `/auth/me` after a language switch**
- Given Company 1 has Arabic enabled and CA-C1 is signed in, When CA-C1 switches the language to Arabic, Then after the language is applied the app calls `GET /auth/me` with `Accept-Language: ar` and the top bar shows the role name returned for Arabic; switching back to English shows "Company Admin".

**AC-39: Texts**
- Given `en.json`, Then it has keys for the error codes `role.not_found`, `role.name_taken`, `role.system`, `role.in_use`, `role.permission_unknown`, `role.view_required`, `role.cannot_change_self`, `role.last_admin`, `permissions.modules.<module>` for each of the 19 modules and `permissions.actions.<action>` for each distinct action, and the "Super Admin" role label; a test fails when a catalog module or action has no key. No hard-coded UI text.

## 6. UI / UX
The roles screen `/admin/roles` (permission grid module × action) is M4 (STORY-050). This story changes only the existing shell; no new screen.

- **Sidebar** (English labels as today, translation keys unchanged): Home, Dashboard, Companies, Service types, Priority types, Service mapping, Branches, Users, Templates, Counter console, Reports. Visibility rule: entry is `live` and the user holds the entry's permission (`service-types.view`, `priority-types.view`, `service-mapping.view`, `branches.view`, `users.view`, `ticket-templates.view`), or `superAdmin` for Companies. Dashboard, Counter console and Reports stay hidden (not live); their permission codes (`dashboard.view`, `counter-console.operate`, `reports.view`) are declared as strings and become real with their stories. No `/admin/roles` entry until STORY-050.
- **Top bar:** avatar, display name, then the role label: role names joined by ", " as returned by `/auth/me` (already in the signed-in language), or the translatable "Super Admin" for a Super Admin. Existing layout and design tokens; no new component style (rules section 11).
- **Redirects:** a signed-in user without the permission for a route goes to `/home` (unchanged behaviour, now by permission).
- **Localisation (ADR-010):** English default in `en.json`; texts added: role error codes, `permissions.modules.*`, `permissions.actions.*`, "Super Admin" label. Other languages come through their translation files when made available. RTL: no layout change; the role label uses logical properties already in the top bar.
- **Accessibility:** no colour-only information; the role label is text.

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
| 1 BA | Yes | — | Story, ACs, TASK-014 to TASK-016 |
| 2 Database | Yes | [TASK-014](../tasks/TASK-014-roles-schema-migration-seed.md) — AC-1 to AC-5 | `role` (`name` + `name_translations`, ADR-010), `role_permission`, `user_role` + raw-SQL FK; migration moving Identity roles to `user_role`; dev seed; catalog entries |
| 3 Backend | Yes | [TASK-015](../tasks/TASK-015-roles-backend.md) — AC-6 to AC-32 (builds on the catalog constants and `DefaultRoles` that TASK-014 adds for the seed) | Role CRUD, `/permissions`, DB resolver, `BranchScope` scope, `DefaultRoles`, lock-out guard, auth contract, full M2 catalog, tests |
| 4 Frontend | Yes | [TASK-016](../tasks/TASK-016-roles-web-shell.md) — AC-33 to AC-39 | `/auth/me` contract changes the shell (navigation, guards, role label); roles screen itself is M4 STORY-050 |
| 5 Android | N/A | — | Devices use device keys, not roles |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
- **Performance:** the resolver adds one indexed query per authenticated request (< 1 ms, inside the 300 ms budget of token issue and Call Next); indexes on `user_role (user_id)`, `role_permission (role_id)`. No cross-request cache in this story (ADR-009 §6).
- **Security:** no permissions in the JWT; server is the authority; role endpoints are company-isolated (other company = 404); permission codes are validated against the catalog on save and ignored on read when removed. Super Admin cannot hold a company role (database FK).
- **Concurrency:** role `version` must change when only the permission set changes (stage 3 touches the role row, e.g. `updated_at`, so `xmin` changes); lock-out checks run under the company row lock.
- **Migration safety:** the migration writes grants as literals, never reads application code; rollback loses access for users with only custom roles (documented).
- **Localisation:** role `name` + `translations` (ADR-010); permission labels and error codes in `en.json` only.
- **Order and dependencies:** after STORY-005 and STORY-051; STORY-047, 049, 050, 011 and every later M2 story depend on it.

## 10. Open questions
- [x] Note for the BA: STORY-005 AC-25 must still pass after the swap through the seeded default roles. Covered by AC-27.
- [ ] Note (not blocking, for the product owner to confirm): the web navigation keeps the codes `dashboard.view`, `counter-console.operate` and `reports.view` for entries that are not live; they are not in the catalog yet and are added by their M3/M5 stories. Until then those entries stay hidden.
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

### Stage 1 — Analysis — business-analyst — 2026-11-08
Result: Done
Changed: this story (sections 3, 4, 5, 6, 8 task links, 9, 10; header status); new tasks `docs/work/tasks/TASK-014-roles-schema-migration-seed.md`, `TASK-015-roles-backend.md`, `TASK-016-roles-web-shell.md`
Notes for next stage:
- 39 acceptance criteria: AC-1 to AC-5 database (TASK-014); AC-6 to AC-32 backend (TASK-015); AC-33 to AC-39 web shell (TASK-016). Stage 5 N/A (no change).
- Role assignment to users (`roleIds` on `/users`) is out of scope here and stays in STORY-011 (section 7); this story ships the role-side rules and the shared lock-out guard.
- Seed dependency: the dev seeder needs the permission constants and `DefaultRoles`, so TASK-014 adds the catalog constants for every M2 module (`Permissions.cs`, with `All`) and `DefaultRoles`, as the stage 0 note "catalog entries" says; TASK-015 adds the catalog description (display order, view rule), the endpoints and the resolver. Stage plan unchanged.
- The migration drops the Identity company roles, so the static resolver and existing tests that seed Identity roles stop working between stage 2 and stage 3. Keep stages 2 and 3 on the same branch and green together; TASK-014 only requires its own database tests green and says so.
- Counts to assert: Company Admin 60 permissions, Branch Manager 28, Counter Staff 0 (19 modules incl. `company-languages`).
- Role `version` must change when only permissions change (touch the role row).
Open questions: none blocking (see section 10: navigation codes for not-yet-live entries; no-role sign-in).

### Stage 2 — Database — database-architect — 2026-11-08
Result: Done
Changed: `backend/Qms.Domain/Entities/Identity/{Role,RolePermission,UserRole}.cs`, `backend/Qms.Domain/Enums/{RoleBranchScope,SystemRoleCode}.cs`, `backend/Qms.Infrastructure/Persistence/Configurations/RoleConfigurations.cs`, `backend/Qms.Infrastructure/Persistence/QmsDbContext.cs`, `backend/Qms.Infrastructure/Persistence/Migrations/20261108101828_ConfigurableRoles*.cs` + snapshot, `backend/Qms.Infrastructure/Persistence/Seed/DevSeeder.cs`, `backend/Qms.Infrastructure/Identity/IdentitySeeder.cs`, `backend/Qms.Infrastructure/Identity/UserAccounts.cs` (temporary adapter), `backend/Qms.Infrastructure/Errors/PostgresErrorCatalog.cs`, `backend/Qms.Application/Auth/{Permissions,DefaultRoles}.cs`, `backend/Qms.Application/Common/IQmsDbContext.cs`, tests `Persistence/ConfigurableRolesTests.cs` (new), `SchemaTests`, `UserAssignmentTests`, `Auth/AuthEndpointsTests`, `Support/SetupTestData`; task [TASK-014](../tasks/TASK-014-roles-schema-migration-seed.md)
Notes for next stage:
- New tables: `role` (company_id, name, name_translations, branch_scope, is_system, system_code, is_active, sort_order), `role_permission` (company_id, role_id, permission), `user_role` (company_id, user_id, role_id). EF sets: `db.CompanyRoles`, `db.RolePermissions`, `db.CompanyUserRoles` (also on `IQmsDbContext`; `Roles` / `UserRoles` belong to Identity). `Role.Permissions` is the navigation to `role_permission` (cascade). `Role.BranchScope` is `RoleBranchScope`, `Role.SystemCode` is `SystemRoleCode?`.
- Constraint names in `RoleConstraints` (Infrastructure): `ux_role_company_id_lower_name` (expression index, raw SQL, not in the EF model), `ux_role_company_id_system_code`, `ux_role_permission_role_id_permission`, `ux_user_role_user_id_role_id`, FKs `fk_role_permission_role_company_id_role_id` (cascade), `fk_user_role_role_company_id_role_id` (restrict), `fk_user_role_user_company` (raw SQL, cascade). `PostgresErrorCatalog` already maps them (`role.name_taken`, `user.not_found`, `role.not_found`; restrict delete 23001 -> `role.in_use`); AC-18 only needs a test over the existing entries.
- Version: `role` has `xmin` like every entity. Updating only `role_permission` rows does not change the role's `xmin`: touch the role row (e.g. `UpdatedAt`) in the update handler (story section 9).
- Catalog: `Permissions.All` has the 60 codes; TASK-015 adds the description (display order, view rule) and `GET /permissions`. `DefaultRoles.Definitions` / `CreateFor(companyId)` give the three system roles with grants (60 / 28 / 0) for STORY-047 and the tests; a test keeps it equal to the migration SQL.
- TEMPORARY ADAPTER to remove in TASK-015: `UserAccounts.ToDtoAsync` (Infrastructure/Identity) falls back to the system code of the user's most privileged active system role in `user_role` when the user has no Identity role. It lets the `role` claim, `StaticRolePermissionResolver`, `BranchScope` (`IsInRole(CompanyAdmin)`) and all existing tests keep working. The full suite is green with it (505 passed, 0 failed, 0 skipped). Without it, every company-user login would return 401 (no role).
- Tests changed because of the schema (not because of the adapter): `SchemaTests` (three new tables), `UserAssignmentTests` (raw constraint list), `AuthEndpointsTests` (Identity roles = only `SuperAdmin`; the counter-staff login test gets its role through `user_role`). `SetupTestData` now seeds the system roles of Company 1 and 2 and `user_role` rows for CA-C1, CA-C2, BM-B1, BM-B2, CS; only SA uses an Identity role. TASK-015 still adds the custom "Type Viewer" user CU-C1 and the grant/revoke helpers (AC-32) and moves the test host's `AuthHost.UserOf`/`CurrentUserDto.Role` to `IsSuperAdmin`.
- `IdentitySeeder` now takes `QmsDbContext`; `Roles.All` still lists the four names (used for authorization policies in `AuthenticationSetup` and by tests); TASK-015 reduces it with the contract change.
- Migration ids of rows it creates are `gen_random_uuid()` (v4); fine for lookups by `system_code`.
Open questions: none
