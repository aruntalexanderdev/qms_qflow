# TASK-009 — Map xmin as row version (model-only migration) and tenant-guard model test

| Field | Value |
|---|---|
| ID | TASK-009 |
| Story | STORY-005 |
| Stage | 2 Database |
| Agent | database-architect |
| Status | To do |
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
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] Bilingual text added (en + ar) if user-facing — N/A, no user-facing text
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- `SchemaTests`: `HasPendingModelChanges` false; the migration's `Up`/`Down` produce no operations; `information_schema.columns` identical before and after (no `xmin` column added by EF).
- Model test: every entity has a row-version property mapped to `xmin`.
- Model test (N9): every `ITenantEntity` has `CompanyId` as a concurrency token with `AfterSaveBehavior = Throw`.
- DB test: two contexts load the same `service_type`, the first saves, the second saves with the old value and gets `DbUpdateConcurrencyException`.
- Existing STORY-002 and STORY-003 tests stay green.

## 7. Notes / result
<Filled by the agent when done.>
