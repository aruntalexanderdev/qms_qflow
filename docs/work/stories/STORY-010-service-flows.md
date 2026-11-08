# STORY-010 — Service flows (Auto / Ask / None)

| Field | Value |
|---|---|
| ID | STORY-010 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 4 (Service flow), 5.3 (Complete & send), 5.5, 6 (Service flow), 8 (`/admin/branches/:id/flows`), 9 (`service_flow`), 10 (`/branches/{id}/flows`), 11 (Audit), 12 (M2), 13 (Next-stage flow per flow) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **to define per branch which service follows another when a visit completes, and whether that happens automatically, on request, or not at all**, so that **multi-stage visits (e.g. Service 2 → Service 1) keep the same token**.

## 2. Background
Spec §6 "Service flow": modes Auto, Ask, None; the queue engine (M3 STORY-018) applies them on Complete. This story only stores and validates flows. Both services must be offered by the branch (at least one pair in the branch mapping, Available or not), which follows the mapping chain (common rules §3). Depends on STORY-007 and STORY-008.

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (Screen is M4 STORY-025.)

## 7. Data and API impact
**Spec:** sections 4, 6, 8, 9, 10, 13. No business rule changes.

**Database**
- Tables: `service_flow` (branch_id, from_service_type_id, to_service_type_id, mode; unique `(branch_id, from_service_type_id, to_service_type_id)`; check `ck_service_flow_different_services`; composite FKs to branch and service types); read `branch_service_priority`; `audit_log`.
- **Schema change: none.** (If the product owner wants at most one flow per from-service enforced by the database, stage 2 would add a unique index `(branch_id, from_service_type_id)`; default is an Application check, see open questions.)

**Backend**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/v1/branches/{branchId}/flows` | CA, BM (own) | `[{ id, fromServiceTypeId, toServiceTypeId, mode, version }]` |
| POST | `/api/v1/branches/{branchId}/flows` | CA | from ≠ to (400 `flow.same_service`); both services in the branch mapping (400 `mapping.service_not_in_branch`); duplicate → 409 `flow.duplicate`; second flow from the same service → 409 `flow.from_service_taken` (default, open question) |
| GET | `/api/v1/flows/{id}` | CA, BM (own) | 404 `flow.not_found` |
| PUT | `/api/v1/flows/{id}` | CA | `{ toServiceTypeId, mode, version }` (from-service fixed; delete and recreate to change it) |
| DELETE | `/api/v1/flows/{id}` | CA | no references, always allowed |
- Audit on every write (`service_flow`).
- `IDeviceConfigNotifier`: not needed (flows are not shown on devices).
- SignalR: none.

**Web / Android / displays:** none (the counter console's "Complete & send to X" button is M5).

**Cross-cutting:** branch scope through flow → branch; Company 2 service type ids → 400 unknown; example: Branch 1 flow Service 2 → Service 1 (spec §5.5) saves with mode Ask or Auto.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008 |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | Table, unique key and check exist (STORY-002); see open question 2 |
| 3 Backend | Yes | BA creates | Flow CRUD, chain validation, tests |
| 4 Frontend | N/A | — | Screen is M4 (STORY-025) |
| 5 Android | N/A | — | No device API change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] **Product owner — who edits flows.** Spec §8 lists flows among the admin screens but neither under Company Admin nor under Branch Manager in the role table. ADR-008 §9 default: Company Admin writes, Branch Manager reads own branches. Confirm, or let the Branch Manager edit flows of their own branches (they already edit the counter mapping). Not blocking.
- [ ] **Product owner — more than one next service.** Spec §4/§6 describe "the next service" (singular) and the Ask button is "Complete & send to X". The schema allows several rows per from-service, which makes Auto ambiguous. Default: at most one flow per from-service per branch (409 `flow.from_service_taken`, Application check). Confirm; if several targets for Ask are wanted, M5 needs a choice list. Not blocking.
- [ ] Note: cycles (A → B and B → A) are allowed; each step needs a staff Complete, so no automatic loop. The BA may state this as a rule.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends on STORY-007 and STORY-008 (the "service in branch mapping" check). Independent of STORY-009; can run in parallel with it.
- STORY-008 must refuse removing the last pair of a service used by a flow (already in its section 7).
- Mode `None` keeps a stored but inactive flow (spec §6); the BA states it.
Open questions: flow editing rights, one flow per from-service; none blocks analysis.
