---
name: database-design
description: Senior PostgreSQL + EF Core method for the QMS — naming, keys, mapping-chain constraints, token sequence locking, indexes for the queue engine, migrations and seed data. Use in stage 2.
---

# Database design skill

## Conventions
- PostgreSQL 16+, EF Core with Npgsql; `UseSnakeCaseNamingConvention()`.
- `id uuid` primary key (generated in app, `Guid.CreateVersion7()`), `created_at timestamptz`, `updated_at timestamptz`.
- Tenant tables carry `company_id` with a global query filter in the DbContext.
- Bilingual text: `name_en`, `name_ar` (both required for user-facing names).
- Enums stored as text (`HasConversion<string>()`), not integers.
- One `IEntityTypeConfiguration<T>` per entity in `Persistence/Configurations/`.

## Mapping-chain integrity
- `branch_service_priority (branch_id, service_type_id, priority_type_id)` has a composite FK to `company_service_priority (service_type_id, priority_type_id)` via an alternate key.
- `counter_service_priority` references `branch_service_priority` through the counter's branch (enforce in Application if a composite FK is impractical, and add a test).
- Unique constraints on every mapping table's natural key.
- Prefix uniqueness: `service_type (company_id, prefix)`, `priority_type (company_id, prefix)`.

## Token numbering
- `token_sequence (branch_id, business_date, priority_type_id, service_type_id)` unique; `last_sequence int`.
- Issue: `INSERT ... ON CONFLICT DO UPDATE SET last_sequence = token_sequence.last_sequence + 1 RETURNING last_sequence` inside the issue transaction — atomic, no race.
- Unique `token (branch_id, business_date, number)` as a safety net.

## Indexes for the queue engine
- `token_visit (zone_id, service_type_id, priority_type_id, queued_at) WHERE ended_at IS NULL AND called_at IS NULL`.
- `token (branch_id, business_date, status)`.
- Call Next uses `FOR UPDATE SKIP LOCKED` — make sure the query hits the partial index (check with `EXPLAIN`).

## Migrations
- Name: `YYYYMMDD_<StoryId>_<What>` e.g. `20261110_STORY012_AddDispenserZoneService`.
- Never edit an applied migration; add a new one.
- Destructive changes (drop/rename with data) need an ADR and a data-migration step.
- Seed data in a separate seeder class (Company 1 / Branch 1 example), idempotent.

## Tests
Testcontainers PostgreSQL: migration applies on an empty DB; unique constraints reject duplicates; mapping FKs reject invalid pairs; two parallel token issues get different sequences.
