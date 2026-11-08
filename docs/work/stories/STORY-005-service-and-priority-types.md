# STORY-005 — Service types and priority types CRUD (incl. paid flag and fee) and the shared setup-API plumbing

| Field | Value |
|---|---|
| ID | STORY-005 |
| Status | Backend |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 4 (entities), 4.1 (priority types table), 5.1 step 2, 6 (Paid priority), 8 (Company Admin; `/admin/service-types`, `/admin/priority-types`), 9 (`service_type`, `priority_type`, `audit_log`), 10 (`/service-types`, `/priority-types`), 11 (Audit, Security), 12 (M2) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **to create, edit, deactivate and delete my company's service types and priority types (including whether a priority is paid and its fee)**, so that **the mapping levels and the dispenser can offer them**.

## 2. Background
First story of M2 (spec §12: "CRUD and mapping endpoints … validation chain from section 4"). Spec §5.1 step 2 is "Service types and priority types (paid, fee)". The tables, check constraints and unique prefixes exist since STORY-002; no endpoint writes them yet.

This story also builds the plumbing every M2 story uses, decided in **ADR-008** (setup API conventions): `IQmsDbContext` port, row-version concurrency (`version`), `[Audited]` decorator with transaction, PostgreSQL error catalog (23001/23503/23505/23514), enums as strings, role policies, `IBranchScope` (first implementation of ADR-003 decision 4), `IDeviceConfigNotifier` no-op, and the integration-test data set (Company 2, Branch 2, role users). The plumbing comes first so STORY-006 to STORY-013 can run without re-deciding it.

## 3. Scope
**In scope**
- Company Admin API for service types and priority types: list, get, create, update (including deactivate / reactivate), delete (spec 8, 10; ADR-008 §2).
- Priority type paid flag and fee (spec 4.1, 6 "Paid priority"); the company currency is returned read-only (`company.currency_code`).
- Validation, optimistic concurrency (`version`), in-use protection, audit entry for every write.
- Role matrix and company isolation for these endpoints (spec 8, 11).
- Shared M2 plumbing from ADR-008, proven by tests (AC-28 to AC-37): `IQmsDbContext`, `[Audited]` decorator, `IBranchScope`, Postgres error catalog, enums as strings, `IDeviceConfigNotifier` (no-op), `xmin` row version, shared integration-test data.

**Out of scope**
- Admin screens `/admin/service-types`, `/admin/priority-types` (STORY-023, M4); Arabic/English labels of error codes (M4).
- Company service mapping (STORY-006), branch fee overrides `branch_priority_fee` (STORY-007), who uses a type in queues (M3).
- A real `IBranchScope` consumer (STORY-007 onwards); SignalR `DeviceConfigChanged` (STORY-021); companies API (see section 10).
- Phase 2 items (SMS, WhatsApp, PhoneOnly, feedback).
- No new tables, columns or seed rows.

## 4. Business rules
| # | Rule | Source |
|---|---|---|
| BR-1 | A service type has name EN and AR, a one-letter prefix unique per company, optional target minutes, sort order, active flag. | Spec 9 `service_type`; 8 screens |
| BR-2 | A priority type has name EN and AR, a one-letter prefix unique per company, priority level (1 = highest), paid flag, fee, active flag. | Spec 4.1, 9 `priority_type` |
| BR-3 | Prefix is exactly one letter A to Z after trimming; no silent upper-casing (`h` is rejected). Token number format is `[Priority prefix][Service prefix][4-digit sequence]`. | Spec 8, 9; rules 2; ADR-008 §8 |
| BR-4 | `nameEn` and `nameAr` are both required, trimmed, 1 to 200 characters. | Spec 9 (bilingual); rules 7; ADR-008 §8 |
| BR-5 | Target minutes, when given, is greater than 0 (`ck_service_type_target_minutes`). Sort order is 0 or more. | Spec 9; ADR-008 §8 |
| BR-6 | Priority level is 1 or more. Two priority types may share a level (the spec does not forbid it). | Spec 4.1, 9 |
| BR-7 | A paid priority type needs a fee of 0 to 9 999 999 999.99 with at most 2 decimals. An unpaid priority type has no fee (the request is rejected if it sends one). | Spec 9 (`fee_amount` required when `is_paid`); 6 Paid priority; ADR-008 §8 |
| BR-8 | Currency comes from `company.currency_code` (AED for Company 1); it is returned, never written, by these endpoints. | Spec 6; qms-project VIP fee |
| BR-9 | Deactivate through `PUT isActive=false`. Delete only while nothing references the row (company mapping, flows, dispenser mapping, tokens for service types; company mapping, branch fees, tokens for priority types). Otherwise 409 with `<entity>.in_use`. | ADR-008 §7 |
| BR-10 | Inactive types stay valid in existing mapping rows; new mapping rows cannot use them (enforced in STORY-006/008). | ADR-008 §7 |
| BR-11 | `PUT` carries the `version` that was read; a mismatch is 409 `request.conflict`. | ADR-008 §5 |
| BR-12 | Only Company Admin writes; Company Admin and Branch Manager read. Super Admin and Counter Staff get 403 `auth.forbidden`. A row of another company is 404 (same answer as unknown id). | Spec 8; ADR-008 §9; ADR-003 |
| BR-13 | Every create, update and delete writes one `audit_log` row in the same transaction; failed commands write none. | Spec 11; ADR-008 §11 |
| BR-14 | `companyId` is never taken from a request body; it comes from the token. | ADR-008 §3 |
| BR-15 | Branch scope: Company Admin passes every branch of its company; a Branch Manager passes only assigned branches (403 otherwise); unknown or other-company branch is 404. | ADR-003 decision 4; ADR-008 §10 |

## 5. Acceptance criteria
Test users (`SetupTestData`, ADR-008 §15): CA-C1 (Company Admin, Company 1), CA-C2 (Company Admin, Company 2), BM-B1 (Branch Manager, Branch 1), BM-B2 (Branch Manager, Branch 2 of Company 1), CS (Counter Staff, Counter 1), SA (Super Admin). Seed: Company 1 Services 1 to 6 with prefixes A, B, C, D, F, G; Normal N level 2 free; VIP V level 1 paid 50.00 AED. Company 2 has its own service type prefix A ("Service X") and priority type N.

### Service types
**AC-1: List service types**
- Given the seeded data, When CA-C1 calls `GET /api/v1/service-types`, Then the response is 200 with six items with prefixes A, B, C, D, F, G sorted by `sortOrder` then `nameEn`; each item has `id`, `nameEn`, `nameAr`, `prefix`, `targetMinutes`, `sortOrder`, `isActive`, `version` (non-empty string); and no Company 2 row appears.

**AC-2: Filter by active flag**
- Given Service 6 (G) is deactivated, When CA-C1 calls `GET /api/v1/service-types?isActive=false`, Then only Service 6 is returned; with `?isActive=true` the other five are returned; with no filter all six are returned.

**AC-3: Get one service type**
- Given Service 2 (prefix B), When CA-C1 calls `GET /api/v1/service-types/{Service 2 id}`, Then 200 with its DTO, `prefix` is `B` and `nameAr` is the stored Arabic text unchanged.

**AC-4: Create a service type**
- Given Company 1 has no prefix H, When CA-C1 calls `POST /api/v1/service-types` with `nameEn` "Cards", `nameAr` "البطاقات", `prefix` "H", `targetMinutes` 10, `sortOrder` 7, `isActive` true (and a stray `companyId` of Company 2 in the body), Then the response is 201 with a `Location` header to `/api/v1/service-types/{id}` and the DTO; the row belongs to Company 1; and `GET` of that id returns the same data.

**AC-5: Service type validation**
- Given CA-C1, When `POST /api/v1/service-types` is sent with each of these bodies: (a) `nameEn` empty, (b) `nameAr` missing, (c) `nameEn` of 201 characters, (d) `prefix` "h", (e) `prefix` "HB", (f) `prefix` "1", (g) `prefix` empty or spaces, (h) `targetMinutes` 0, (i) `targetMinutes` -5, (j) `sortOrder` -1, Then each response is 400 `validation.failed` with `errors` naming the failing field (`nameEn`, `nameAr`, `prefix`, `targetMinutes`, `sortOrder`), and no row and no audit row are created. `targetMinutes` null is accepted. A prefix " H " is accepted and stored as `H`.

**AC-6: Service type prefix unique per company**
- Given Company 1 has Service 1 with prefix A, When CA-C1 creates a service type with prefix A, Then 409 `service_type.prefix_taken`; the same happens on `PUT` of Service 2 changing its prefix to A. When CA-C2 creates a service type with prefix B in Company 2 (Company 2's "Service X" uses A; B is free there), Then 201: prefixes are independent per company, and CA-C2 creating prefix A is 409. When two concurrent creates of prefix H race, Then exactly one gets 201 and the other 409 `service_type.prefix_taken` (database unique index as backstop).

**AC-7: Update a service type**
- Given Service 3 (prefix C) read with `version` V1, When CA-C1 calls `PUT /api/v1/service-types/{id}` with all editable fields (`nameEn` "Loans", `nameAr` "القروض", `prefix` C, `targetMinutes` 20, `sortOrder` 3, `isActive` true) and `version` V1, Then 200 with the new DTO and a `version` different from V1; a following `GET` shows the change. `PUT` replaces all fields (no partial update).

**AC-8: Stale or missing version**
- Given two admin sessions both read Service 3 with version V1, When the first `PUT` succeeds and the second `PUT` sends V1, Then the second response is 409 `request.conflict` and the row keeps the first change. When `version` is missing or empty, Then 400 `validation.failed` with an error on `version`.

**AC-9: Deactivate and reactivate a service type**
- Given Service 6 (G) is active, When CA-C1 sends `PUT` with `isActive` false and its version, Then 200 and the DTO has `isActive` false; the row still appears in the list; a later `PUT` with `isActive` true makes it active again. Deactivation is allowed even while the type is in the company mapping.

**AC-10: Delete an unused service type**
- Given the service type "Cards" (H) from AC-4 is referenced nowhere, When CA-C1 calls `DELETE /api/v1/service-types/{id}`, Then 204, the row is gone (`GET` returns 404 `service_type.not_found`) and an audit row `service_type` / `delete` exists.

**AC-11: Delete a service type in use**
- Given Service 1 (A) is in the company service mapping (and has tokens, flows or a dispenser mapping in other cases), When CA-C1 calls `DELETE` on it, Then 409 `service_type.in_use`, the row remains, and no audit row is written. The same 409 is returned when the reference is only a dispenser mapping row, only a flow row, or only a token (one test per reference kind). If the pre-check is bypassed, the database foreign key error is mapped to 409 with a code (default `resource.in_use`).

**AC-12: Not found and cross-company (service types)**
- Given a Company 1 service type id, When CA-C2 calls `GET`, `PUT` or `DELETE` on it, Then each response is 404 `service_type.not_found` (identical to a random unknown GUID), nothing changes, and CA-C2's list contains only Company 2 rows.

### Priority types
**AC-13: List priority types**
- Given the seeded data, When CA-C1 calls `GET /api/v1/priority-types`, Then 200 with VIP first (`prefix` V, `priorityLevel` 1, `isPaid` true, `feeAmount` 50.00) and Normal second (`prefix` N, `priorityLevel` 2, `isPaid` false, `feeAmount` null), each with `currencyCode` "AED", `isActive`, `version`; sorted by `priorityLevel` then `prefix`; `?isActive=` filters as in AC-2; no Company 2 rows.

**AC-14: Get one priority type**
- Given VIP, When CA-C1 calls `GET /api/v1/priority-types/{VIP id}`, Then 200 with the DTO of AC-13; `feeAmount` is the JSON number 50.00 (not a string).

**AC-15: Create an unpaid priority type**
- Given Company 1 has no prefix S, When CA-C1 calls `POST /api/v1/priority-types` with `nameEn` "Senior", `nameAr` "كبار السن", `prefix` "S", `priorityLevel` 1, `isPaid` false, no `feeAmount`, `isActive` true, Then 201 with `Location`, `feeAmount` null, `currencyCode` "AED". A body with `isPaid` false and `feeAmount` 0 is rejected (AC-17).

**AC-16: Create a paid priority type**
- Given Company 1 has no prefix P, When CA-C1 posts "Premium" / "مميز", prefix "P", `priorityLevel` 1, `isPaid` true, `feeAmount` 75.50, Then 201 and the DTO shows `isPaid` true and `feeAmount` 75.50. `feeAmount` 0 with `isPaid` true is accepted (free-of-charge paid type, `fee_amount >= 0`).

**AC-17: Priority type validation**
- Given CA-C1, When `POST /api/v1/priority-types` is sent with: (a) `nameEn` empty, (b) `nameAr` missing, (c) `prefix` "p", "PV", "7" or empty, (d) `priorityLevel` 0, (e) `priorityLevel` -1, (f) `isPaid` true and no `feeAmount`, (g) `isPaid` true and `feeAmount` 12.345, (h) `isPaid` true and `feeAmount` -1, (i) `isPaid` false and `feeAmount` 10, (j) `isPaid` true and `feeAmount` 10000000000, Then each response is 400 `validation.failed` with `errors` on the failing field (`nameEn`, `nameAr`, `prefix`, `priorityLevel`, `feeAmount`) and nothing is saved. The validator rejects (f) to (i) before the database check constraints are reached.

**AC-18: Priority type prefix unique per company**
- Given Company 1 has Normal with prefix N and VIP with prefix V, When CA-C1 creates a priority type with prefix V, or `PUT`s Normal to prefix V, Then 409 `priority_type.prefix_taken`. A priority type and a service type may share a letter (prefix V on a priority type and a service type V are independent). CA-C2 may create prefix V in Company 2.

**AC-19: Update a priority type and its fee**
- Given VIP with version V1, When CA-C1 `PUT`s VIP with `isPaid` true, `feeAmount` 60.00 and `version` V1 (other fields unchanged), Then 200, the DTO shows 60.00 and a new `version`, and `GET` returns 60.00. Tokens already issued keep the fee they were issued with (no change to token rows in this story).

**AC-20: Change a priority type from paid to unpaid**
- Given VIP is paid at 50.00, When CA-C1 `PUT`s `isPaid` false with `feeAmount` null, Then 200 with `feeAmount` null. When `isPaid` false is sent with `feeAmount` 50.00, Then 400 (AC-17 i). Existing `branch_priority_fee` rows for VIP are not deleted (interpretation, section 10). When `isPaid` is changed back to true with a fee, Then 200.

**AC-21: Stale or missing version (priority types)**
- Given two sessions read Normal with version V1, When the first `PUT` succeeds and the second sends V1, Then 409 `request.conflict`. A missing `version` is 400 `validation.failed`.

**AC-22: Deactivate a priority type**
- Given Normal is active, When CA-C1 sends `PUT` with `isActive` false, Then 200 with `isActive` false, the row stays in the list and `?isActive=false` returns it.

**AC-23: Delete a priority type**
- Given the "Senior" type from AC-15 is unused, When CA-C1 calls `DELETE`, Then 204 and an audit row `priority_type` / `delete`. Given VIP is in the company mapping (Services 2 and 3), When CA-C1 calls `DELETE` on VIP, Then 409 `priority_type.in_use`, the row stays and no audit row is written. One test per reference kind (company mapping, branch fee, token).

**AC-24: Not found and cross-company (priority types)**
- Given a Company 1 priority type id, When CA-C2 calls `GET`, `PUT` or `DELETE`, Then each is 404 `priority_type.not_found`; the Company 1 row is unchanged; CA-C2's list shows only Company 2 rows.

### Roles and access
**AC-25: Role matrix**
- Given both resources, When each user calls `GET` list and `GET` item: CA-C1 and BM-B1 get 200. When BM-B1 calls `POST`, `PUT` or `DELETE`, Then 403 `auth.forbidden` and nothing changes. When CS or SA calls any of the ten endpoints (including `GET`), Then 403 `auth.forbidden`. When no token is sent, Then 401 `auth.unauthorized`. BM-B2 (another branch of Company 1) has the same read access as BM-B1, because these resources are company-wide.

### Audit
**AC-26: Audit entry for every write**
- Given CA-C1, When it creates "Cards" (H), updates VIP to 60.00, and deletes "Cards", Then `audit_log` gains three rows in order, each with `company_id` Company 1, `user_id` CA-C1, `device_id` null, `entity` `service_type` / `priority_type`, `action` `create` / `update` / `delete`, `entity_id` the affected row id, `at` from `TimeProvider` (UTC), and `data` holding the command JSON (for the update: `feeAmount` 60.00 and the `version` sent). Reads (`GET`) write no audit row.

**AC-27: Failed write writes nothing**
- Given a `POST` with a taken prefix (409), a `PUT` with a stale version (409) and a `POST` failing validation (400), When each completes, Then no row changed and `audit_log` has no new row for any of them; when the handler succeeds but the audit insert fails (test double), the data change is rolled back too.

### Shared plumbing
**AC-28: Enums as strings**
- Given a test-host endpoint that returns and accepts an enum (for example `TokenStatus.Waiting`), When it is called, Then the JSON contains `"Waiting"`, not a number, and a request body with the string value is accepted; the existing `role` claim response and the auth contract are unchanged (STORY-003 tests stay green).

**AC-29: Postgres error catalog**
- Given test-host endpoints that raise each database error, When they run, Then: SQLSTATE 23505 on the unique index `(company_id, prefix)` of each table (the real index names from the migration, `ix_service_type_company_id_prefix` style) returns 409 with the catalog code `service_type.prefix_taken` / `priority_type.prefix_taken`, an unknown unique constraint returns 409 `resource.duplicate`; 23503 / 23001 returns 409 with the catalog code or default `resource.in_use`; 23514 on a check returns 400 `request.invalid`; the four user-assignment constraints `fk_user_branch_user_company`, `fk_user_counter_user_company` return 404 `user.not_found` and `fk_user_branch_branch_company_id_branch_id`, `fk_user_counter_counter_company_id_counter_id` return 404 `resource.not_found`; the response never contains the SQL message or constraint text; `DbUpdateConcurrencyException` still returns 409 `request.conflict`. A test asserts that every constraint name in the catalog exists in the EF model or migration.

**AC-30: Branch scope**
- Given Company 1 has Branch 1 and Branch 2 and Company 2 has its own branch, and a test-host endpoint that calls `IBranchScope`, When CA-C1 asks for Branch 1 and Branch 2, Then both pass and `AccessibleBranchIdsAsync` is `null` (all branches). When BM-B1 asks for Branch 1, Then it passes, and `AccessibleBranchIdsAsync` returns only Branch 1. When BM-B1 asks for Branch 2, Then 403 `auth.forbidden`. When BM-B1 or CA-C1 asks for a Company 2 branch id or a random GUID, Then 404 `resource.not_found` (the handler loads the branch through the tenant-filtered set first, so the answer is the same for both). The assignments are read once per request (a test counts one `user_branch` query for several calls).

**AC-31: Audit decorator mechanics**
- Given a test command marked `[Audited("test_entity", "create")]` with a property marked `[NotAudited]`, When it runs through the pipeline, Then the order is validation, cross-tenant, audit, handler; the audit row holds `entity_id` from the result DTO `Id` (or the command `Id` when the result has none) and `data` without the `[NotAudited]` property; a command without `[Audited]` or with `[NotAudited]` on the type writes no row; a failing validator stops before any transaction opens; a handler error rolls back and writes no row.

**AC-32: Version helpers**
- Given an entity row with `xmin` 1234, When it is mapped to a DTO, Then `version` is an opaque non-empty string that converts back to 1234; an unparsable string is a 400 validation error on `version`, not a 500. The set version for a grid is the same for the same (row id, `xmin`) pairs in any order and differs when any `xmin` or row id differs. (Used by STORY-006 and later.)

**AC-33: Device configuration notifier**
- Given `IDeviceConfigNotifier` is replaced by a recording test double, When CA-C1 changes a service type name, prefix or `isActive`, or a priority type name, prefix, `isActive`, `isPaid` or fee, Then `ConfigChangedAsync` is called once after the commit. When only `sortOrder`, `targetMinutes` or `priorityLevel` changes, or the request fails, Then it is not called. The production registration is a no-op that never throws.

**AC-34: Application layer uses the database through the port**
- Given the solution, When the architecture tests run, Then `Qms.Application` references `Microsoft.EntityFrameworkCore` but not `Qms.Infrastructure`; handlers use `IQmsDbContext` (not `QmsDbContext`); `QmsDbContext` implements `IQmsDbContext`; Domain still has no dependencies; controllers contain no business logic.

**AC-35: Shared integration-test data set**
- Given a seeded test database, When `SetupTestData` runs, Then it adds Company 2 (own service type with prefix A, priority type N, branch, zone, counter, dispenser) and Branch 2 in Company 1 (zone, counter, dispenser), and users CA-C1, CA-C2, BM-B1 (Branch 1), BM-B2 (Branch 2), CS (Counter 1) and SA; running it twice changes nothing (idempotent); a helper returns a valid bearer token for each user; Company 1 seed data is unchanged (AC-2 of STORY-002 still passes).

**AC-36: Row version in the model, no DDL**
- Given the model, When the schema tests run, Then every entity derived from `EntityConfiguration<T>` has `xmin` mapped as a row version (`uint`, `IsRowVersion()`) with no column created; the new migration's `Up` and `Down` contain no DDL; `HasPendingModelChanges` is false; the database schema after migration is identical to before (no new column in `information_schema`); and every `ITenantEntity` has `CompanyId` as a concurrency token with `AfterSaveBehavior = Throw` (STORY-002 review N9).

**AC-37: Validation error format and codes**
- Given any of the failing requests above, When the response is read, Then it is `application/problem+json` (RFC 7807) with `code` (one of the codes in section 4 of ADR-008 §13 or this story), `status`, and for 400 an `errors` map keyed by camelCase field; no stack trace, SQL or internal id appears; and `Location`, DTO field names and enum values follow ADR-008 §3.

## 6. UI / UX
No screen in this story; the screens `/admin/service-types` and `/admin/priority-types` are STORY-023 (M4). This section fixes the content for that story and for API consumers.
- **Labels (English / Arabic, translated in M4):** Name (English) / الاسم (بالإنجليزية); Name (Arabic) / الاسم (بالعربية); Prefix / الرمز; Target minutes / الدقائق المستهدفة; Sort order / الترتيب; Active / نشط; Priority level (1 = highest) / مستوى الأولوية (1 = الأعلى); Paid / مدفوعة; Fee amount / مبلغ الرسوم; Currency (read only) / العملة. Final Arabic wording is for the M4 translator to confirm.
- **Error codes the web app will translate (en + ar) in M4:** `service_type.prefix_taken`, `priority_type.prefix_taken`, `service_type.in_use`, `priority_type.in_use`, `service_type.not_found`, `priority_type.not_found`, `request.conflict`, `validation.failed`, `auth.forbidden`.
- API text rules: names are stored and returned exactly as typed (trimmed), in both languages; Arabic text is not script-checked; API returns no display strings, only codes. A paid VIP type shows a ★ and a badge with a text label in later UI (rules 11).
- RTL: no layout in this story; the M4 form uses logical CSS properties.

## 7. Data and API impact
**Spec:** sections 4, 4.1, 6, 8, 9, 10, 11. No business rule changes. ADR-008 decides the conventions (interpretations listed there).

**Database**
- Tables used: `service_type` (company_id, name_en, name_ar, prefix, target_minutes, sort_order, is_active), `priority_type` (company_id, name_en, name_ar, prefix, priority_level, is_paid, fee_amount, is_active), `audit_log` (write). All columns exist; checks exist: `ck_service_type_prefix`, `ck_service_type_target_minutes`, `ck_priority_type_prefix`, `ck_priority_type_priority_level`, `ck_priority_type_fee_required_when_paid`, `ck_priority_type_fee_not_negative`; unique `(company_id, prefix)` on both.
- **Schema change: model only, one migration.** ADR-008 §5: map the PostgreSQL system column `xmin` as a row version (`uint`, `IsRowVersion()`) for every entity in `EntityConfiguration<T>`. Npgsql creates no column, but the model snapshot changes, so `SchemaTests` (`HasPendingModelChanges`) needs a migration (e.g. `RowVersion`) whose `Up`/`Down` contain no DDL. Stage 2 also adds the model test from STORY-002 review N9 (every `ITenantEntity` has `CompanyId` as a concurrency token with `AfterSaveBehavior = Throw`), because this change touches the same configuration base.
- Seed: unchanged.

**Backend — shared plumbing (ADR-008)**
- `Qms.Application/Common`: `IQmsDbContext` (§1; adds the `Microsoft.EntityFrameworkCore` package to Application), `AuditedAttribute`, `NotAuditedAttribute`, `AuditDecorator` (§11; chain validation → cross-tenant → audit → handler), `IBranchScope` (§10), `IDeviceConfigNotifier` (§12, no-op implementation), shared error codes (§13), `Version` helpers (row `xmin` ↔ string; set-hash for grids, §5).
- `Qms.Infrastructure`: `QmsDbContext : IQmsDbContext`, `BranchScope` (reads `user_branch` once per request with `IgnoreQueryFilters()` by user id), `PostgresErrorCatalog` (§7, incl. the four STORY-003 user-assignment constraint names).
- `Qms.Api`: `JsonStringEnumConverter` globally (§3); role policies `SetupRead` (CompanyAdmin, BranchManager), `CompanyAdmin`, `BranchSetup` (CompanyAdmin, BranchManager) (§9); `GlobalExceptionHandler` maps `DbUpdateException`/`PostgresException` by SQLSTATE and catalog (§7).
- Tests support: `SetupTestData` (§15) with Company 2, Branch 2 in Company 1, users CA-C1, CA-C2, BM-B1, BM-B2, Counter Staff, Super Admin, and a token helper.

**Backend — endpoints (spec §10 + ADR-008 §2 item routes)**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/v1/service-types` | CA, BM | sorted `sortOrder`, `nameEn`; `?isActive=` |
| GET | `/api/v1/service-types/{id}` | CA, BM | 404 `service_type.not_found` (also other company) |
| POST | `/api/v1/service-types` | CA | 201; 409 `service_type.prefix_taken` |
| PUT | `/api/v1/service-types/{id}` | CA | `version` required; 409 `request.conflict`; deactivate via `isActive` |
| DELETE | `/api/v1/service-types/{id}` | CA | 409 `service_type.in_use` (company mapping, flows, dispenser mapping, tokens) |
| GET | `/api/v1/priority-types` | CA, BM | sorted `priorityLevel`, `prefix`; includes `isPaid`, `feeAmount`, company `currencyCode` |
| GET | `/api/v1/priority-types/{id}` | CA, BM | 404 `priority_type.not_found` |
| POST | `/api/v1/priority-types` | CA | `feeAmount` required when `isPaid`, must be empty when not paid; 409 `priority_type.prefix_taken` |
| PUT | `/api/v1/priority-types/{id}` | CA | same rules + `version` |
| DELETE | `/api/v1/priority-types/{id}` | CA | 409 `priority_type.in_use` (company mapping, branch fees, tokens) |
- DTOs: `ServiceTypeDto { id, nameEn, nameAr, prefix, targetMinutes?, sortOrder, isActive, version }`, `PriorityTypeDto { id, nameEn, nameAr, prefix, priorityLevel, isPaid, feeAmount?, currencyCode, isActive, version }`.
- Audit: every create/update/delete writes `audit_log` (`service_type` / `priority_type`, `create` / `update` / `delete`).
- `IDeviceConfigNotifier` call after commit when a name, prefix, `isActive`, `isPaid` or fee changes (no-op in M2).
- SignalR: none (M3). Background jobs: none.

**Web / Android / displays:** none in this story (admin screens are M4 STORY-023; dispenser M7).

**Cross-cutting:** security (role policy; Super Admin and Counter Staff 403; cross-company 404); audit in the same transaction; bilingual names; concurrency via `version`; performance not critical (small lists).

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008 (Proposed) |
| 1 BA | Yes | — | Done. One backend task (TASK-010), with the plumbing as its first part and the two CRUD features as its second part |
| 2 Database | Yes | [TASK-009](../tasks/TASK-009-xmin-row-version-model.md) — AC-36 (and the model side of AC-32) | `xmin` row version in `EntityConfiguration<T>` + model-only migration; STORY-002 review N9 model test |
| 3 Backend | Yes | [TASK-010](../tasks/TASK-010-setup-plumbing-and-type-crud.md) — AC-1 to AC-35, AC-37 (AC-32 helpers) | Plumbing (ADR-008) + service/priority type endpoints + tests |
| 4 Frontend | N/A | — | Admin screens are M4 (STORY-023) |
| 5 Android | N/A | — | No device API change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
- **Security:** role policies on controllers (rules 6); Super Admin and Counter Staff 403; cross-company and unknown ids give the same 404; no tenant id from the body; no SQL or constraint text in errors; no secrets or phone numbers involved (none in these commands, so `[NotAudited]` is not needed on them).
- **Audit:** same transaction as the change (spec 11).
- **Performance:** lists are small (tens of rows), no paging; no performance target beyond normal response times in tests.
- **Concurrency:** `xmin` row version; two admins editing one row get 409, never a silent overwrite.
- **Data:** money is `numeric(12,2)` and serialised as a JSON number with at most 2 decimals; timestamps UTC.
- **Compatibility:** no change to existing auth contracts; enum string serialisation affects no current contract. No migration data change.
- **Privacy / bilingual:** names always stored in both languages (rules 7).

## 10. Open questions
Decided with the architect's defaults (proposals accepted for this story unless the product owner objects; none blocks stages 2 and 3):
- Default 1 (prefix change after tokens exist): prefixes are editable at any time; a collision of token numbers is mapped to 409 in M3 (STORY-014). Not covered by an AC here.
- Default 2 (paid to not paid): allowed; `branch_priority_fee` overrides are kept and ignored while the type is unpaid (AC-20).
- Default 3 (priority level): two priority types may share a level; the spec has no uniqueness rule (BR-6).
- Default 4 (deactivation): deactivating a type that is in use is allowed (AC-9, AC-22); the effect on issuing is decided in M3.
- Default 5 (fee 0): a paid type may have fee 0.00 (the check constraint is `>= 0`) (AC-16).
- Default 6 (Branch Manager reads): Branch Manager can read both lists, company-wide, because the branch mapping screens need them (ADR-008 §9).
- Default 7 (Company 2 data): `SetupTestData` creates Company 2 with a service type with prefix A, so AC-6 can show per-company uniqueness.
- [ ] **Product owner — companies are not in any M2 story.** Spec §10 lists `CRUD /companies` and §8 gives Super Admin "Companies, licences, global settings", and M4 has `/admin/companies`, but the backlog has no story for the companies API or for creating a new company's first Company Admin. Today only the seed creates Company 1 and its admin. Proposal: add a story to M2 (next free id STORY-047, "Companies and Company Admin provisioning", Super Admin, `[CrossTenant]`). Not blocking this story.
- [ ] Product owner — prefix change after tokens exist. The token keeps its prefixes (`token.priority_prefix`, `service_prefix`), but if prefix `A` is moved to another service type on the same business date, the new type's sequence restarts at 0001 and its first token collides with an issued `NA0001` (unique `token (branch_id, business_date, number)`). Proposal for M2: prefixes are editable; M3 (STORY-014) maps that collision to 409. Alternative: lock the prefix once any token exists. Not blocking.
- [ ] Product owner — a priority type changed from paid to not paid keeps its branch fee overrides (`branch_priority_fee`). Proposal: allowed; overrides are ignored while not paid and apply again if it becomes paid. Not blocking.
- [ ] Note: ADR-008 is Proposed; the product owner accepts it (and the §10 route additions) before the stage 3 merge.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: docs/adr/ADR-008-setup-api-conventions.md (new), this story (new: header, sections 1, 2, 7, 8, 10), docs/work/BACKLOG.md (M2 links)
Notes for next stage:
- Read ADR-008 first; all M2 stories follow it. This story carries the plumbing; STORY-006 to STORY-013 depend on it.
- Stage 2 is model-only (`xmin` row version + empty migration + N9 test). No column, table or seed change.
- Stage 3 notes: `GlobalExceptionHandler` already maps `DbUpdateConcurrencyException` → 409 `request.conflict`; extend it for `PostgresException` 23001/23503/23505/23514 via the catalog. The CrossTenant decorators show how to add the audit decorator to `DependencyInjection.RegisterDecorated`. `IBranchScope` has no consumer in this story; give it unit and integration tests (CA passes all Company 1 branches, BM-B1 passes Branch 1 and fails Branch 2 with 403, unknown/other-company branch 404).
- Fee: `numeric(12,2)`; validators enforce ≤ 2 decimals and the paid/fee pairing before the check constraint.
- Integration tests follow ADR-008 §15 for every endpoint.
Open questions: companies not in M2 (product owner), prefix change after tokens, paid→not paid overrides; none blocks analysis.

### Stage 1 — Analysis — business-analyst — 2026-11-08
Result: Done
Changed: this story (sections 3, 4, 5, 6, 8 task links, 9, 10; status Database), docs/work/tasks/TASK-009-xmin-row-version-model.md (new), docs/work/tasks/TASK-010-setup-plumbing-and-type-crud.md (new)
Notes for next stage:
- 37 acceptance criteria: AC-1 to AC-12 service types, AC-13 to AC-24 priority types, AC-25 role matrix, AC-26/27 audit, AC-28 to AC-37 shared plumbing and model. Stage 2 covers AC-36 only (TASK-009); everything else is stage 3 (TASK-010).
- Stage 2 is model-only: `xmin` row version, migration with no DDL, N9 model test. No column, table, index or seed change.
- Rules not in the DB but required by the validators: unpaid priority type must have no fee; fee max 2 decimals and 0 to 9 999 999 999.99; prefix is not upper-cased silently.
- Defaults 1 to 7 in section 10 follow the architect's proposals; no rule was invented beyond ADR-008.
Open questions: none blocking; product owner items in section 10 (companies story, prefix change after tokens, paid to unpaid overrides, ADR-008 acceptance before stage 3 merge).

### Stage 2 — Database — database-architect — 2026-11-08
Result: Done
Changed: backend/Qms.Domain/Entities/Common/Entity.cs, backend/Qms.Infrastructure/Persistence/Configurations/EntityConfiguration.cs, backend/Qms.Infrastructure/Persistence/Migrations/20261108072029_RowVersion.cs (+ .Designer.cs, QmsDbContextModelSnapshot.cs), backend/Qms.Tests/Persistence/RowVersionTests.cs (new), backend/Qms.Tests/Persistence/TenantIntegrityTests.cs (one test adjusted), docs/work/tasks/TASK-009-xmin-row-version-model.md
Notes for next stage:
- Row version = `public uint RowVersion { get; set; }` on the Domain base class `Entity` (plain CLR property, no package dependency), mapped in `EntityConfiguration<T>` to column `xmin`, type `xid`, `IsRowVersion()` (ValueGenerated OnAddOrUpdate). Applies to all 27 entity tables (tenant and non-tenant, including refresh_token and audit_log); AppUser/AppRole keep `ConcurrencyStamp`.
- Read: `version` string from `entity.RowVersion` (it is a uint; the DTO string is yours to encode, e.g. decimal). After `SaveChanges` EF reads the new `xmin` back into `RowVersion` (tested).
- Write / check a client version: load the tracked entity, then `db.Entry(entity).Property(e => e.RowVersion).OriginalValue = clientVersion;` before `SaveChangesAsync`. A mismatch gives `DbUpdateConcurrencyException` (tested). Do not assign `RowVersion` itself; it is database generated.
- UPDATE and DELETE now filter on `id`, `company_id` and `xmin` (tested through the logged SQL). `CompanyId` keeps `IsConcurrencyToken` + `AfterSaveBehavior = Throw` (N9 model test added).
- DELETE (ADR-008: "DELETE takes no version"): load the row (tracked or AsNoTracking) before `Remove`; a hand-built stub with `RowVersion = 0` now matches 0 rows and throws `DbUpdateConcurrencyException`. I changed the existing test `A_detached_row_of_the_current_company_can_still_be_updated_and_removed` accordingly (it now reads the row first). Never delete via a stub entity.
- Migration `RowVersion` has empty Up/Down. EF generated `AddColumn xmin` for each table (the property is new to its model); PostgreSQL rejects a column named like a system column, so I removed those operations by hand before anything was applied. The snapshot and designer carry the model; `HasPendingModelChanges` is false. Do not regenerate it. A test asserts the migration has no operations and no table has a real `xmin` column.
- Set hash for whole-set resources: read `RowVersion` of each tracked row (sorted by id) after `SELECT ... FOR UPDATE` on the parent.
Open questions: none. Spec section 9 unchanged (no ADR request needed).
