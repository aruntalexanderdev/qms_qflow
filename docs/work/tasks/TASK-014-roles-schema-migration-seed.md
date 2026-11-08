# TASK-014 — Roles schema, data migration and seed

| Field | Value |
|---|---|
| ID | TASK-014 |
| Story | STORY-048 |
| Stage | 2 Database |
| Agent | database-architect |
| Status | Done (stage 2); story status Backend |
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
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] User-facing text localised (role `name` + `name_translations`; English seed names)
- [x] No business logic outside `Qms.Application` (the temporary sign-in adapter below is the one exception, removed by TASK-015)
- [x] Spec / ADR updated if behaviour changed (no behaviour change against spec section 9 / ADR-009; see notes)
- [x] Handoff written in the story's Stage log

## 6. Tests
`backend/Qms.Tests/Persistence/ConfigurableRolesTests.cs` (16 tests, Testcontainers PostgreSQL 18):
- AC-1: `Ac1_the_three_tables_have_the_columns_of_the_design_and_no_name_en_or_name_ar`, `Ac1_constraints_and_indexes_exist_under_their_fixed_names`, `Ac1_the_snapshot_has_no_pending_model_changes`, `Ac1_role_rules_name_scope_system_code_and_translations`, `Ac1_role_permission_is_unique_per_role_and_cascades_and_a_role_held_by_a_user_cannot_be_deleted`, `Ac1_user_role_refuses_a_super_admin_and_a_cross_company_pair_and_follows_the_user_delete`, `Ac1_the_catalog_maps_the_role_constraints`.
- AC-2 / AC-3: `Ac2_the_migration_seeds_the_three_system_roles_with_the_literal_grants_for_every_company` (60 / 28 / 0 for both companies, Branch Manager set checked module by module), `Ac3_existing_users_move_to_user_role_and_only_super_admin_stays_an_identity_role`, `Ac3_the_migration_refuses_and_changes_nothing_when_a_company_role_cannot_move`.
- AC-4: `Ac4_down_restores_the_identity_roles_and_drops_the_tables` (incl. custom-role-only user loses access, Up again), `Ac4_down_gives_a_user_with_two_system_roles_the_most_privileged_identity_role`.
- AC-5: `Ac5_the_catalog_has_60_distinct_codes_and_the_default_counts_are_60_28_0`, `Ac5_default_roles_equal_what_the_migration_wrote`, `Ac5_the_dev_seed_gives_company_1_the_system_roles_and_the_company_admin_holds_one_through_user_role`, `Ac5_the_seeded_template_database_has_the_roles_of_company_1_and_no_tenant_row_leaks_to_other_companies`.
- Existing tests updated for the new schema: `SchemaTests` (three new tables), `UserAssignmentTests` (`fk_user_role_user_company` in the raw-constraint list), `AuthEndpointsTests` (only `SuperAdmin` is an Identity role; the counter-staff test gets its role through `user_role`).
- Result: `dotnet build backend/Qms.slnx -c Release` 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release` Passed 505, Failed 0, Skipped 0.

## 7. Notes / result
**Delivered**
- Domain: `Role`, `RolePermission`, `UserRole` (all `TenantEntity`, so `company_id`, audit columns and `xmin` version); enums `RoleBranchScope` (`Company`/`Assigned`) and `SystemRoleCode` (`CompanyAdmin`/`BranchManager`/`CounterStaff`), stored as text.
- Infrastructure: `RoleConfigurations.cs` (checks, alternate key `(company_id, id)`, composite FKs, fixed names in `RoleConstraints`); `QmsDbContext` sets `CompanyRoles`, `RolePermissions`, `CompanyUserRoles` (`Roles` / `UserRoles` are taken by Identity); migration `20261108101828_ConfigurableRoles`.
- Migration: tables via EF; the `xmin` `AddColumn` operations EF generated were removed again (as in `RowVersion`; the model/snapshot still map `xmin`); raw SQL for the unique index `ux_role_company_id_lower_name` `(company_id, lower(name))` and `fk_user_role_user_company`; literal-SQL seed of the three system roles per company, 60 / 28 / 0 literal grants, `user_role` insert from `asp_net_user_roles` (with a guard that raises and rolls back if a company role cannot be moved), removal of the three Identity roles. Down restores them (one Identity role per user, the most privileged system role) and drops the tables; users with only custom roles lose access.
- Application: `Permissions.cs` now has all 60 codes (19 modules) in `Permissions.All`; `DefaultRoles` (`Definitions`, `For`, `CreateFor(companyId)`); `IQmsDbContext` exposes the three new sets. `RolePermissionMap` and `StaticRolePermissionResolver` untouched (TASK-015 removes them).
- Seed: `DevSeeder` adds the three system roles to Company 1 through `DefaultRoles` (idempotent per system code); `IdentitySeeder` creates only `SuperAdmin` and gives the Company Admin a `user_role` row.
- `PostgresErrorCatalog`: `ux_role_company_id_lower_name` -> 409 `role.name_taken`; `fk_user_role_user_company` -> 404 `user.not_found`; `fk_user_role_role_company_id_role_id` -> 404 `role.not_found` on insert (23503) and 409 `role.in_use` on a restricted delete (23001, new `RestrictedForeignKeys` table).
- Deviations / notes: (1) migration rows use `gen_random_uuid()` (v4), not v7 ids (SQL cannot call `Guid.CreateVersion7`); app-created rows use v7. (2) `role_permission.permission` is `varchar(100)` (ADR says text); codes are far shorter. (3) the `role_permission (role_id)` index is the leading column of the unique `(role_id, permission)`. No change to spec section 9, no ADR request.
- Temporary adapter (so the existing 505 tests stay green): `UserAccounts.ToDtoAsync` falls back to the system code of the user's most privileged active system role in `user_role` when the user has no Identity role, so the `role` claim, `StaticRolePermissionResolver` and `BranchScope` (`IsInRole(CompanyAdmin)`) keep working. TASK-015 removes it with the static resolver. `SetupTestData` seeds the system roles for Company 1 and 2 and `user_role` rows.
