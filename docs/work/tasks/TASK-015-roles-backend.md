# TASK-015 — Roles backend: catalog, role API, database resolver, lock-out guard, auth contract

| Field | Value |
|---|---|
| ID | TASK-015 |
| Story | STORY-048 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | To do |
| Estimate | L |
| Created | 2026-11-08 |

## 1. Goal
Deliver `/permissions` and `/roles`, replace the static resolver with the database-backed one, take branch scope from the role, protect against lock-out, and change the auth user contract.

## 2. Acceptance criteria covered
- AC-6 to AC-32 (catalog and endpoints AC-6 to AC-18; effective access AC-19 to AC-23; lock-out AC-24, AC-25; matrix and regression AC-26, AC-27; auth contract AC-28 to AC-31; test data AC-32)

## 3. Technical details
- **Catalog:** extend `Permissions` with a catalog description (module, actions, display order, view action) on top of the constants from TASK-014; `Permissions.All` stays; module display order as ADR-009 §3. Invariant tests (AC-7), including STORY-005's invariants kept for database roles (ADR-008 Amendment 3 F).
- **Endpoints** (`Qms.Api` `RolesController`, `PermissionsController`, ADR-008 conventions): `GET /api/v1/permissions` (`roles.view`); `GET /api/v1/roles[?isActive=]`, `GET /roles/{id}` (`roles.view`); `POST` (`roles.create`, 201); `PUT /{id}` (`roles.edit`, `version`); `DELETE /{id}` (`roles.delete`, 204). Request: `{ name, translations?, branchScope, isActive, sortOrder, permissions[] }`; `isActive` and `sortOrder` default only on `POST`; `PUT` is full replacement with `CommonRules.Sent()`; `LocalizedTextRules` for name/translations (languages from the company, STORY-051); DTO `{ id, name, translations, branchScope, isSystem, systemCode?, isActive, sortOrder, permissions[], userCount, version }`, translations limited to enabled languages. Commands `[Audited("role", "create|update|delete")]` with permissions in `data`.
- **Validation and errors:** `role.permission_unknown`, `role.view_required` (400), `role.not_found` (404, other company same), `role.name_taken` (409), `role.system` (409, deactivate or delete a system role), `role.in_use` (409, delete with users), `role.cannot_change_self`, `role.last_admin` (409). `PostgresErrorCatalog` entries for the `role` name unique, `user_role` FKs and the restrict on role delete (AC-18; names fixed in TASK-014).
- **Version:** when only the permission set changes, touch the role row (`updated_at`) so `xmin` and `version` change; a no-op `PUT` sends no `UPDATE`.
- **Resolver:** `DatabaseRolePermissionResolver` (Infrastructure) behind `IPermissionResolver`; one query per request (user active, company active, `user_role` to active `role` to `role_permission`, filtered to the catalog), memoised per request, exposes `EffectiveAccess(Permissions, BranchScope)`; Super Admin and users without a company get nothing. Delete `StaticRolePermissionResolver` and `RolePermissionMap`; `[RequirePermission]` and `Permissions` constants stay; add `[RequireSuperAdmin]` (role check) for STORY-047.
- **Branch scope:** `BranchScope` reads the effective scope from the resolver instead of `IsInRole(CompanyAdmin)`; `Company` = all branches of the company (null from `AccessibleBranchIdsAsync`), `Assigned` = `user_branch`.
- **Lock-out guard:** Application service (`IAdminContinuityGuard` or similar) used by the role commands: caller's own effective set after the change must keep `roles.edit` and `users.edit` (`role.cannot_change_self`); the company must keep an active user (active user, active company) whose union of active roles has both permissions and `Company` scope (`role.last_admin`); the company row is locked `SELECT … FOR UPDATE` inside the audit transaction. Public interface suitable for STORY-011 (user changes). Unit tests incl. concurrent requests (AC-25).
- **Auth contract:** `CurrentUserDto` drops `Role`, gets `IsSuperAdmin`, `Roles` (`{ Id, Name }`, name via `IRequestLanguage` and `LocalizedText`, default-language fallback), `Permissions` (sorted ordinally), `BranchScope` (`Company`/`Assigned`/null); used by login, refresh and `GET /auth/me`. JWT: `role` claim only for Super Admin; `ICurrentUser.Role` becomes `IsSuperAdmin`; update `HttpContextCurrentUser` and `GetCurrentUserQuery`. A company user with no active role signs in with empty permissions.
- **Tests:** `SetupTestData` creates users through `user_role` and `DefaultRoles`, adds CU-C1 ("Type Viewer") and helpers to grant/revoke a permission and create a custom role; integration tests for every endpoint (ADR-008 §15: happy path with audit row, 400, 401, 403 for BM/CS/SA, cross-company 404, 409 stale version, 409 in use); matrix tests AC-26, AC-27 (STORY-005 and STORY-051 suites run unchanged against seeded roles); query-count test AC-22; revoke/grant next-request test AC-21; auth contract tests AC-28 to AC-31 (incl. Arabic role name with `Accept-Language`).

## 4. Dependencies
- Needs: TASK-014 (tables, migration, catalog constants, `DefaultRoles`); STORY-051 (`LocalizedTextRules`, `IRequestLanguage`).
- Provides to: TASK-016 (new `user` contract), STORY-011 (guard, `user_role`), STORY-047 (`DefaultRoles`, `[RequireSuperAdmin]`), every later M2 story (catalog, `SetupTestData`).

## 5. Done checklist
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] User-facing text localised (translation keys / translated fields; English required, ADR-010)
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- Unit: catalog invariants, `DefaultRoles` vs migration, role validators, lock-out guard, effective access union and scope.
- Integration (Testcontainers): `/permissions`, `/roles` CRUD and matrix, lock-out incl. concurrency, resolver query count, next-request effect, auth contract, STORY-005/051 regression.

## 7. Notes / result
<Filled by the agent when done.>
