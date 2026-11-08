# TASK-009 — Map xmin as row version (model-only migration) and tenant-guard model test

| Field | Value |
|---|---|
| ID | TASK-009 |
| Story | STORY-005 |
| Stage | 2 Database |
| Agent | database-architect |
| Status | Done |
| Estimate | S |
| Created | 2026-11-08 |

## 1. Goal
Give every entity an optimistic-concurrency row version using the PostgreSQL system column `xmin` (ADR-008 §5), with a migration that changes no schema, and add the STORY-002 review N9 model test.

## 2. Acceptance criteria covered
- AC-36 (row version in the model, no DDL, N9 guard)
- Model side of AC-32 (the `version` string helpers themselves are in TASK-010)

## 3. Technical details
- `backend/Qms.Infrastructure/Persistence/Configurations/EntityConfiguration<T>`: map `xmin` as a row version (`uint`, `IsRowVersion()`, column name `xmin`, system column, not created by Npgsql). Add the CLR property on the Domain base type (or a shadow property) as the cleanest option without giving Domain a dependency; record the choice in the handoff. Applies to every entity using the base (tenant and non-tenant). Identity tables (`AppUser`, `AppRole`) keep `ConcurrencyStamp` and are not changed.
- Migration (for example `RowVersion`) via `dotnet ef migrations add RowVersion -p backend/Qms.Infrastructure -s backend/Qms.Api`. `Up` and `Down` must contain no DDL (only the updated snapshot). `HasPendingModelChanges` in `SchemaTests` must stay false.
- Make sure `AuditableEntityInterceptor` and the tenant guard (`CompanyId` concurrency token, `AfterSaveBehavior = Throw`) still work with the new token: an update with a stale `xmin` throws `DbUpdateConcurrencyException`.
- New model test (STORY-002 review N9): every `ITenantEntity` has `CompanyId` with `IsConcurrencyToken` and `AfterSaveBehavior = Throw`.
- Seed unchanged; no new table, column, index or constraint.

## 4. Dependencies
- Needs: nothing (first task of the story).
- Provides to: TASK-010 (stage 3) uses the row version for `version`, `PUT` conflict (409) and the set hash.

## 5. Done checklist
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] Bilingual text added (en + ar) if user-facing — N/A, no user-facing text
- [x] No business logic outside `Qms.Application`
- [x] Spec / ADR updated if behaviour changed
- [x] Handoff written in the story's Stage log

## 6. Tests
- `SchemaTests`: `HasPendingModelChanges` false; the migration's `Up`/`Down` produce no operations; `information_schema.columns` identical before and after (no `xmin` column added by EF).
- Model test: every entity has a row-version property mapped to `xmin`.
- Model test (N9): every `ITenantEntity` has `CompanyId` as a concurrency token with `AfterSaveBehavior = Throw`.
- DB test: two contexts load the same `service_type`, the first saves, the second saves with the old value and gets `DbUpdateConcurrencyException`.
- Existing STORY-002 and STORY-003 tests stay green.

## 7. Notes / result
Done 2026-11-08. `Entity.RowVersion` (uint) in Domain, mapped to `xmin`/`xid` with `IsRowVersion()` once in `EntityConfiguration<T>` (ADR-008 decision; Domain stays dependency-free). Migration `20261108072029_RowVersion` has empty Up/Down: EF generated AddColumn xmin per table, which PostgreSQL rejects for a system column, so the operations were removed before applying.

Tests added in `backend/Qms.Tests/Persistence/RowVersionTests.cs`: every entity maps RowVersion to xmin; N9 (every ITenantEntity has CompanyId concurrency token + AfterSaveBehavior Throw); UPDATE SQL filters on company_id and xmin; stale xmin on update and on delete throws DbUpdateConcurrencyException; client version via OriginalValue; no real xmin column; RowVersion migration has no operations. `SchemaTests.The_model_has_no_changes...` still green. One existing test (`TenantIntegrityTests.A_detached_row_of_the_current_company_can_still_be_updated_and_removed`) now reads the row before removing it because DELETE filters on xmin.

Result: `dotnet build backend/Qms.slnx -c Release` 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release` Passed 254, Failed 0, Skipped 0.
