# STORY-009 — Zones, counters and counter service mapping (4.3), validation chain

| Field | Value |
|---|---|
| ID | STORY-009 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 3 (counters), 4 (Zone, Counter), 4.3, 4.4 (validation rules), 5.1 steps 6–7, 6 (Call Next 1, Zones), 8 (`/admin/branches/:id/zones`), 9 (`zone`, `counter`, `counter_service_priority`), 10 (`/branches/{id}/zones`, `/zones/{id}/counters`, `/counters/{id}/service-mapping`), 11 (Audit), 12 (M2: "invalid mapping rows rejected"), 13 (Counter scope; Counter 2 – Service 3 – VIP resolved) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Branch Manager** (or Company Admin), I want **to create zones and counters in my branch and map each counter to the service + priority pairs it serves**, so that **Call Next only gives a counter the tokens it is meant to serve**.

## 2. Background
Spec §5.1 steps 6–7 and mapping level 3 of 4 (§4.3). Counter rows must exist in the branch mapping (Available or Not Available); a pair missing from the branch mapping is rejected (§4.3, §4.4 rules; §13 resolved: Counter 2 – Service 3 – VIP is valid). The composite FK `fk_counter_service_priority_branch_service_priority_branch_id_` backs this. M3 Call Next (§6) uses exactly these rows. Depends on STORY-007 (branches, default zone) and STORY-008 (branch mapping).

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (Screens are M4 STORY-025.)

## 7. Data and API impact
**Spec:** sections 4, 4.3, 4.4, 6, 8, 9, 10, 13. No business rule changes. ADR-008 §14: the last zone of a branch cannot be deleted.

**Amendment 2026-11-08 (product owner decisions, ADR-009, ADR-008 Amendment 1 B):**
- Permissions (the "Roles" column below is the default grant; CA and the Branch Manager role have all of them by default): `zones.view/create/edit/delete`, `counters.view/create/edit/delete` (incl. `GET /branches/{id}/counters`), `counter-service-mapping.view/edit`. Branch scope unchanged.
- `is_active` on zones and counters: `PUT` bodies gain `isActive`; DTOs return it; lists accept `?isActive=`. Rules (ADR-008 Amendment 1 B): a counter created in or moved to an inactive zone → 400 `zone.inactive`; a zone can be deactivated only when none of its counters is active (409 `zone.has_active_counters`) and never when it is the branch's last active zone (409 `zone.last_active_zone`); a counter can be deactivated only while Closed (409 `counter.not_closed`); saving the service mapping of an inactive counter is allowed (prepare before activation). Inactive zones/counters are hidden from consoles, dispensers and displays in M3; history kept; delete rules unchanged.
- Audit: activation changes are ordinary `update` rows.

**Database**
- Tables: `zone` (branch_id, name_en, name_ar, sort_order), `counter` (branch_id, zone_id, name, number ≥ 1 unique per branch, status, current_user_id; composite FK zone in same branch), `counter_service_priority` (branch_id, counter_id, service_type_id, priority_type_id; unique `(counter_id, service_type_id, priority_type_id)`; FKs to counter and branch mapping); read `branch_service_priority`, `device`, `dispenser_zone_service`, `token_visit`, `user_counter` for in-use checks; `audit_log`.
- **Schema change: yes (stage 2, product owner 2026-11-08):** `zone.is_active` and `counter.is_active` (boolean, not null, default true; existing rows true), migration e.g. `ZoneCounterActive`; entity property `IsActive` on `Zone` and `Counter`. Spec §9 updated.

**Backend**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET / POST | `/api/v1/branches/{branchId}/zones` | CA, BM (own) | sorted `sortOrder`, `nameEn` |
| GET / PUT / DELETE | `/api/v1/zones/{id}` | CA, BM (own) | DELETE → 409 `zone.in_use` (counters, devices, dispenser rows, visits/tokens) or `zone.last_zone` |
| GET / POST | `/api/v1/zones/{zoneId}/counters` | CA, BM (own) | counter `{ name, number }`; 409 `counter.number_taken` (unique per branch) |
| GET | `/api/v1/branches/{branchId}/counters` | CA, BM (own) | all counters of a branch with zone (needed by STORY-011 assignment and STORY-012 counter displays; ADR-008 §2 nested list) |
| GET / PUT / DELETE | `/api/v1/counters/{id}` | CA, BM (own) | PUT `{ name, number, zoneId, version }` — moving to another zone of the same branch is allowed only while Closed; `status` and `currentUserId` are not editable here (M3 counter session); DELETE only while Closed and unreferenced → 409 `counter.in_use` |
| GET | `/api/v1/counters/{id}/service-mapping` | CA, BM (own) | `{ version, rows: [{ serviceTypeId, priorityTypeId }] }`, each row with the branch pair's `isAvailable` for display |
| PUT | `/api/v1/counters/{id}/service-mapping` | CA, BM (own) | whole set; pair missing from the counter's branch mapping → 400 `mapping.pair_not_in_branch` (row errors); Not Available pair accepted; stale `version` → 409 |
- Database backstop: `fk_counter_service_priority_branch_service_priority_branch_id_` → 400-equivalent pre-check, residual 409 `mapping.pair_not_in_branch`; `ux`/unique counter number → 409 `counter.number_taken`; restrict on delete → 409 `zone.in_use` / `counter.in_use`.
- Audit on every write (`zone`, `counter`, `counter_service_mapping`).
- `IDeviceConfigNotifier.ConfigChangedAsync(branchId)` after counter mapping saves and zone renames (dispenser warnings / zone names; no-op in M2).
- SignalR: none in M2 (`CounterStatusChanged` is M3).

**Web / Android / displays:** none.

**Cross-cutting:** branch scope through zone → branch and counter → branch (load, 404, then 403); the `zoneId` in a counter PUT must be in the counter's branch (FK `fk_counter_zones_branch_id_zone_id`; pre-check 400 `zone.not_found`). Example data tests (spec §4.3): the 7 seeded rows round-trip; Counter 1 + Service 6 Normal → 400 `mapping.pair_not_in_branch`; Counter 3 + Service 4 VIP → 400; Counter 2 keeps Service 3 VIP after it is set Not Available in STORY-008.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008 (+ Amendment 1), ADR-009 |
| 1 BA | Yes | — | BA may split stage 3 into zones/counters CRUD and counter mapping |
| 2 Database | Yes | BA creates | `zone.is_active`, `counter.is_active` + migration (product owner 2026-11-08); other tables, keys and composite FKs exist (STORY-002) |
| 3 Backend | Yes | BA creates | Zone and counter CRUD incl. active flag rules, counter mapping grid, chain validation, permission attributes, tests; after STORY-048 |
| 4 Frontend | N/A | — | Screens are M4 (STORY-025) |
| 5 Android | N/A | — | No device API change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [x] **Resolved 2026-11-08:** `is_active` added to zones and counters (ADR-008 Amendment 1 B). Original question: Product owner (low) — zones and counters have no `is_active` in spec §9, so a counter or zone that served tokens can never be removed from lists (delete is refused by history). Proposal: accept for phase 1 (a counter stays Closed); add `is_active` later if branches ask. Not blocking.
- [ ] None else. Counter `name` stays a single (not bilingual) column as in spec §9 (STORY-002 decision 5).

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends on STORY-007 (branch, default "Main zone") and STORY-008 (branch mapping). Zones/counters CRUD only need STORY-007; the counter mapping needs STORY-008.
- `GET /branches/{branchId}/counters` is an addition beyond the §10 table (covered by ADR-008 §2 nested lists); list it in the ADR-008 spec impact when the spec is updated.
- Counter session fields (`status`, `current_user_id`) belong to M3 STORY-016; setup never writes them.
Open questions: `is_active` for zones/counters (low); none blocks analysis.

### Stage 0 — Architecture review (amendment) — project-architect — 2026-11-08
Result: Done
Changed: this story (sections 7, 8, 10); ADR-009 (new); ADR-008 Amendment 1; `docs/PROJECT_SPEC.md` §9 (approved)
Notes for next stage:
- Stage 2 now applies: `is_active` on `zone` and `counter`. The BA creates a stage 2 task.
- Active-flag rules (zone/counter consistency, Closed-only counter deactivation) are in section 7; they are interpretations recorded in ADR-008 Amendment 1 B.
- Permission codes listed in section 7; depends on STORY-048 too.
- STORY-012's dispenser-mapping warning counts only **active** counters of the zone.
Open questions: none.
