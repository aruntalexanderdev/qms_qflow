# ADR-008 — Setup API conventions (milestone M2)

Status: Proposed
Date: 2026-11-08
Story: STORY-005 to STORY-013 (M2 Setup API)

## Context
M2 adds about 60 setup endpoints in nine stories (spec §10, §12 M2 "all setup endpoints tested; invalid mapping rows rejected; role and branch checks enforced"). The spec lists routes but not verbs per item, paging, concurrency, delete behaviour, audit mechanics or the role matrix per endpoint. M1 left these open on purpose: there is no `IBranchScope`, no `[Audited]` decorator (ADR-002 §3), no database access port in `Qms.Application`, no mapping for PostgreSQL 23001/23503/23505, and enums serialise as numbers. All nine stories need the same answers, so they are decided once here.

## Decision

### 1. Data access from Qms.Application
- `Qms.Application` gets an `IQmsDbContext` port (DbSets of the Domain entities, `SaveChangesAsync`, `BeginTransactionAsync`). `QmsDbContext` implements it. Application references the `Microsoft.EntityFrameworkCore` package (no project reference to Infrastructure; the architecture tests stay green). No generic repositories.
- Identity users (`AppUser` lives in Infrastructure) stay behind ports, as in STORY-003: `IUserAdministration` (STORY-011), implemented with `UserManager<AppUser>`.
- Reads project to DTOs (`Select`), `AsNoTracking`, no lazy loading.

### 2. Routes and verbs
All under `/api/v1`. Collections keep the spec §10 shape (nested under their parent); items of a branch-level collection are addressed at the top level by their own id.

| Kind | Verbs | Example |
|---|---|---|
| Top-level collection | `GET` list, `POST` create | `/service-types`, `/branches`, `/ticket-templates`, `/users` |
| Nested collection | `GET` list, `POST` create | `/branches/{branchId}/zones`, `/zones/{zoneId}/counters`, `/branches/{branchId}/flows`, `/branches/{branchId}/devices`, `/branches/{branchId}/calendar` |
| Item | `GET`, `PUT`, `DELETE` | `/service-types/{id}`, `/zones/{id}`, `/counters/{id}`, `/flows/{id}`, `/devices/{id}`; calendar entries `/branches/{branchId}/calendar/{entryId}` (owned rows, never referenced) |
| Whole-set resource | `GET`, `PUT` | `/company-service-mapping`, `/branches/{id}/service-mapping`, `/counters/{id}/service-mapping`, `/devices/{id}/zone-service-mapping`, `/branches/{id}/priority-fees`, `/branches/{id}/hours` |
| Singleton | `GET`, `PUT` | `/branches/{id}/settings`, `/devices/{id}/dispenser-settings` |
| Action | `POST` | `/devices/{id}/registration-code`, `/users/{id}/password` |

- `PUT` replaces all editable fields (no PATCH). `POST` create → 201 with `Location` and the DTO; `PUT` → 200 with the DTO; `DELETE` → 204; whole-set `PUT` → 200 with the saved set.
- `/branches/{id}/hours` is a whole-week set (spec says CRUD): a weekly schedule is edited and validated (no overlaps) as one unit.
- Route additions beyond the §10 table are listed under "Spec impact".

### 3. Contracts
- Api request records `Create<Entity>Request`, `Update<Entity>Request`, `Save<Set>Request` (e.g. `SaveBranchServiceMappingRequest`); controllers map them to Application `…Command` records (route ids + body). Responses are Application `<Entity>Dto` / `<Set>Dto` records. Validators are on the commands (the ADR-002 decorator runs them).
- JSON: camelCase; **enums as strings** (`JsonStringEnumConverter` registered globally; affects no existing contract, `role` is already a string); money as a JSON number with at most 2 decimals; times `"HH:mm"`; dates `"yyyy-MM-dd"`; timestamps ISO 8601 UTC.
- Every DTO carries `id` and `version`; tenant ids (`companyId`) are never accepted in a request body (they come from the token).

### 4. Lists
- Setup lists are small (tens of rows): no paging. Fixed sort: `sortOrder`, then `nameEn` (service types, zones), `priorityLevel` then `prefix` (priority types), `number` (counters), `code` (branches), `name` (devices). Inactive rows are included with `isActive`; optional `?isActive=true|false` filter where the entity has the flag.
- **Users are the exception** (up to ~1 000 per company): `GET /users?page=1&pageSize=50&search=&role=&branchId=&isActive=` → `{ items, page, pageSize, totalCount }`, `pageSize` 1–200, sorted by `displayName`.

### 5. Optimistic concurrency
- Every DTO has an opaque string `version`. `PUT` requests must send the `version` they read (missing → 400); a mismatch → **409 `request.conflict`** (already produced by `GlobalExceptionHandler` for `DbUpdateConcurrencyException`). `DELETE` takes no version.
- Rows: the PostgreSQL system column `xmin`, mapped once in `EntityConfiguration<T>` as a row version (`uint`, `IsRowVersion()`; Npgsql creates no column). The handler sets the original value from the request before saving. Stage 2 adds it with a model-only migration in STORY-005.
- Users: Identity's `ConcurrencyStamp`.
- Whole-set resources: `version` = hash of the sorted (row id, `xmin`) pairs of the set. The `PUT` transaction first locks the parent row (`SELECT … FOR UPDATE` on company / branch / counter / device), recomputes the hash and returns 409 if it differs. This serialises concurrent saves of one grid.

### 6. Whole-set PUT semantics (mapping grids, fees, hours)
- The body is the complete desired set. Rows missing from it are deleted, new rows inserted, changed rows updated, in one transaction. Duplicate keys in the body → 400. An empty set is allowed (subject to the in-use rules below).
- Each row is validated against the level above (spec §4 "validation rules"); the first failing rows are reported as 400 `validation.failed` with `errors` keyed `rows[i].field` and a specific `code` when all failures share one reason (e.g. `mapping.pair_not_in_company`).
- **No cascade down the mapping chain.** Removing a row that a lower level still uses is rejected with 409 and lists the users of the row (e.g. which counters map the pair). Turning a branch pair Not Available is always allowed (spec §4.3).
- Warnings (only spec §4.4 today): a successful save returns `warnings: [{ code, … }]` next to `rows`; `GET` returns the same warnings.

### 7. Delete, deactivate and database errors
- Every CRUD entity has `DELETE`. Entities with `is_active` (service type, priority type, branch, user) are also deactivated through `PUT`. Delete succeeds only while nothing references the row; history (tokens, visits) keeps rows forever, which is why deactivation exists.
- Handlers pre-check references for a friendly 409 with a specific code. The database stays the backstop: `GlobalExceptionHandler` maps a `DbUpdateException` whose inner `PostgresException` is
  - 23001 / 23503 (foreign key, restrict) → **409**, code from a constraint-name catalog, default `resource.in_use`;
  - 23505 (unique) → **409**, catalog code (e.g. `service_type.prefix_taken`), default `resource.duplicate`;
  - 23514 (check) → **400** `request.invalid`.
  The catalog (`PostgresErrorCatalog`, Infrastructure) maps constraint names → codes; the Api never parses messages. It also covers the four user-assignment constraints left open by STORY-003 (TASK-008 note): `fk_user_branch_user_company`, `fk_user_counter_user_company` → 404 `user.not_found`; `fk_user_branch_branch_company_id_branch_id`, `fk_user_counter_counter_company_id_counter_id` → 404 `resource.not_found`. A test asserts every catalog name exists in the model or migration.
- Inactive service and priority types stay valid in existing mapping rows; new mapping rows may not reference an inactive type (400 `mapping.type_inactive`). Interpretation of "is_active"; queue behaviour of inactive types is M3.

### 8. Validation (FluentValidation on every command)
- `nameEn` and `nameAr` both required, trimmed, 1–200 characters (templates' header/footer 1–500). `nameAr` is not script-checked.
- Prefix: exactly one character `A`–`Z` after trimming (no silent upper-casing); unique per company (pre-check + `23505`).
- Money: 0 to 9 999 999 999.99, at most 2 decimals. `sortOrder` ≥ 0. Codes (branch): 1–50 of `[A-Za-z0-9_-]`, unique per company. Time zone: an IANA id that `TimeZoneInfo.TryFindSystemTimeZoneById` accepts. Ids in bodies must be non-empty GUIDs.
- Validation failure → 400 `validation.failed` with `errors` (existing behaviour).

### 9. Authorization (spec §8)
Declared on controllers with role policies; branch scope in handlers (§10).

| Area | Read (`GET`) | Write |
|---|---|---|
| Service types, priority types, company mapping, ticket templates | Company Admin, Branch Manager | Company Admin |
| Branches: details, hours, calendar, settings, priority fees | Company Admin, Branch Manager (own branches; the branch list shows only them) | Company Admin |
| Branch service mapping, zones, counters, counter mapping, devices, dispenser settings and mapping | Company Admin, Branch Manager (own branches) | Company Admin, Branch Manager (own branches) |
| Service flows | Company Admin, Branch Manager (own branches) | Company Admin (see STORY-010 open question) |
| Users | Company Admin | Company Admin |
| Own language (`PUT /auth/me/language`) | — | any signed-in user |

- Super Admin has no company: every tenant setup endpoint returns **403 `auth.forbidden`** (no `[CrossTenant]` handler in M2). Counter Staff gets 403 on all setup endpoints.
- Not signed in → 401 `auth.unauthorized` (fallback policy, unchanged).

### 10. Branch scope (ADR-003 decision 4, first implementation)
- `IBranchScope` (Application, scoped; implemented in Infrastructure, assignments read once per request with `IgnoreQueryFilters()` by user id, as login does): `Task<Result> EnsureAccessAsync(Guid branchId, CancellationToken)` and `Task<IReadOnlyCollection<Guid>?> AccessibleBranchIdsAsync(CancellationToken)` (`null` = all branches of the company).
- Order in a handler: load the target (branch, or the zone / counter / device / flow that leads to it) through the tenant-filtered DbSet → not found or other company = **404** (`<entity>.not_found`, same answer for both); then `EnsureAccessAsync(branchId)` → **403 `auth.forbidden`** for a Branch Manager of another branch. Company Admin passes for every branch of the company.

### 11. Audit (spec §11, ADR-002 §3)
- Every setup command is marked `[Audited("<entity>", "<action>")]` (e.g. `[Audited("service_type", "update")]`). The `AuditDecorator` sits in the chain validation → cross-tenant → **audit** → handler. It opens a transaction, runs the handler (which may call `SaveChangesAsync`), and on success adds one `audit_log` row and commits; on failure it rolls back and writes nothing. So the change and its audit row commit together.
- Row: `company_id` (current tenant), `user_id` (`ICurrentUser.UserId`), `device_id` null, `entity`, `entity_id` (the `Id` of the result DTO, else the command's `Id`), `action`, `at` (`TimeProvider`), `data` = the command serialised as JSON minus properties marked `[NotAudited]`. Passwords are always `[NotAudited]`; phone numbers never appear in setup commands.
- Queries are never audited. Login/refresh are not setup changes and stay unaudited (unchanged).

### 12. Device configuration changes
Handlers that change what a dispenser or display shows (branch mapping, branch hours/calendar, dispenser settings and mapping, service/priority type names, flags and fees, ticket templates, device zone) call `IDeviceConfigNotifier.ConfigChangedAsync(branchId | deviceId)` **after commit**. M2 registers a no-op implementation; STORY-021 implements it with SignalR `DeviceConfigChanged`. No SignalR in M2.

### 13. Error codes
`<area>.<reason>`, snake_case, stable. Shared: `validation.failed`, `request.conflict`, `request.invalid`, `resource.not_found`, `resource.in_use`, `resource.duplicate`, `auth.forbidden`. Per entity: `<entity>.not_found`, `<entity>.in_use`, `<entity>.prefix_taken` / `.code_taken`; mapping chain: `mapping.pair_not_in_company`, `mapping.pair_not_in_branch`, `mapping.service_not_in_branch`, `mapping.pair_in_use`, `mapping.type_inactive`; warning `mapping.no_counter_for_service`. Each story lists its own codes; the web app translates every code (en + ar) in M4.

### 14. Defaults created with a parent (interpretation of spec §4 and §9)
- Creating a branch also creates its `branch_setting` (recall limit 3, skip default NoShow, block when no counter false, wait alert empty) and one zone "Main zone" / "المنطقة الرئيسية" (spec §4: "A branch with one zone gets a default Main zone"). The last zone of a branch cannot be deleted (409 `zone.last_zone`).
- Creating a dispenser device also creates its `dispenser_setting` with the spec defaults (PrintOnly, show VIP true, confirmation 8 s, languages en + ar, default en).
- A new branch has no hours, so it is closed until hours are saved (M3 issue rule 5).

### 15. Integration-test pattern (every M2 endpoint)
- Testcontainers PostgreSQL, a fresh seeded database per test class (`PostgresFixture.NewDatabaseAsync(seeded: true)`), `QmsApiFactory`.
- A shared `SetupTestData` (STORY-005) adds to the Company 1 / Branch 1 seed: **Company 2** with its own service type, priority type, branch, zone, counter and dispenser; **Branch 2** in Company 1 with a zone, counter and dispenser; users Company Admin C1, Company Admin C2, Branch Manager B1, Branch Manager B2, Counter Staff (Counter 1), Super Admin. Tokens come from `POST /auth/login` or a test token helper with the test signing key.
- Each endpoint has at least: happy path (asserting the `audit_log` row for writes); 400 validation; 401 anonymous; 403 for Counter Staff and Super Admin; **cross-company** — Company Admin C2 on a Company 1 id → 404 (and C1 list never shows C2 rows); **cross-branch** — Branch Manager B2 on a Branch 1 resource → 403 (branch-level endpoints); 409 stale `version` on `PUT`; 409 in-use on `DELETE` where references exist. Mapping endpoints add one test per spec §4 validation rule and the §4 example data (Counter 2 – Service 3 – VIP valid; Service 6 on a counter rejected).

## Consequences
- One way to write a setup endpoint: reviewers check the pattern instead of re-deciding it per story.
- STORY-005 carries the shared plumbing (sections 1, 3, 5, 7, 9–12, 15) and is the first M2 story; the others build on it.
- EF Core in `Qms.Application` couples use cases to EF's LINQ, accepted for less boilerplate; the queue engine (M3) may still use raw SQL behind the same port.
- `version` in the body (not `If-Match`) keeps the Angular forms simple; the API is not HTTP-cache friendly, which a setup API does not need.
- Deactivation instead of delete leaves rows with history in place; zones, counters and devices have no `is_active` (spec §9), so they are retired by leaving them unused (devices: regenerate the registration code to revoke the key).

## Spec impact
No business rule changes. Additions to section 10 (to be written into the spec after the product owner accepts this ADR):
- Item routes `GET/PUT/DELETE` for `/service-types/{id}`, `/priority-types/{id}`, `/branches/{id}`, `/branches/{id}/calendar/{entryId}`, `/zones/{id}`, `/counters/{id}`, `/flows/{id}`, `/ticket-templates/{id}`, `/users/{id}`, `/devices/{id}`; `GET` for every resource that §10 lists only with `PUT` (`/devices/{id}/dispenser-settings`, `/devices/{id}/zone-service-mapping`), and `GET /branches/{id}/devices`.
- `POST /devices/{id}/registration-code` (new code, revokes the device key; ADR-004 §7), `POST /users/{id}/password` (admin sets a password), `PUT /auth/me/language` (ADR-007 B.4), `GET /ticket-templates/{id}/preview` (STORY-013).
- `/branches/{id}/hours` is a whole-week `GET`/`PUT` instead of per-row CRUD.
Section 9 is not changed by this ADR (the row version is the system column `xmin`).
