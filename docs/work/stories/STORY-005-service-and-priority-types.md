# STORY-005 — Service types and priority types CRUD (incl. paid flag and fee) and the shared setup-API plumbing

| Field | Value |
|---|---|
| ID | STORY-005 |
| Status | Analysis |
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
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (No screen in M2; the screens are STORY-023 in M4.)

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
| 1 BA | Yes | — | BA may split stage 3 into two tasks: plumbing, then the two CRUD features |
| 2 Database | Yes | BA creates | `xmin` row version in `EntityConfiguration<T>` + model-only migration; STORY-002 review N9 model test |
| 3 Backend | Yes | BA creates | Plumbing (ADR-008) + service/priority type endpoints + tests |
| 4 Frontend | N/A | — | Admin screens are M4 (STORY-023) |
| 5 Android | N/A | — | No device API change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
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
