# STORY-002 — Full database schema, migration and seed data

| Field | Value |
|---|---|
| ID | STORY-002 |
| Status | Database |
| Priority | Must |
| Milestone | M1 |
| Spec sections | 4 (4.1–4.4), 9, 11 (time, audit), 13 |
| Requested by | Alexa (product owner) |
| Created | 2026-11-07 |

## 1. User story
As a **developer on the QMS team**,
I want **the complete PostgreSQL schema from spec section 9 as one EF Core migration, with Company 1 and Branch 1 seeded exactly as in section 4**,
so that **every setup and queue story works against the real data model and the real example data**.

## 2. Background
Depends on STORY-001. The schema includes the Phase 0 decisions: VIP fee columns, `branch_priority_fee`, `is_front` and `show_vip` default true (spec 4, 6, 9), plus `refresh_token` (ADR-004, Proposed). Tenant scoping follows ADR-003.

## 3. Scope
**In scope**
- `QmsDbContext` in `Qms.Infrastructure` with Npgsql, `UseSnakeCaseNamingConvention()`, all tables from spec section 9 (phase 1 tables; the phase 2 tables `message_log` and `messaging_setting` included for a complete model), ASP.NET Identity tables, `user_branch`, `user_counter`, `refresh_token`.
- Entities in `Qms.Domain` with `id uuid` (`Guid.CreateVersion7()`), `created_at` / `updated_at` set by an interceptor through `TimeProvider`; enums stored as text.
- Constraints and indexes from section 9: uniques on every mapping table, unique `token (branch_id, business_date, number)`, unique `token_sequence (…)`, unique `branch_priority_fee (branch_id, priority_type_id)`, the partial queue index on `token_visit … where ended_at is null`.
- **Mapping-chain foreign keys:** `branch_service_priority (company… service_type_id, priority_type_id)` → `company_service_priority`; `counter_service_priority (branch… service_type_id, priority_type_id)` → `branch_service_priority`; composite keys as needed.
- Check constraints: prefixes are one upper-case letter; `priority_level >= 1`; `fee_amount` required and `>= 0` when `is_paid`; `max_tokens_per_day` between 1 and 9999.
- Global query filter on `company_id` (ADR-003).
- Seed (idempotent, Development and test environments only), exactly spec section 4:
  - Company 1 (currency AED); Services 1–6 with prefixes A, B, C, D, F, G; priority types Normal (N, level 2, not paid) and VIP (V, level 1, paid, fee 50.00).
  - Company mapping 4.1 (8 rows, incl. Service 2 VIP and Service 3 VIP).
  - Branch 1 (timezone Asia/Dubai, hours Sunday–Thursday 08:00–15:00 as sample data), branch settings (recall limit 3, skip default NoShow), branch mapping 4.2 (7 rows, all Available, max 100).
  - Zone 1, Zone 2; Counters 1–4 and counter mapping 4.3 (7 rows, incl. Counter 2 – Service 3 – VIP).
  - Token Dispenser 1 and 2 with dispenser mapping 4.4 (5 rows), dispenser settings (PrintOnly, `show_vip` true, languages EN + AR).
  - One ticket template with EN + AR header and footer.
- `GET /health/ready` includes a PostgreSQL check.
- Migrations run at API startup in Development only (Azure uses a migration bundle, ADR-006).

**Out of scope**
- CRUD endpoints (M2). Users and roles seeding (STORY-003). Queue logic (M3).

## 4. Business rules
1. Mapping chain (spec section 4, common rules section 3): a branch row needs the pair in the company mapping; a counter row needs the pair in the branch mapping (Available or Not Available). Enforced by foreign keys.
2. Token sequence is unique per branch + business date + priority type + service type (spec 6, section 13 Resolved).
3. All timestamps are stored in UTC (`timestamptz`); business date is a `date` by branch timezone (spec 11).

## 5. Acceptance criteria

**AC-1: Migration on an empty database**
- Given an empty PostgreSQL 18 database (Testcontainers)
- When the migration is applied
- Then it succeeds, and every table and column from spec section 9 (with the Phase 0 columns) exists in snake_case.

**AC-2: Seed matches spec section 4**
- Given the migrated database
- When the seed runs twice
- Then the data matches spec section 4 exactly: 8 company mapping rows, 7 branch mapping rows (Service 2 VIP and Service 3 VIP Available), 7 counter mapping rows (Counter 2 – Service 3 – VIP present), 5 dispenser mapping rows, VIP paid with fee 50.00 and currency AED. Running it a second time creates no duplicates.

**AC-3: Mapping chain enforced by the database**
- Given the seed data
- When inserting a branch mapping row for Service 4 VIP (not in the company mapping)
- Then the insert fails with a foreign key violation; and inserting a counter mapping row for Service 6 Normal in Branch 1 (not in the branch mapping) also fails.

**AC-4: Not Available pair can still be mapped to a counter**
- Given Branch 1 – Service 2 – VIP set to Not Available
- When Counter 2 keeps its Service 2 VIP counter mapping row
- Then no constraint is violated.

**AC-5: Uniqueness**
- Given the seed data
- When inserting a second `token_sequence` row for Branch 1, the same business date, VIP, Service 2; or a second priority type with prefix V in Company 1
- Then the insert fails with a unique violation.

**AC-6: Fee constraint**
- Given priority type VIP
- When `is_paid` is true and `fee_amount` is null or negative
- Then the database rejects the row.

**AC-7: Tenant filter**
- Given a second company seeded in a test
- When `QmsDbContext` is used with Company 1 as the current company
- Then queries return only Company 1 rows.

**AC-8: Readiness**
- Given the API is running against the database
- When `GET /health/ready` is called with the database up, then down
- Then it returns 200, then 503.

## 6. UI / UX
None.

## 7. Data and API impact
- Tables: all of spec section 9 plus `refresh_token` (ADR-004). New Phase 0 columns: `company.currency_code`, `priority_type.is_paid`, `priority_type.fee_amount`, `branch_priority_fee`, `dispenser_setting.show_vip` default true, `token.fee_amount`, `token.fee_status`, `token.fee_settled_by_user_id`, `token.fee_settled_at`, `token.fee_note`, `token_visit.is_front`.
- Endpoint change: `/health/ready` gets the database check.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-003, ADR-004 (refresh_token) |
| 1 BA | Yes | — | |
| 2 Database | Yes | TASK-002 | Entities, configuration, migration, seed, DB tests |
| 3 Backend | Yes | TASK-003 | DbContext registration, dev migrate on startup, health check, tenant context |
| 4 Frontend | N/A | — | No UI |
| 5 Android | N/A | — | No dispenser change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
- `customer_phone` is personal data: no default logging of entity values, and EF sensitive-data logging off outside tests.
- `token_sequence` is designed for `SELECT … FOR UPDATE` row locking (M3).

## 10. Open questions
- [ ] `refresh_token` depends on ADR-004 (Proposed). If ADR-004 is rejected in the Phase 0 PR, remove the table from this story.
- [ ] Branch 1 working hours are sample values (spec section 4 does not list hours). Confirm or change them.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-07
Result: Done
Changed: docs/adr/ADR-003-tenant-and-branch-scoping.md, docs/adr/ADR-004-auth-tokens.md, this story (sections 7–8)
Notes for next stage: The schema must include the Phase 0 spec changes (VIP fee, Service 3 VIP, `is_front`, `show_vip` default true). Enforce the mapping chain with composite foreign keys.
Open questions: none blocking

### Stage 1 — Analysis — business-analyst — 2026-11-07
Result: Done
Changed: this story (sections 1–6, 9–10), docs/work/tasks/TASK-002-schema-migration-seed.md, docs/work/tasks/TASK-003-dbcontext-wiring.md
Notes for next stage: Seed values are exactly spec section 4 after the Phase 0 update; hours are sample data (open question).
Open questions: see section 10
