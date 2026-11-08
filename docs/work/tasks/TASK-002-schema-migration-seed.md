# TASK-002 — Entities, configuration, initial migration and seed data

| Field | Value |
|---|---|
| ID | TASK-002 |
| Story | STORY-002 |
| Stage | 2 Database |
| Agent | database-architect |
| Status | Done |
| Estimate | L |
| Created | 2026-11-07 |

## 1. Goal
Model the full spec section 9 schema (with the Phase 0 changes and `refresh_token`), create the `InitialSchema` migration and the idempotent seed of spec section 4.

## 2. Acceptance criteria covered
- AC-1 to AC-7 (STORY-002)

## 3. Technical details
- Entities in `Qms.Domain/<Area>/` (Organization, Mapping, Devices, Tokens, Audit, Messaging); base `Entity` (`Id`, `CreatedAt`, `UpdatedAt`), `ITenantEntity`.
- Enums (stored as text): `CounterStatus`, `SkipDefault`, `CalendarEntryType`, `DeviceType`, `TokenMode`, `TokenStatus`, `VisitOutcome`, `FeeStatus` (None / Due / Paid / Waived), `FlowMode`, `PrinterStatus`, `MessageChannel`.
- `Qms.Infrastructure/Persistence/QmsDbContext.cs` (Identity: `IdentityDbContext<AppUser, AppRole, Guid>`), one `IEntityTypeConfiguration<T>` per entity in `Configurations/`, `AuditableEntityInterceptor` (timestamps via `TimeProvider`), tenant query filter (ADR-003).
- Composite FKs for the mapping chain (alternate keys on `company_service_priority (company_id, service_type_id, priority_type_id)` and `branch_service_priority (branch_id, service_type_id, priority_type_id)`; carry `company_id` / `branch_id` on the child rows to make this possible).
- Check constraints as listed in STORY-002 section 3; `numeric(12,2)` for money; `timestamptz` for instants; `date` for `business_date`.
- Partial index: `token_visit (zone_id, service_type_id, priority_type_id, is_front, queued_at) WHERE ended_at IS NULL`.
- Migration: `dotnet ef migrations add InitialSchema -p backend/Qms.Infrastructure -s backend/Qms.Api`.
- Seed: `Qms.Infrastructure/Persistence/Seed/DevSeeder.cs`, fixed v7-style GUIDs, upsert by natural keys; data exactly as STORY-002 section 3.
- DB tests (Testcontainers PostgreSQL 18) for AC-1 to AC-7.

## 4. Dependencies
- Needs: TASK-001.
- Provides to: TASK-003 (DbContext and seeder to register), TASK-004 (Identity tables, `refresh_token`), all M2/M3 stories.

## 5. Done checklist
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] Bilingual text added (en + ar) if user-facing (seeded `name_en` / `name_ar`)
- [x] No business logic outside `Qms.Application`
- [x] Spec / ADR updated if behaviour changed
- [x] Handoff written in the story's Stage log

## 6. Tests
All in `backend/Qms.Tests/Persistence/` (Testcontainers PostgreSQL 18, one container per run, one database copy per test via `CREATE DATABASE ... TEMPLATE`). 61 new tests, whole suite 98 passed / 0 failed / 0 skipped.
- `SchemaTests` (AC-1): migration applies to an empty database and nothing is pending; no model changes missing from the migration; every spec section 9 table and column (with Phase 0 columns and `refresh_token`) exists in snake_case, no unexpected tables; column types (uuid, timestamptz, date, `numeric(12,2)`, enums as text, jsonb); `show_vip` default true; the partial queue index definition.
- `SeedTests` (AC-2): 8 / 7 / 7 / 5 mapping rows exactly as spec 4.1-4.4, services and prefixes, priority types (VIP paid 50.00, Normal free), currency AED, Branch 1 timezone, hours, settings, ticket template, bilingual names, dispenser settings, running twice creates no duplicates, a second run does not overwrite later edits, no users or roles.
- `MappingChainTests` (AC-3, AC-4): branch row for Service 4 VIP rejected (FK to company mapping); counter row for Service 6 Normal rejected (FK to branch mapping); valid counter row accepted; Not Available pair keeps its counter rows and can be mapped again; deleting a used mapping row is restricted; counter zone, dispenser zone and mapping company must belong to the same branch / company.
- `ConstraintTests` (AC-5, AC-6): unique `token_sequence`, token number, priority and service prefix (per company), mapping natural keys, `branch_priority_fee`, counter number; fee check constraint (null, negative, update to null), valid combinations, two-decimal money; prefix, priority level, max tokens per day (0, 1, 9999, 10000) and 4-digit sequence checks.
- `TenantAndAuditTests` (AC-7): tenant filter with two companies, no company returns nothing, `IgnoreQueryFilters`, company stamped on insert, writing or updating another company's row throws, UTC timestamps from `TimeProvider`, v7 ids.
- `QueueSupportTests`: 25 parallel `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` issues give sequences 1..25; the Call Next query plan uses `ix_token_visit_queue` with no sort.

## 7. Notes / result
Result: done. Migration `InitialSchema` (35 tables: 27 spec tables incl. `refresh_token`, `user_branch`, `user_counter`; 7 Identity tables; `__EFMigrationsHistory`). Design notes and the backend hand-off are in the story's Stage 2 entry.

Review loop 1 (2026-11-08): stage 6 asked for tenant/branch integrity fixes (M1, M2, S1). `InitialSchema` was regenerated (never deployed). New: `token_visit.branch_id`; composite foreign keys for `token_visit` (branch, token, zone, counter), `dispenser_zone_service` and `counter_service_priority` (company + branch), `dispenser_setting` (company + device) and `message_log` (company + token); alternate keys `token (branch_id, id)`, `token (company_id, id)`, `device (company_id, id)`; `company_id` is immutable after insert and tenant checks use the original company. 19 new tests (`TenantIntegrityTests`, 5 in `TenantAndAuditTests`); suite 129 passed / 0 failed / 0 skipped. See the story's "Stage 2 — Database (review loop 1)" entry.

