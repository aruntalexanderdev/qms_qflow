# STORY-008 — Branch service mapping grid API (4.2)

| Field | Value |
|---|---|
| ID | STORY-008 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 4.2, 4.3 (Not Available pairs stay mappable), 4.4 (validation rules), 5.1 step 5, 6 (Issue rules 2–4), 8 (`/admin/branches/:id/services`), 9 (`branch_service_priority`), 10 (`/branches/{id}/service-mapping`), 11 (Audit), 12 (M2) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Branch Manager** (or Company Admin), I want **to save the service + priority pairs my branch offers, with a daily token limit and Available / Not Available, as one grid**, so that **dispensers offer exactly those pairs and counters can only be mapped to them**.

## 2. Background
Mapping level 2 of 4 (spec §4.2). Each row must exist in the company mapping (STORY-006; composite FK). Counter mapping rows reference this table (composite FK), so a pair in use by a counter cannot be removed — but it can be set Not Available (spec §4.3). Dispenser and flow rows reference only the service, so removing the last pair of a service that a dispenser or flow uses is also rejected (ADR-008 §6, chain rule). Depends on STORY-006 and STORY-007.

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (Grid screen is M4 STORY-024.)

## 7. Data and API impact
**Spec:** sections 4.2–4.4, 6, 8, 9, 10. No business rule changes.

**Amendment 2026-11-08 (ADR-009, ADR-008 Amendment 1):** authorization is permission-based.
- Permissions `branch-service-mapping.view` / `.edit` (default: Company Admin and Branch Manager role, own branches via `IBranchScope`); the role column below is the default grant.
- Inactive service / priority types still cannot be used in new rows (`mapping.type_inactive`, ADR-008 §7, unchanged).

**Database**
- Tables: `branch_service_priority` (branch_id, service_type_id, priority_type_id, max_tokens_per_day 1–9999 `ck_branch_service_priority_max_tokens`, is_available; alternate key `(branch_id, service_type_id, priority_type_id)`; FK `fk_branch_service_priority_company_service_priorities_company_`); read `company_service_priority`, `counter_service_priority`, `dispenser_zone_service`, `service_flow` for checks; `branch` row locked for the set version; `audit_log`.
- **Schema change: none.**

**Backend**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/v1/branches/{id}/service-mapping` | CA, BM (own) | `{ version, rows: [{ serviceTypeId, priorityTypeId, maxTokensPerDay, isAvailable }] }` |
| PUT | `/api/v1/branches/{id}/service-mapping` | CA, BM (own) | whole set (ADR-008 §6) |
- PUT rules: pair not in the company mapping → 400 `mapping.pair_not_in_company` (row errors); `maxTokensPerDay` 1–9999; new row with an inactive type → 400 `mapping.type_inactive`; removing a pair that a counter maps → 409 `mapping.pair_in_use` (lists counters); removing the last pair of a service used by a dispenser mapping or a service flow of this branch → 409 `mapping.service_in_use` (lists dispensers / flows); setting `isAvailable = false` is always allowed; stale `version` → 409.
- Database backstop: `fk_counter_service_priority_branch_service_priority_branch_id_` → 409 `mapping.pair_in_use`; `fk_branch_service_priority_company_service_priorities_company_` → 409 `mapping.pair_not_in_company`.
- Audit: one row per save (`branch_service_mapping`, `update`).
- `IDeviceConfigNotifier.ConfigChangedAsync(branchId)` after every save (spec §10: `DeviceConfigChanged` "also sent when a service becomes … Not Available"; no-op in M2).
- SignalR: none in M2.

**Web / Android / displays:** none.

**Cross-cutting:** branch scope (BM-B2 on Branch 1 → 403; CA-C2 → 404); example data tests: the seeded 7 rows round-trip; adding Service 4 VIP → 400 (not in the company mapping); removing Service 3 VIP → 409 (Counter 2 maps it); setting Service 3 VIP Not Available → 200.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008 (+ Amendment 1), ADR-009 |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | Table, alternate key and composite FKs exist (STORY-002) |
| 3 Backend | Yes | BA creates | GET/PUT grid on the STORY-006 grid helper, chain checks, tests |
| 4 Frontend | N/A | — | Grid screen is M4 (STORY-024) |
| 5 Android | N/A | — | Dispenser reads this through `/device/config` (M3 STORY-015) |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] None blocking. Note for the BA: lowering `maxTokensPerDay` below today's issued count is allowed in M2 (no tokens yet); M3 issue rule 4 then simply refuses further tokens.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends on STORY-006 (company mapping + grid helper) and STORY-007 (branches, `IBranchScope` usage).
- The "service in use by a dispenser or flow" check is Application-only: those rows reference the service type, not the pair, so no FK protects it (STORY-002 note).
Open questions: none

### Stage 0 — Architecture review (amendment) — project-architect — 2026-11-08
Result: Done
Changed: this story (section 7, 8)
Notes for next stage:
- Endpoints use `[RequirePermission]` with the codes in section 7 (ADR-009 catalog); depends on STORY-048 as well. Stage plan otherwise unchanged.
Open questions: none.
