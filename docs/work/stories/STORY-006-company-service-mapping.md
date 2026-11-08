# STORY-006 — Company service mapping grid API (4.1)

| Field | Value |
|---|---|
| ID | STORY-006 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 4, 4.1, 4.4 (validation rules), 5.1 step 3, 8 (`/admin/service-mapping`), 9 (`company_service_priority`), 10 (`/company-service-mapping`), 11 (Audit), 12 (M2) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **to save which priority types each service type allows, as one grid**, so that **branches can only offer service + priority pairs the company allows**.

## 2. Background
Mapping level 1 of 4 (spec §4.1, common rules §3). Branch rows reference this table by a composite foreign key (STORY-002), so a pair removed here must not be in use by any branch. Follows the conventions of ADR-008 (whole-set PUT §6, set version §5, no cascade down the chain). Depends on STORY-005 (types and plumbing).

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (Grid screen is M4 STORY-023.)

## 7. Data and API impact
**Spec:** sections 4.1, 8, 9, 10. No business rule changes.

**Amendment 2026-11-08 (ADR-009, ADR-008 Amendment 1):** authorization is permission-based.
- Permissions `service-mapping.view` (default: Company Admin, Branch Manager role) and `service-mapping.edit` (default: Company Admin); the role column below is the default grant.
- Inactive service / priority types still cannot be used in new rows (`mapping.type_inactive`, ADR-008 §7, unchanged).

**Database**
- Tables: `company_service_priority` (company_id, service_type_id, priority_type_id; alternate key `(company_id, service_type_id, priority_type_id)`, composite FKs to `service_type` / `priority_type`); read `branch_service_priority` for the in-use check; `company` row locked for the set version (ADR-008 §5); `audit_log`.
- **Schema change: none.**

**Backend**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/v1/company-service-mapping` | CA, BM | `{ version, rows: [{ serviceTypeId, priorityTypeId }] }` |
| PUT | `/api/v1/company-service-mapping` | CA | body `{ version, rows: [...] }`; whole set (ADR-008 §6) |
- Rules for the PUT: each id must be a service / priority type of the current company (else 400 with row errors; another company's id is treated as unknown); duplicates → 400; new rows with an inactive type → 400 `mapping.type_inactive`; removing a pair that a branch maps → 409 `mapping.pair_in_use` listing the branches; stale `version` → 409 `request.conflict`.
- Database backstop: 23503/23001 on `fk_branch_service_priority_company_service_priorities_company_` (actual generated name; the catalog uses the real name) → 409 `mapping.pair_in_use`.
- Audit: one row per save (`company_service_mapping`, `update`, data = submitted rows).
- `IDeviceConfigNotifier`: not needed (dispensers read the branch mapping, not this one).
- SignalR: none.

**Web / Android / displays:** none.

**Cross-cutting:** Company Admin only for writes; Super Admin and Counter Staff 403; Company 2 never sees or changes Company 1 rows (cross-company test: C2's PUT with Company 1 type ids → 400 unknown ids, C1 grid unchanged).

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008 (+ Amendment 1), ADR-009 |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | Table, keys and composite FKs exist (STORY-002) |
| 3 Backend | Yes | BA creates | GET/PUT grid, chain checks, tests |
| 4 Frontend | N/A | — | Grid screen is M4 (STORY-023) |
| 5 Android | N/A | — | No device API change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] None blocking. Test data: the seeded grid (Services 1–6 Normal, Services 2 and 3 VIP) must round-trip unchanged; removing Service 2 VIP must fail with 409 because Branch 1 maps it.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends on STORY-005 (plumbing, types). Small story; the grid pattern built here (diff, parent lock, set version, row errors) is reused by STORY-008, STORY-009 (counter mapping) and STORY-012 (dispenser mapping), so keep it as a reusable helper.
- Spec §10 says "CRUD"; ADR-008 §2 makes it a whole-set GET/PUT like the other grids.
Open questions: none

### Stage 0 — Architecture review (amendment) — project-architect — 2026-11-08
Result: Done
Changed: this story (section 7, 8)
Notes for next stage:
- Endpoints use `[RequirePermission]` with the codes in section 7 (ADR-009 catalog); depends on STORY-048 as well. Stage plan otherwise unchanged.
Open questions: none.
