# STORY-002 — Full database schema, migration and seed data

| Field | Value |
|---|---|
| ID | STORY-002 |
| Status | Code Review |
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

### Stage 2 — Database — database-architect — 2026-11-08
Result: Done
Changed:
- Domain: `backend/Qms.Domain/Entities/**` (27 entities, `Entity`, `TenantEntity`, `ITenantEntity`, `IAuditable`), `backend/Qms.Domain/Enums/**` (12 enums).
- Application: `backend/Qms.Application/Common/ICurrentTenant.cs`.
- Infrastructure: `backend/Qms.Infrastructure/Persistence/**` (`QmsDbContext`, `AuditableEntityInterceptor`, `TenantViolationException`, `QmsDbContextOptions.UseQmsPostgres`, `QmsDbContextFactory`, `Configurations/*`, `Identity/AppUser.cs` (AppUser, AppRole), `Seed/DevSeeder.cs`, `Seed/SeedIds.cs`, `Migrations/*_InitialSchema`), packages in `backend/Qms.Infrastructure/Qms.Infrastructure.csproj`.
- Build/tooling: `backend/Directory.Packages.props` (EF Core 10.0.14, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, EFCore.NamingConventions 10.0.1, Identity.EntityFrameworkCore, Testcontainers.PostgreSql 4.16.0), `backend/Qms.Api/Qms.Api.csproj` (EF Design, `PrivateAssets=all`), `backend/Qms.Tests/Qms.Tests.csproj`, `.config/dotnet-tools.json` (local tool manifest at the repository root, dotnet-ef 10.0.14), `backend/README.md`.
- Tests: `backend/Qms.Tests/Persistence/*` (61 new tests; suite 98 passed, 0 failed, 0 skipped).
- Task: docs/work/tasks/TASK-002-schema-migration-seed.md (Done).
Notes for next stage (TASK-003):
- Registration is yours: `services.AddDbContext<QmsDbContext>((sp, o) => o.UseQmsPostgres(connectionString).AddInterceptors(new AuditableEntityInterceptor(sp.GetRequiredService<TimeProvider>())))` plus `AddScoped<DevSeeder>()`. `UseQmsPostgres` sets Npgsql, snake_case and the migrations assembly; add retry on failure via its Npgsql options if you want it. `QmsDbContext` takes `ICurrentTenant` (interface in `Qms.Application/Common`: `Guid? CompanyId`); implement it in `Qms.Api` (no company until STORY-003). Without the interceptor timestamps stay `default`, so register it.
- Tenant filter fails closed: with `CompanyId == null` every tenant query returns nothing and `SaveChanges` neither stamps nor checks. Seeder, Super Admin `[CrossTenant]` handlers, login and background jobs (daily reset) must use `IgnoreQueryFilters()` or a context with a tenant. With a tenant, new rows get `CompanyId` stamped, and writing a row of another company throws `TenantViolationException` (map to 403/500 as you see fit; it signals a bug).
- Not tenant-filtered by design: ASP.NET Identity tables (`asp_net_*`), `refresh_token`, `user_branch`, `user_counter` (sign-in looks users up before a tenant is known; branch scope is checked by `IBranchScope`).
- `DevSeeder.SeedAsync()` is idempotent (inserts missing rows by natural key, never overwrites) and does not check the environment; gate it to Development/test in Program. Fixed ids in `SeedIds` (e.g. `SeedIds.Company1`, `Branch1`, `Service(1..6)`, `Counter(1..4)`, `Zone(1..2)`, `Dispenser(1..2)`, `Normal`, `Vip`).
- Migrate with `db.Database.MigrateAsync()` in Development only (Azure uses a bundle, ADR-006). Create further migrations with `dotnet tool restore` then `dotnet ef migrations add <Name> -p backend/Qms.Infrastructure -s backend/Qms.Api` from the repository root.
- Tests: reuse `PostgresFixture` (`[Collection(PostgresCollection.Name)]`): `NewDatabaseAsync(seeded)`, `CreateContext(connectionString, companyId, timeProvider)`, and `SettableCurrentTenant` (tests only). For the readiness test stop the container or use `NewBlankDatabaseAsync`.
- Schema facts the queue engine needs: enums are text; `token.business_date` and `token_sequence.business_date` are `date`; issue with `INSERT ... ON CONFLICT (branch_id, business_date, priority_type_id, service_type_id) DO UPDATE SET last_sequence = token_sequence.last_sequence + 1 RETURNING last_sequence` (tested with 25 parallel issues); Call Next orders `is_front DESC, queued_at` and uses partial index `ix_token_visit_queue` (`WHERE ended_at IS NULL`; the plan shows no sort, tested). Unique `ux_token_branch_date_number`. `token` and `token_visit` have `company_id`; `token_visit` has no `branch_id` (zone gives the branch).
- Deleting a row that a lower mapping level uses fails with PostgreSQL 23001 (all foreign keys restrict except config children: dispenser_setting, token_visit, refresh_token, user_branch, user_counter). Entity navigations are reference-only (no collections).
- Handlers must still validate the chain in `Qms.Application` for friendly errors; the database is the backstop. Dispenser rows (spec 4.4 "zone has a counter for the service" warning, service must be in the branch mapping) are NOT foreign-key backed because a dispenser row has no priority; validate in Application.
Design decisions (spec section 9 left room; none contradicts it, see also Open questions):
1. Tenant scoping: `company_id` is on every tenant table, including branch-level tables (zone, counter, device, token, ...), not only the ones section 9 lists, so the ADR-003 filter is one comparison. `branch_*`, `zone`, `counter`, `device`, `token`, `token_sequence`, `service_flow`, `dispenser_zone_service` reference `branch (company_id, id)` with a composite key so the company cannot differ from the branch's.
2. Mapping chain: `service_type` and `priority_type` have alternate key `(company_id, id)`; `company_service_priority` is unique on `(company_id, service_type_id, priority_type_id)`; `branch_service_priority` carries `company_id` and has FK `(company_id, service_type_id, priority_type_id)` to it and is unique on `(branch_id, service_type_id, priority_type_id)`; `counter_service_priority` carries `branch_id` and has FK `(branch_id, service_type_id, priority_type_id)` to the branch mapping and FK `(branch_id, counter_id)` to `counter`. Zone / device / counter references inside a branch use `(branch_id, id)` alternate keys. A Not Available branch pair stays mappable.
3. `AppUser` / `AppRole` live in Infrastructure (Identity is a framework dependency; Domain has none). `AppUser` adds `company_id` (null for Super Admin), `display_name`, `language` (en/ar, the `lang` claim), `is_active`, `created_at`, `updated_at` for ADR-004. Identity tables are renamed to snake_case (`asp_net_users` ...).
4. `device.registration_code_expires_at` added (ADR-004: single use, 24 hours). `device.zone_id` is nullable (lobby display shows all zones; the spec says "installed zone"). `dispenser_zone_service.branch_id` added for the same-branch foreign keys.
5. `counter.name` stays a single column as in section 9 (seed "Counter 1"); zones, services, priority types, branches have `name_en` / `name_ar`.
6. Extra checks beyond the story list: currency code 3 upper-case letters, `close_time > open_time`, calendar date range and SpecialHours times, `token.sequence` 1-9999, `last_sequence` 0-9999, non-negative fees, counter number >= 1, `from_service <> to_service` in a flow. Unique `(branch_id, day_of_week, open_time)` allows split shifts.
7. Queue index is `(zone_id, service_type_id, priority_type_id, is_front DESC, queued_at) WHERE ended_at IS NULL` as in spec section 9 (the database-design skill adds `AND called_at IS NULL`; spec wins). Seed: `target_minutes` null, `wait_alert_minutes` null, `block_when_no_counter` false, dispenser phone settings +971 / 9 digits, both dispensers installed in Zone 1. Enum `MessageStatus` (Queued / Sent / Failed) and `PrinterStatus` (Unknown / Ok / PaperOut / CoverOpen / Error) values are my choice (spec lists none).
8. Migration is named `InitialSchema` as TASK-002 says (the database-design skill suggests `YYYYMMDD_<Story>_<What>`; EF prefixes a timestamp anyway). Migration files are marked generated code through `Persistence/Migrations/.editorconfig` so the file-scoped-namespace style rule does not fail the build. `dotnet-ef` is a local tool in `.config/dotnet-tools.json` at the repository root.
Verification (Release): `dotnet build backend/Qms.slnx -c Release` 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release` Passed 98, Failed 0, Skipped 0 (37 existing + 61 new). Tables created by the migration: __EFMigrationsHistory, asp_net_role_claims, asp_net_roles, asp_net_user_claims, asp_net_user_logins, asp_net_user_roles, asp_net_user_tokens, asp_net_users, audit_log, branch, branch_calendar, branch_hours, branch_priority_fee, branch_service_priority, branch_setting, company, company_service_priority, counter, counter_service_priority, device, dispenser_setting, dispenser_zone_service, message_log, messaging_setting, priority_type, refresh_token, service_flow, service_type, ticket_template, token, token_sequence, token_visit, user_branch, user_counter, zone.
Open questions (for the architect; I did not change the spec):
- Spec section 9 should list the additions in decisions 1, 3 and 4 (company_id on branch-level tables, `branch_id` on `counter_service_priority` and `dispenser_zone_service`, `device.registration_code_expires_at`, nullable `device.zone_id`, user columns). ADR request: please confirm or amend; nothing blocks Stage 3.
- Branch 1 working hours remain sample data (story open question unchanged). ADR-003 and ADR-004 were treated as accepted.

### Stage 3 — Backend — backend-developer — 2026-11-08
Result: Done
Changed:
- Infrastructure: `backend/Qms.Infrastructure/DependencyInjection.cs` (`AddDbContext<QmsDbContext>` with `UseQmsPostgres` + `AuditableEntityInterceptor(TimeProvider)`, `DevSeeder` scoped, `GetRequiredConnectionString`, `MigrateAndSeedAsync`).
- Api: `backend/Qms.Api/Program.cs` (fail-fast connection string check; Development-only migrate + seed), `backend/Qms.Api/Common/ApplicationBuilderExtensions.cs` (ready check, `ICurrentTenant`), new `Common/DatabaseHealthCheck.cs`, new `Common/NoCompanyCurrentTenant.cs`.
- Tests: new `backend/Qms.Tests/Api/DatabaseHostingTests.cs`, `Infrastructure/InfrastructureRegistrationTests.cs`, `Support/QmsApiFactory.cs` (optional `connectionString`).
- Docs: `CLAUDE.md` (dotnet tool restore), `backend/README.md`, TASK-003 (Done). `docker-compose.yml` unchanged (already passes `ConnectionStrings__Qms` from `.env` values).
Notes for next stage: No endpoint or event changes except `/health/ready`, which now includes the `postgresql` check (503 when the database is down). Stages 4 and 5 are N/A. For STORY-003: replace `NoCompanyCurrentTenant` with a claims-based `ICurrentTenant` (registered scoped in `AddQmsHealthChecks` today; move it to the auth setup). Retry-on-failure is off on purpose (explicit transactions in the queue engine). `QmsApiFactory(connectionString: ...)` gives tests a real database; without it the host skips migrate/seed and the DB ready check. Verification: `dotnet build backend/Qms.slnx -c Release` 0 warnings, 0 errors; `dotnet test` Passed 110, Failed 0, Skipped 0 (twice).
Open questions: none

### Stage 4 — Frontend — frontend-developer — 2026-11-08
Result: N/A — No UI (stage plan)
Changed: none
Notes for next stage: none
Open questions: none

### Stage 5 — Android — android-developer — 2026-11-08
Result: N/A — No dispenser change (stage plan)
Changed: none
Notes for next stage: Ready for code review (stage 6).
Open questions: none
