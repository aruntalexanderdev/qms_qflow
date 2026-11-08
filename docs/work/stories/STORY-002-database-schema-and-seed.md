# STORY-002 — Full database schema, migration and seed data

| Field | Value |
|---|---|
| ID | STORY-002 |
| Status | QA |
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

### Stage 6 — Code review — code-reviewer — 2026-11-08
Result: Changes requested
Builds/tests: backend ✅ (`dotnet build backend/Qms.slnx -c Release --no-incremental` 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release` Passed 110, Failed 0, Skipped 0, 37 s) · web N/A · android N/A
ACs checked: AC-1 ✅, AC-2 ✅ (8/7/7/5 rows, Service 3 VIP, Counter 2 – Service 3 – VIP, VIP paid 50.00, AED, idempotent), AC-3 ✅, AC-4 ✅, AC-5 ✅, AC-6 ✅, AC-7 ✅ (the query filter works; but the database backstop has cross-company holes, see M1/M2), AC-8 ✅
Verified by hand: I applied the `InitialSchema` SQL (`dotnet ef migrations script`) to a scratch PostgreSQL 18 with two companies. Each of these inserts SUCCEEDED, and each should have failed: (a) a `dispenser_zone_service` row with `company_id` = Company 1 and Company 1's service type on Company 2's branch, dispenser and zone; (b) a `token_visit` for a Company 1 token queued in a Company 2 zone; (c) a `dispenser_setting` with `company_id` = Company 1 for a Company 2 device.

Must fix
- M1 [stage 2] backend/Qms.Infrastructure/Persistence/Configurations/TokenConfigurations.cs:50,55,56 — `token_visit.token_id`, `zone_id` and `counter_id` are single-column foreign keys. Nothing ties them to the visit's `company_id`, or to each other's branch. So a visit can sit in another company's or another branch's zone or counter. This is the queue table: Call Next (M3) is raw SQL on `zone_id` with `FOR UPDATE SKIP LOCKED`, where the EF query filter does not protect, so a bad row lets one bank's token show up at another bank's counter. Every other branch-level table uses composite keys; this one does not. → Add `token_visit.branch_id`, and add composite FKs `(company_id, branch_id)` → `branch`, `(branch_id, token_id)` → `token (branch_id, id)` (new alternate key), `(branch_id, zone_id)` → `zone`, `(branch_id, counter_id)` → `counter`. Keep `ix_token_visit_queue` as it is. Fold the change into `InitialSchema` (it is not applied anywhere yet), or add a new migration. Add MappingChain/Constraint tests: a visit in another branch's zone or counter is rejected, and so is a visit for another company's token.
- M2 [stage 2] the same cross-company gap on more tables, which breaks the stage 2 design claim that "the company cannot differ from the branch's":
  - backend/Qms.Infrastructure/Persistence/Configurations/DeviceConfigurations.cs:52-57 — `dispenser_zone_service.branch_id` is not linked to `company_id`. Proven: Company 1's service type can be mapped on Company 2's dispenser. → Add FK `(company_id, branch_id)` → `branch (company_id, id)`.
  - backend/Qms.Infrastructure/Persistence/Configurations/MappingConfigurations.cs:45-50 — `counter_service_priority.company_id` is not tied to `branch_id`, so the row can carry another company's id. That makes it visible to the wrong tenant and hidden from the right one. → Same FK `(company_id, branch_id)` → `branch`.
  - backend/Qms.Infrastructure/Persistence/Configurations/DeviceConfigurations.cs:41 — `dispenser_setting.device_id` is a single-column FK. Proven: a setting (and through it a ticket template) of Company 1 can attach to Company 2's device. → Add alternate key `device (company_id, id)` and FK `(company_id, device_id)`. Keep the cascade.
  - backend/Qms.Infrastructure/Persistence/Configurations/MessagingAndIdentityConfigurations.cs:16 — `message_log.token_id` is a single-column FK (phase 2 table, same defect). → FK `(company_id, token_id)` → `token (company_id, id)`.
  - Add one rejection test per table.

Should fix
- S1 [stage 2] backend/Qms.Infrastructure/Persistence/QmsDbContext.cs:164-173 — the tenant check compares the current `CompanyId` value. A row loaded with `IgnoreQueryFilters()` whose `CompanyId` is then set to the current company passes the check, which moves the row into the current tenant. → For Modified and Deleted entries, check `entry.OriginalValues[CompanyId]`, and make `CompanyId` immutable after insert (`SetAfterSaveBehavior(PropertySaveBehavior.Throw)`). Add a test.
- S2 [stage 2 + stage 3] backend/Qms.Infrastructure/Persistence/QmsDbContext.cs:155 and backend/Qms.Api/Common/ApplicationBuilderExtensions.cs:20 — reads fail closed, but writes fail open. With `CompanyId == null` nothing is stamped or checked, and every API request uses `NoCompanyCurrentTenant` until STORY-003. A forgotten tenant in a device endpoint or a job could write any company's rows. → Make cross-tenant writing explicit (for example `ICurrentTenant.IsCrossTenant` for seeder, Super Admin `[CrossTenant]` handlers and system jobs) and throw on tenant writes when there is neither a company nor that flag. Track this in STORY-003, where the real `ICurrentTenant` arrives.
- S3 [stage 2, or Application in STORY-003/M2] backend/Qms.Infrastructure/Persistence/Configurations/MessagingAndIdentityConfigurations.cs:49,60 — `user_branch` and `user_counter` are unfiltered, which is justified for sign-in. But nothing in the database (and no validator yet) stops a Company 2 user from getting a Company 1 branch or counter, and that would make `IBranchScope` pass. The tenant filter still hides the data, so this is defence in depth only. → Either add a `company_id` column with a composite FK to `branch` / `counter` and a check against `asp_net_users.company_id`, or validate in Application with a test (STORY-003).
- S4 [stage 2] backend/Qms.Infrastructure/Persistence/Configurations/OrganizationConfigurations.cs:78,95 — `close_time > open_time` rules out opening hours that cross midnight. That is fine as a database default, but it is an invented business rule. → Note it in the spec additions as "no overnight hours in phase 1" for the product owner to confirm, or drop it.

Nice to have
- N1 [stage 2] The Call Next test (backend/Qms.Tests/Persistence/QueueSupportTests.cs:47-81) covers a single queue only. Spec section 6 Call Next spans several pairs and orders by `priority_type.priority_level`, which lives on another table. The per-queue partial index is fine for that, because each (zone, service, priority) has one fixed level. M3 should pick per level (lowest first), then use the index per pair with `FOR UPDATE SKIP LOCKED` (not a UNION, which does not allow it). Every raw SQL statement must also carry `company_id` / `branch_id` predicates, because query filters do not apply to `ExecuteSql`.
- N2 [stage 2] Add a check that `token.number = priority_prefix || service_prefix || lpad(sequence::text, 4, '0')`, and narrow `number` from `varchar(8)` to 6 characters. This puts the fixed format into the database.
- N3 [stage 2] Device type consistency check: `counter_id IS NOT NULL` exactly when `type = 'CounterDisplay'`.
- N4 [stage 2] Generated FK names mix plural and singular forms (`fk_token_visit_zones_zone_id`, `fk_dispenser_zone_service_service_types_…`, `fk_message_log_tokens_token_id` beside `fk_token_visit_counter_counter_id`), and some are truncated at 63 characters. Consider explicit `HasConstraintName` for the composite keys, because tests and error mapping read constraint names.
- N5 [stage 2] backend/Qms.Infrastructure/Persistence/Seed/DevSeeder.cs:54,128 — `EnsureAsync` matches rows by natural key but creates them with fixed `SeedIds`. If an admin edits a seeded prefix or number in the Development database, the next start fails with a PK or FK error. Matching seeded rows by `Id` would be more robust.
- N6 [stage 2] Add a test that migrates down to `0` (the `Down` method is generated and looks complete, but nothing tests it).
- N7 [stage 3] backend/Qms.Api/Common/ApplicationBuilderExtensions.cs:20 — `ICurrentTenant` is registered inside `AddQmsHealthChecks`. Moving it to its own setup method in STORY-003 is already noted in the handoff.

Checked and fine: every §9 table and column with the Phase 0 additions (`currency_code`, `is_paid`, `fee_amount numeric(12,2)`, `branch_priority_fee` unique, `show_vip` DB default true, the five token fee fields, `token_visit.is_front`, `refresh_token` per ADR-004 #2), enums as text, `timestamptz` / `date`, uuid v7. Mapping chain: composite FKs for company → branch → counter, Not Available pairs stay mappable, delete restricted (cascade only for config children, all sensible). `token_sequence` unique index supports `ON CONFLICT … RETURNING` (25 parallel issues tested). The queue partial index matches spec §9 (spec wins over the skill's `called_at IS NULL`). Sensitive-data logging is off, no secrets in appsettings or compose, the readiness response body shows no exception text. Migrate and seed run only when `IsDevelopment()`, and `Database:MigrateOnStartup` can only switch them off, never on in Production (covered by `Production_startup_does_not_migrate_or_seed`). The model-drift test exists, and the `.editorconfig` generated-code exception covers only `Persistence/Migrations/`. Tests are deterministic and use their own databases.

Spec §9 additions (stage 2 decisions), my view for the product owner:
- Approve: `company_id` on all branch-level tables; `branch_id` on `counter_service_priority` and `dispenser_zone_service` (needed for the composite FKs, but only safe after M2); `device.registration_code_expires_at` (ADR-004 #7); nullable `device.zone_id` (lobby display); `AppUser` columns `company_id`, `display_name`, `language`, `is_active`, `created_at`, `updated_at` (ADR-004 claims); `PrinterStatus` values (they match spec §7 "paper out, cover open"); the currency, prefix, sequence and fee checks.
- Add as well: `token_visit.branch_id` (from M1).
- Do NOT write into the spec now: the `MessageStatus` values Queued / Sent / Failed (phase 2 delivery tracking will likely need Delivered / Read for WhatsApp, so leave it to the phase 2 design); `close_time > open_time` as a business rule (S4) unless the product owner confirms that branches never open overnight.

Status: stays `Code Review` (Changes requested) as instructed by the caller. Owning stage of the first Must fix: stage 2 (database-architect), then back to stage 6.
Open questions: none

### Stage 2 — Database (review loop 1) — database-architect — 2026-11-08
Result: Done
Changed:
- Domain: `backend/Qms.Domain/Entities/Tokens/TokenVisit.cs` (new `BranchId`, `Branch`).
- Infrastructure: `Persistence/Configurations/{TokenConfigurations,DeviceConfigurations,MappingConfigurations,MessagingAndIdentityConfigurations,EntityConfiguration}.cs`, `Persistence/QmsDbContext.cs`, `Persistence/Migrations/*` (old `InitialSchema` and snapshot removed, one new `InitialSchema` generated with `dotnet ef migrations add InitialSchema -o Persistence/Migrations`; pass `-o` when the snapshot is gone, otherwise EF writes to `Migrations/`). Seeder unchanged (token_visit is not seeded; the other rows already carried `branch_id`).
- Tests: new `backend/Qms.Tests/Persistence/TenantIntegrityTests.cs` (14), 5 added to `TenantAndAuditTests.cs`, `ConstraintTests.cs` (token visit now sets `BranchId`).
- Task: TASK-002 notes.
Fixes:
- M1: `token_visit.branch_id` added. Composite FKs: `(company_id, branch_id)` to branch, `(branch_id, token_id)` to token (cascade), `(branch_id, zone_id)` to zone, `(branch_id, counter_id)` to counter. Token got alternate key `(branch_id, id)`. `counter_id` stays nullable: PostgreSQL composite FKs are MATCH SIMPLE, so a null counter skips the check and a non-null counter must be in the same branch (tested both ways). Queue index `ix_token_visit_queue` is unchanged `(zone_id, service_type_id, priority_type_id, is_front DESC, queued_at) WHERE ended_at IS NULL`: a zone belongs to exactly one branch, so `branch_id` adds no selectivity and would only widen the index; spec 9 order kept. Raw Call Next SQL should still add `branch_id` / `company_id` predicates (filtered on the heap row).
- M2: `dispenser_zone_service` and `counter_service_priority` got FK `(company_id, branch_id)` to branch. `dispenser_setting` FK is now `(company_id, device_id)` to new alternate key `device (company_id, id)` (cascade kept). `message_log` FK is now `(company_id, token_id)` to new alternate key `token (company_id, id)`. The reviewer's three proofs are reproduced as tests and now fail with 23503 (named constraints asserted).
- S1: new `RejectCompanyChanges()` runs before change detection and throws `TenantViolationException` when a tracked tenant row's `CompanyId` differs from its original value (any tenant, also with no current company). It must run before `DetectChanges`: on entities whose alternate key contains `company_id` EF would otherwise throw its own InvalidOperationException first. The tenant check now judges Modified/Deleted rows by the original company. `CompanyId` also has `AfterSaveBehavior = Throw` as a backstop.
Scan of every other FK between tenant tables (no gap found beyond the above): token (branch, priority, service, zone, device), token_sequence, branch_hours / calendar / setting / priority fee / service priority, service_flow, zone, counter, device (zone, counter), ticket_template use in dispenser_setting, company_service_priority are all composite on company or branch already. `audit_log` has only the plain `company_id` FK by design (no references to other tenant rows). Remaining single-column FKs, left open on purpose: `token.issued_by_user_id`, `token.fee_settled_by_user_id`, `token_visit.user_id`, `counter.current_user_id`, `user_branch`, `user_counter` all point at `asp_net_users`, whose `company_id` is nullable (Super Admin), so a composite key would exclude Super Admin; this is the same class as S3 and belongs to Application validation in STORY-003. Also not enforced by the database: that a visit's counter is in the visit's zone (a forwarded token may be queued in a zone before any counter calls it).
Notes for next stage: no DI or wiring change; stage 3 is unaffected. Callers that create `TokenVisit` must set `BranchId` (= the token's branch). Tests that edit `CompanyId` expect `TenantViolationException`.
Verification (Release): `dotnet build backend/Qms.slnx -c Release --no-incremental` 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release` run twice: Passed 129, Failed 0, Skipped 0 (both runs; 110 before, 19 new). The model-drift test is green.
Open questions: none new. S2/S3 stay tracked for STORY-003, S4 waits for the product owner, N-items untouched. Spec section 9 additions list (architect): add `token_visit.branch_id`.


### Stage 6 — Code review (round 2) — code-reviewer — 2026-11-08
Result: Changes requested
Builds/tests: backend ✅ (`dotnet build backend/Qms.slnx -c Release --no-incremental` 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release` Passed 129, Failed 0, Skipped 0, 33 s) · web N/A · android N/A
ACs checked: AC-1 ✅ (the regenerated single `20261108021220_InitialSchema` applies cleanly to an empty PostgreSQL 18), AC-2 ✅ (seed code unchanged since 461435f, seed tests green), AC-3 ✅, AC-4 ✅, AC-5 ✅, AC-6 ✅, AC-7 ✅ (query filter; the database backstop is now closed, see below), AC-8 ✅
Reviewed: `git diff 461435f HEAD -- backend` (commit 91d08b3).

Verified by hand: I applied `dotnet ef migrations script` output to a scratch postgres:18 with Company 1 (branches B1 and B1b) and Company 2 (branch B2), each with zone, counter, dispenser, service type, priority type and token. Every one of these 11 statements is now REJECTED by the intended named constraint, and a valid visit (own token, zone and counter) is accepted:
- round-1 proof (a) `dispenser_zone_service` of Company 1 on Company 2's branch, device and zone → `fk_dispenser_zone_service_branch_company_id_branch_id`;
- round-1 proof (b) `token_visit` of a Company 1 token in a Company 2 zone → `fk_token_visit_zones_branch_id_zone_id` (with `branch_id` = B1) and `fk_token_visit_branch_company_id_branch_id` (with `branch_id` = B2);
- round-1 proof (c) `dispenser_setting` of Company 1 on Company 2's device → `fk_dispenser_setting_device_company_id_device_id`;
- new: visit with a counter of another branch of the same company (insert, and UPDATE of an existing valid visit) → `fk_token_visit_counter_branch_id_counter_id`; an all-Company-2 visit pointing at a Company 1 token → `fk_token_visit_token_branch_id_token_id`; a Company 2 `dispenser_setting` with a Company 1 ticket template → `fk_dispenser_setting_ticket_templates_…`; `counter_service_priority` carrying Company 2's id on a Branch 1 counter → `fk_counter_service_priority_branch_company_id_branch_id`; `message_log` of Company 1 for a Company 2 token → `fk_message_log_tokens_company_id_token_id`; UPDATE of a token's `company_id` → `fk_token_branch_company_id_branch_id`.
- FK scan from `pg_constraint`: every table with `branch_id` has a `(company_id, branch_id)` FK to `branch`, except `user_branch`. The only single-column FKs between non-company tables are the six to `asp_net_users` plus `user_branch.branch_id` and `user_counter.counter_id`. That is exactly stage 2's list.
- Stage 2's justifications: (1) FKs to `asp_net_users`: these hold. A composite key would block Super Admin (`company_id` NULL) as issuer, settler or server, so Application validation in STORY-003 is the right place. This is tracked with S3. (2) `audit_log`: this holds. It references no tenant rows except `company`, and `entity_id` is polymorphic. (3) Visit counter not tied to the visit's zone: I accept the decision, because the rule belongs to Call Next / Transfer in Application and a database rule could block a later spec flow. The stated reason is not quite right, though (see N8).
- `ix_token_visit_queue` is unchanged: `(zone_id, service_type_id, priority_type_id, is_front DESC, queued_at) WHERE ended_at IS NULL`, which matches spec §9. Leaving `branch_id` out of it is correct.

M1 ✅ fixed. M2 ✅ fixed (all four tables, plus one rejection test each, plus positive controls).
S1 is partly fixed. The tracked path is correct and tested: the original value is checked; Modified and Deleted rows are judged by their original company; `RejectCompanyChanges` runs with `AutoDetectChangesEnabled` off, so it fires before EF's key-modification error; it also works with no current company; and normal tracked updates and detached `Update()` of own rows still work (I checked both by hand against the scratch database). But the `AfterSaveBehavior = Throw` backstop opened a new gap, see R2-M1.

Must fix
- R2-M1 [stage 2] backend/Qms.Infrastructure/Persistence/Configurations/EntityConfiguration.cs:66-67 and backend/Qms.Infrastructure/Persistence/QmsDbContext.cs:182-194 — a disconnected (detached) entity of another company can still be updated or deleted. The tenant check trusts the entity's in-memory `CompanyId` as the "original" value, and the SQL is `WHERE id = @id` only. Proven against the scratch database with Company 1 as the current tenant. I loaded Company 2's row with `AsNoTracking()` (an API handler would simply build it from route id + DTO), set `CompanyId = Company1`, and called `db.Update(row)` / `db.Remove(row)`. Each SaveChanges SUCCEEDED and changed Company 2's data: the service type was renamed, the counter was renamed, and the dispenser device was DELETED. This change made it worse. Before it, `counter`, `device` and the other tables without a `company_id` key had `company_id` in the UPDATE SET list, so the composite `(company_id, branch_id)` FK rejected the forged row. Now `AfterSaveBehavior = Throw` drops `company_id` from SET, and that database backstop is gone for every tenant table. `Update(id-from-route)` is the usual IDOR pattern, and it is one handler away from STORY-003 / M2 admin endpoints. → Mark `CompanyId` as a concurrency token as well (`.IsConcurrencyToken()` in `EntityConfiguration`). EF then emits `WHERE id = @id AND company_id = @original` for every UPDATE and DELETE of a tenant row. A forged detached row hits 0 rows and gets `DbUpdateConcurrencyException`, so the database decides, whatever pattern the handler uses. Keep `AfterSaveBehavior = Throw` and `RejectCompanyChanges`. Add tests: detached `Update()` and detached `Remove()` of another company's row with a forged `CompanyId` both throw and leave the row unchanged (for one table with `company_id` in a key, e.g. `service_type`, and one without, e.g. `counter`); detached `Update()` of an own row still succeeds. Check that the model-drift test stays green (a concurrency token is not a schema change, so no migration is expected).

Should fix
- none new. S2 and S3 stay tracked for STORY-003; S4 waits for the product owner (not blockers, as agreed).

Nice to have
- N8 [stage 2] TASK-002 / the loop-1 handoff explains "visit counter vs zone not enforced" with "a forwarded token may be queued in a zone before any counter calls it". That case is already covered by MATCH SIMPLE (null counter). The real reason is that the counter-in-zone rule belongs to Call Next / Transfer in Application. Please correct the note so M3 picks up the rule there.
- N1–N7 from round 1 are unchanged.

Status: stays `Code Review` (Changes requested). Owning stage of the first Must fix: stage 2 (database-architect), then back to stage 6 (round 3; loop 2 of 3).
Open questions: none


### Stage 2 — Database (review loop 2) — database-architect — 2026-11-08
Result: Done
Changed:
- `backend/Qms.Infrastructure/Persistence/Configurations/EntityConfiguration.cs` (`CompanyId` is `.IsConcurrencyToken()`; `AfterSaveBehavior = Throw` kept).
- `backend/Qms.Tests/Persistence/TenantIntegrityTests.cs` (10 new tests).
- Task: TASK-002 notes. No migration, no snapshot change (a concurrency token is model metadata only; the model-drift test is green).
Fixes:
- R2-M1: EF now emits `UPDATE ... WHERE id = @id AND company_id = @originalCompanyId` and the same for `DELETE`. For a detached entity the "original" value is whatever the caller set, so a forged row (another company's id with `CompanyId` overwritten to the current company) matches 0 rows and SaveChanges throws `DbUpdateConcurrencyException`; the database decides, whatever pattern the handler uses. Kept: `RejectCompanyChanges`, the original-company tenant check for tracked rows, and `AfterSaveBehavior = Throw`.
- Exception type: I kept EF's `DbUpdateConcurrencyException` and did not translate it to `TenantViolationException`. A 0-row result cannot be told apart from a genuine concurrent delete/update of an own-company row, and mapping both to a tenant error would mask real conflicts. Stage 3 should map `DbUpdateConcurrencyException` to 409 (or 404) in the problem-details handler; it is never a success.
- Own-company detached rows still work (`Update`, stub `Remove`). Cascade check: a forged device delete fails before any cascade, `dispenser_setting` stays.
Tests added (all in `TenantIntegrityTests`; database unchanged afterwards, asserted on a fresh context): detached `Update()` and `Remove()` of another company's `service_type` and `counter`; stub `new Counter { Id, CompanyId = current }` removed; `Attach` + modify; `Entry(x).State = Modified`; forged `Remove` of a device with a `dispenser_setting` (both stay); detached `Update()` and stub `Remove()` of own-company rows succeed; `ExecuteUpdate`/`ExecuteDelete` (counter, device, service_type) affect 0 foreign rows and exactly the 6 own service types. Proof the tests bite: with `IsConcurrencyToken()` removed 8 of them fail (the `Update`/`Remove`/stub/`Attach`/`Entry` cases and the device cascade case); with it, all pass.
N8: corrected in TASK-002: the counter-in-zone rule belongs to Call Next / Transfer in Application (M3: STORY-016, STORY-018), not the database. The loop-1 line above ("a forwarded token may be queued in a zone before any counter calls it") is superseded by this note.
Notes for next stage: stage 3 must treat `DbUpdateConcurrencyException` on tenant rows as a conflict/not found and must still load rows through the filtered DbSets (the filter remains the first line of defence). Bulk `ExecuteUpdate/ExecuteDelete` respect the global filter; with `IgnoreQueryFilters()` they do not, so keep that to `[CrossTenant]` code.
Verification (Release): `dotnet build backend/Qms.slnx -c Release --no-incremental` 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release` run twice: Passed 139, Failed 0, Skipped 0 (both runs; 129 before, 10 new).
Open questions: none


### Stage 6 — Code review (round 3) — code-reviewer — 2026-11-08
Result: Approved
Builds/tests: backend ✅ (`dotnet build backend/Qms.slnx -c Release --no-incremental` 0 warnings, 0 errors; `dotnet test backend/Qms.slnx -c Release` Passed 139, Failed 0, Skipped 0, 45 s) · web N/A · android N/A
ACs checked: AC-1 ✅ (no migration or snapshot change since 2c2002f; the model-drift test is green), AC-2 ✅ (seed code unchanged; seed tests green), AC-3 ✅, AC-4 ✅, AC-5 ✅, AC-6 ✅, AC-7 ✅ (query filter, plus the original-company check for tracked rows, plus the `company_id` concurrency predicate for detached rows), AC-8 ✅
Reviewed: `git diff 2c2002f HEAD -- backend` (commit 2476486). Only two files changed: `EntityConfiguration.cs` (one line, `.IsConcurrencyToken()`) and `TenantIntegrityTests.cs` (10 tests).

R2-M1 ✅ fixed.
- Coverage: every tenant entity is covered. All 28 configurations derive from `EntityConfiguration<T>`. The only other configuration, `AppUserConfiguration`, is not a tenant entity. The token is set inside the `ITenantEntity` branch, so no tenant table can miss it. `AfterSaveBehavior = Throw` and `RejectCompanyChanges` are kept.
- Exploit re-run: the round-2 exploit is now a test, run with Company 1 as the current tenant. Steps: load Company 2's row with `AsNoTracking`, set `CompanyId = Company1`, then `Update`/`Remove`, then SaveChanges. Every path throws `DbUpdateConcurrencyException`:
  - `service_type` (company_id in a key) and `counter` (no company key), both update and remove;
  - the stub `new Counter { Id = foreignId, CompanyId = current }` remove (IDOR pattern);
  - `Attach` followed by a modify;
  - `Entry(x).State = Modified`;
  - a forged `device` remove with a cascading `dispenser_setting`.
  Company 2's data is checked afterwards on a fresh context and is unchanged in every case.
- Bulk paths: `ExecuteUpdate`/`ExecuteDelete` are scoped by the query filter. Foreign ids affect 0 rows, and an unfiltered bulk update touches exactly Company 1's 6 service types.
- Tests bite: I removed `.IsConcurrencyToken()` in a scratch copy of HEAD and ran `TenantIntegrityTests`. Result: 8 failed / 16 passed, the same 8 tests stage 2 named. The repo was not touched and the scratch copy is deleted.
- No regressions: legitimate own-company writes still work, tracked (existing tests) and detached (`Update` and stub `Remove`, new test). `CompanyId` never changes after insert, so the token cannot cause false conflicts on own rows. The seeder and all seed tests are green.

Question: `DbUpdateConcurrencyException` not translated to `TenantViolationException`. I accept this for STORY-002. A 0-row result cannot be told apart from a genuine concurrent change of an own row, so a tenant error would be wrong for real conflicts. The behaviour fails closed: nothing is written. Today `GlobalExceptionHandler` (backend/Qms.Api/Common/ProblemDetailsSetup.cs) returns a generic 500 with no details, and this story has no write endpoints. The mapping belongs with the first write endpoints, so it is follow-up S5, not a Must fix.

Must fix
- none

Should fix (follow-ups)
- S2 [stage 2 + stage 3] → STORY-003: fail-closed tenant writes when there is no company (explicit cross-tenant flag). Unchanged from round 1.
- S3 [stage 2 or Application] → STORY-003: `user_branch` / `user_counter` company consistency, plus Application validation of the FKs to `asp_net_users` (issuer, settler, server). Unchanged.
- S4 [stage 2] → product owner: `close_time > open_time` (no overnight hours) is to be confirmed in the spec additions. Unchanged.
- S5 [stage 3] → STORY-003 (first write endpoints): map `DbUpdateConcurrencyException` to problem details with 409 `request.conflict`, or 404 when the handler re-reads through the filtered DbSet and finds no row. Do the same for `TenantViolationException`, which is also a generic 500 today. Add an integration test per mapping.

Nice to have
- N9 [stage 2] Add a model test asserting that every `ITenantEntity` has `CompanyId` with `IsConcurrencyToken` and `AfterSaveBehavior = Throw`. Then a future configuration that bypasses `EntityConfiguration<T>` cannot silently drop the guard.
- N10 [stage 2] backend/Qms.Tests/Persistence/TenantIntegrityTests.cs, `A_detached_row_of_the_current_company_can_still_be_updated_and_removed`: `db.ChangeTracker.Clear()` does nothing, because the stub is removed through the separate `other` context. Remove it, or use one context.
- N8 ✅ done (TASK-002 note corrected).
- N1–N7 from round 1 are still open → N1 and the raw-SQL `company_id` / `branch_id` predicates go to the M3 Call Next / Issue stories (STORY-016, STORY-018); N7 goes to STORY-003; N2–N6 go to a stage 2 follow-up task.

Status: `QA` (Approved; review loop 3 of 3 closed with no open Must fix).
Open questions: none
