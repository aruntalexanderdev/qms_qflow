# TASK-002 — Entities, configuration, initial migration and seed data

| Field | Value |
|---|---|
| ID | TASK-002 |
| Story | STORY-002 |
| Stage | 2 Database |
| Agent | database-architect |
| Status | To do |
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
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] Bilingual text added (en + ar) if user-facing (seeded `name_en` / `name_ar`)
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- …

## 7. Notes / result
