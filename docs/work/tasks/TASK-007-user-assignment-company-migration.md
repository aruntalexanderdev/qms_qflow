# TASK-007 — User assignment company consistency (additive migration)

| Field | Value |
|---|---|
| ID | TASK-007 |
| Story | STORY-003 (scope amendment 2026-11-08) |
| Stage | 2 Database |
| Agent | database-architect |
| Status | Done |
| Estimate | M |
| Created | 2026-11-08 |

## 1. Goal
Make the database reject any `user_branch` or `user_counter` row that crosses companies or belongs to a Super Admin (STORY-002 follow-up S3, ADR-003 Amendment 1 B.1).

## 2. Acceptance criteria covered
- AC-13, AC-14, AC-15, AC-16, AC-23 (STORY-003)

## 3. Technical details
- New additive migration, e.g. `UserAssignmentCompany`. `InitialSchema` is merged and must not be regenerated.
- `user_branch.company_id` and `user_counter.company_id`: uuid, not null. Backfill from `branch.company_id` / `counter.company_id`. The migration fails (and changes nothing) if an existing row's user belongs to another company.
- `UserBranch` and `UserCounter` become `ITenantEntity`: tenant query filter, stamping, immutable `company_id`, concurrency token.
- New alternate key on `counter (company_id, id)`.
- Composite FKs: `user_branch (company_id, branch_id)` → `branch (company_id, id)`; `user_counter (company_id, counter_id)` → `counter (company_id, id)`.
- Raw SQL in the migration (EF cannot model a key on the nullable `AppUser.CompanyId`):
  - unique `ux_asp_net_users_id_company_id` on `asp_net_users (id, company_id)`;
  - FKs `fk_user_branch_user_company` and `fk_user_counter_user_company` on `(user_id, company_id)` → `asp_net_users (id, company_id)`, on delete cascade.
- The Down migration drops all of the above (columns, key, FKs, unique constraint).
- No change to the actor-user FKs (`token.issued_by_user_id`, `token.fee_settled_by_user_id`, `token_visit.user_id`, `counter.current_user_id`).
- Seed: no new rows; the seeder creates no assignments.
- The model-drift test must stay green (raw-SQL constraints are outside the EF model).
- Snake_case names, `company_id` on tenant tables, per project rules section 5.

## 4. Dependencies
- Needs: STORY-002 `InitialSchema` (TASK-002). Product owner approval of `company_id` on `user_branch` / `user_counter` in spec section 9: starting is allowed, **merging is blocked** until approved (story section 10).
- Provides to: TASK-008 (stage 3 uses the entities as `ITenantEntity` and the constraints for AC-13 to AC-16).

## 5. Done checklist
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] Bilingual text added (en + ar) if user-facing — N/A (no user-facing text)
- [x] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed (spec section 9 after approval) — pending product owner approval; spec not edited by stage 2 (blocks merge only)
- [x] Handoff written in the story's Stage log

## 6. Tests
- Cross-company `user_branch` (Company 2 user, Branch 1) rejected by `fk_user_branch_user_company` and by the `(company_id, branch_id)` FK (AC-13).
- Cross-company `user_counter` (Company 2 user, Counter 2) rejected by `fk_user_counter_user_company` and by the `(company_id, counter_id)` FK (AC-14).
- Super Admin `user_branch` and `user_counter` rows rejected (AC-15).
- Valid same-company rows accepted; changing the user's company while rows exist is rejected (AC-16).
- Migration backfill, failure on a mismatched existing row, and migrate down (AC-23).
- Model-drift test still green; seed unchanged.

Implemented in `backend/Qms.Tests/Persistence/UserAssignmentTests.cs` (AC-13, AC-14, AC-15, AC-16 x2, delete cascade, raw-constraint existence + drift, AC-23 backfill / failure x3 scenarios / down then up, seed unchanged) and `SchemaTests` (company_id columns). Full suite: 152 passed, 0 failed, run twice.

## 7. Notes / result
Migration `20261108025641_UserAssignmentCompany` (additive, InitialSchema untouched).
- `UserBranch` / `UserCounter` now derive from `TenantEntity` (`ITenantEntity`: query filter, stamping, immutable company, concurrency token).
- `counter`: new alternate key `ak_counter_company_id_id (company_id, id)`.
- EF composite FKs replace the old single-column ones (cascade kept): `fk_user_branch_branch_company_id_branch_id`, `fk_user_counter_counter_company_id_counter_id`; plus `fk_*_company_company_id`.
- Raw SQL: `ux_asp_net_users_id_company_id`, `fk_user_branch_user_company`, `fk_user_counter_user_company` (cascade on user delete).
- Up order: guard (DO block raises when an existing row's user company differs from the branch/counter company, or the user has none) -> drop old FKs -> add nullable column -> backfill -> NOT NULL -> keys/FKs. Whole migration runs in one transaction, so a failure changes nothing.
- Down drops raw constraints first, then FKs, key, columns, and restores the old single-column FKs. Assignment rows are kept.
- Drift: raw constraints exist in neither model nor snapshot, so `HasPendingModelChanges` stays green; `The_raw_sql_constraints_exist_...` asserts they exist in a migrated DB (names, type, definition) and fails if a later migration drops them.
