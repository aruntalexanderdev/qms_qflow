# TASK-014 — Roles schema, data migration and seed

| Field | Value |
|---|---|
| ID | TASK-014 |
| Story | STORY-048 |
| Stage | 2 Database |
| Agent | database-architect |
| Status | To do |
| Estimate | L |
| Created | 2026-11-08 |

## 1. Goal
Create `role`, `role_permission` and `user_role`, seed the three system roles per existing company with the ADR-009 §4 grants, move existing users from Identity roles to `user_role`, and update the dev seed.

## 2. Acceptance criteria covered
- AC-1, AC-2, AC-3, AC-4, AC-5

## 3. Technical details
- **Entities and configuration** (Domain + Infrastructure, `ITenantEntity`, `EntityConfiguration<T>` with `xmin`): `Role` (`company_id`, `name` varchar 200, `name_translations` jsonb not null default `'{}'` with object check, `branch_scope` text with check `Company`/`Assigned`, `is_system`, `system_code` with check on the three codes, `is_active`, `sort_order`, `created_at`, `updated_at`); `RolePermission` (`company_id`, `role_id`, `permission`); `UserRole` (`company_id`, `user_id`, `role_id`). No `name_en` / `name_ar`.
- **Keys and indexes:** unique `(company_id, lower(name))`; unique `(company_id, system_code)` where not null; check `is_system = (system_code is not null)`; alternate key `(company_id, id)`; `role_permission` unique `(role_id, permission)`, composite FK `(company_id, role_id)` cascade; `user_role` unique `(user_id, role_id)`, composite FK `(company_id, role_id)` restrict, raw-SQL FK `fk_user_role_user_company` `(user_id, company_id)` to `asp_net_users (id, company_id)` cascade (same pattern as `fk_user_branch_user_company`); indexes on `user_role (user_id)` and `role_permission (role_id)`. Use fixed constraint names so TASK-015 can map them in `PostgresErrorCatalog`.
- **Migration** (`ConfigurableRoles`, from the repository root: `dotnet ef migrations add ConfigurableRoles -p backend/Qms.Infrastructure -s backend/Qms.Api`): for every company insert the three roles ("Company Admin" `CompanyAdmin` `Company` sort 1; "Branch Manager / Supervisor" `BranchManager` `Assigned` sort 2; "Counter Staff" `CounterStaff` `Assigned` sort 3; `is_system` true, `name_translations` `{}`); insert `role_permission` rows as literal codes (ADR-009 §4 and the §3 catalog incl. `company-languages`): Company Admin 60, Branch Manager 28, Counter Staff 0; insert `user_role` from `asp_net_user_roles` joined to `asp_net_roles` by name and company; delete the three Identity roles and their `asp_net_user_roles` rows; keep `SuperAdmin`. Do not call application code.
- **Down:** recreate the three Identity roles, restore `asp_net_user_roles` from `user_role` + `system_code`, drop the tables; comment that users with only custom roles lose access.
- **Catalog constants and `DefaultRoles` (needed by the seed):** in `Qms.Application/Auth/Permissions.cs` add the constants of every M2 module and action of ADR-009 §3 plus `company-languages` and extend `All` (60 codes); add `DefaultRoles` (Application) that creates the three system roles with the default grants for a company. TASK-015 adds the catalog description and endpoints on top. Leave `RolePermissionMap` and `StaticRolePermissionResolver` for TASK-015 to remove.
- **Dev seeder:** Company 1 gets the system roles via `DefaultRoles`; the seeded Company Admin, Branch Manager and Counter Staff users get `user_role` rows instead of Identity roles; only `SuperAdmin` is created in `asp_net_roles`.
- **Tests (database):** migration up on a database with data at the previous migration (Companies 1 and 2, users CA-C1, CA-C2, BM-B1, BM-B2, CS, SA with Identity roles) and assertions of AC-2/AC-3 (counts 60 / 28 / 0, names, scopes, sort order); constraint tests of AC-1 (Super Admin and cross-company `user_role` rejected, cascade on user delete, role delete cascades permissions and is restricted by `user_role`); down migration (AC-4); `DefaultRoles` output equals the migration output (AC-5); model snapshot has no pending changes.

## 4. Dependencies
- Needs: STORY-005 and STORY-051 merged (`Permissions.cs`, `company_language` objects), the `asp_net_users (id, company_id)` alternate key from `UserAssignmentCompany`.
- Provides to: TASK-015 (tables, catalog constants, `DefaultRoles`, constraint names). Note: after this migration Identity company roles no longer exist, so the static resolver and tests that seed Identity roles fail until TASK-015; land stages 2 and 3 together on one branch. This task's own database tests must be green.

## 5. Done checklist
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] User-facing text localised (translation keys / translated fields; English required, ADR-010)
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- Migration up with data (AC-2, AC-3); migration down (AC-4); constraints (AC-1); `DefaultRoles` equals migration grants and dev seed (AC-5); snapshot check.

## 7. Notes / result
<Filled by the agent when done.>
