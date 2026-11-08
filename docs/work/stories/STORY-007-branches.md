# STORY-007 — Branches: details, working hours, calendar, settings, VIP fee override

| Field | Value |
|---|---|
| ID | STORY-007 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 4 (Branch, Zone), 5.1 step 4, 6 (Issue rules 5–6, Paid priority, Working hours, Other counter actions: recall, skip), 8 (`/admin/branches`), 9 (`branch`, `branch_hours`, `branch_calendar`, `branch_setting`, `branch_priority_fee`, `zone`), 10 (`/branches`, `/hours`, `/calendar`, `/settings`, `/priority-fees`), 11 (Time, Audit), 12 (M2), 13 (opening hours resolved) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **to create and maintain branches with their working hours, holidays and special hours, queue settings and VIP fee overrides**, so that **each branch opens, closes, recalls, skips and charges the VIP fee as configured**.

## 2. Background
Spec §5.1 step 4 ("Branch: hours, holidays, fee override"). The branch tables exist since STORY-002 with composite same-company keys and checks (`close_time > open_time`, confirmed in §13 on 2026-11-08: branches never open past midnight). Hours and calendar are read by M3 issue rule 5; settings by recall/skip (M3); fee overrides by issue rule 6 ("branch override, else the company fee"). Branch Manager reads only their own branches (ADR-008 §9, §10). Depends on STORY-005 (plumbing, `IBranchScope`, priority types).

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (Branch screens are M4 STORY-024.)

## 7. Data and API impact
**Spec:** sections 4, 6, 8, 9, 10, 11, 13. No business rule changes. Interpretations in ADR-008 §14 (default "Main zone" and settings on create) and §2 (hours as a whole week).

**Amendment 2026-11-08 (localisation, ADR-010). It supersedes the conflicting lines below.**
- `branch` stores `name` + `name_translations` (built by STORY-051), not `name_en` / `name_ar`. The API exchanges `name` (required, 1–200) and `translations` (optional) through the shared `LocalizedTextRules` (ADR-010 §5).
  - Translation keys must be languages enabled for the company.
  - Responses return only the enabled-language keys.
  - `PUT` keeps the stored keys of languages that are not enabled.
- Branch create: the default zone is `name` "Main zone" with no translations (ADR-008 Amendment 2 §14).
- Tests: one branch test with a second language made available and enabled for a test company (ADR-008 Amendment 2 §15). Phase 1 data is English only.
- Now depends on STORY-051 as well.

**Amendment 2026-11-08 (product owner decisions, ADR-009, ADR-008 Amendment 1):**
- Authorization is permission-based. The "Roles" column below is the **default grant**; endpoints declare permissions: `branches.view/create/edit/delete` (details), `branch-hours.view/edit` (hours **and** calendar), `branch-settings.view/edit`, `branch-fees.view/edit`. Read for the Branch Manager role by default; a company may grant the Branch Manager role edit rights (open question 1 resolved by configuration). "Own branches" = `IBranchScope` (role branch scope `Company` or `Assigned` + `user_branch`), unchanged order 404 → 403.
- The branch list returns all branches for a user with `Company` scope and only assigned ones for `Assigned` scope (`AccessibleBranchIdsAsync`, no longer a role check).
- Overlapping calendar entries are **rejected** (confirmed): 409 `branch_calendar.overlap` on POST and PUT; Application check under a `SELECT … FOR UPDATE` lock on the branch row (no exclusion constraint, no `btree_gist`).
- Deactivating a branch hides it from devices and consoles (M3); inactive branch stays readable in setup.
- Integration tests: per ADR-008 Amendment 1 A, one grant/revoke test (custom role with `branch-hours.edit` can save hours; revoked → 403 on the next request).

**Database**
- Tables: `branch` (name, name_translations, code unique per company, timezone, is_active), `branch_hours` (day_of_week, open_time, close_time; unique `(branch_id, day_of_week, open_time)`; split shifts allowed), `branch_calendar` (date_from, date_to, type Holiday/SpecialHours, open_time, close_time; checks `ck_branch_calendar_date_range`, `ck_branch_calendar_special_hours_times`), `branch_setting` (one per branch: recall_limit, skip_default, block_when_no_counter, wait_alert_minutes), `branch_priority_fee` (unique `(branch_id, priority_type_id)`, fee ≥ 0), `zone` (default "Main zone" inserted on create), `audit_log`.
- **Schema change: none.** All columns and constraints exist.

**Backend**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/v1/branches` | CA (all), BM (own only, via `AccessibleBranchIdsAsync`) | sorted by `code`; `?isActive=` |
| GET | `/api/v1/branches/{id}` | CA, BM (own) | 404 `branch.not_found`; BM of another branch 403 |
| POST | `/api/v1/branches` | CA | creates branch + default `branch_setting` + zone "Main zone" (no translations, ADR-010) in one transaction (ADR-008 §14); 409 `branch.code_taken` |
| PUT | `/api/v1/branches/{id}` | CA | name, code, timezone (IANA), `isActive`; `version` |
| DELETE | `/api/v1/branches/{id}` | CA | removes its own default rows (setting, empty zones, hours, calendar, fees) only if nothing else references the branch; else 409 `branch.in_use` (deactivate instead) |
| GET / PUT | `/api/v1/branches/{id}/hours` | GET CA, BM (own); PUT CA | whole week `{ version, rows: [{ dayOfWeek, openTime, closeTime }] }`; close after open; no overlapping intervals on a day → 400 `branch_hours.overlap` |
| GET / POST | `/api/v1/branches/{id}/calendar` | GET CA, BM (own); POST CA | entry `{ dateFrom, dateTo, type, openTime?, closeTime? }`; times required for SpecialHours, forbidden for Holiday; `?from=&to=` filter |
| GET / PUT / DELETE | `/api/v1/branches/{id}/calendar/{entryId}` | GET CA, BM (own); PUT/DELETE CA | overlapping entries → 409 `branch_calendar.overlap` (confirmed 2026-11-08) |
| GET / PUT | `/api/v1/branches/{id}/settings` | GET CA, BM (own); PUT CA | `{ recallLimit, skipDefault, blockWhenNoCounter, waitAlertMinutes?, version }` |
| GET / PUT | `/api/v1/branches/{id}/priority-fees` | GET CA, BM (own); PUT CA | GET lists every **paid** priority type with `companyFee`, `overrideFee?`, `currencyCode`; PUT `{ version, rows: [{ priorityTypeId, feeAmount }] }` — omitted type = no override; a non-paid or other-company type → 400 `branch_priority_fee.priority_not_paid` / unknown |
- Audit on every write (`branch`, `branch_hours`, `branch_calendar`, `branch_setting`, `branch_priority_fee`; create/update/delete).
- `IDeviceConfigNotifier.ConfigChangedAsync(branchId)` after hours, calendar, `isActive` or fee changes (no-op in M2; "open/full state" in device config, §10).
- SignalR: none. Background jobs: none (the daily reset reads hours in M3).

**Web / Android / displays:** none.

**Cross-cutting:** branch scope (ADR-008 §10) on every `{id}` route; time zone validation with `TimeZoneInfo.TryFindSystemTimeZoneById` (the `aspnet:10.0` Debian image ships tzdata; add a test with `Asia/Dubai`); times are branch-local wall times (`HH:mm`), dates branch-local; store as `time`/`date` (no UTC conversion of wall times).

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008 (+ Amendment 1), ADR-009 |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | All tables, columns and checks exist (STORY-002) |
| 3 Backend | Yes | BA creates | Branch CRUD, hours, calendar (overlap rejected), settings, priority fees, permission attributes, tests; after STORY-048 |
| 4 Frontend | N/A | — | Branch screens are M4 (STORY-024) |
| 5 Android | N/A | — | No device API change |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [x] **Resolved 2026-11-08 by configurable roles (ADR-009):** default stays read-only for the Branch Manager role; a company may grant edit. Original question: **Product owner — Branch Manager rights on branch details.** Spec §8 gives "Branches … working hours, holidays, special hours, branch settings … fee override" to the Company Admin and does not list them for the Branch Manager. ADR-008 §9 therefore lets the Branch Manager **read** their own branches only. Confirm, or allow the Branch Manager to edit hours / calendar / settings of their own branches. Not blocking (default = read only).
- [x] **Resolved 2026-11-08:** overlaps rejected. Original question: Product owner — overlapping calendar entries (e.g. a holiday inside a Ramadan special-hours range). Proposal: reject overlaps (409 `branch_calendar.overlap`) so M3 has one answer per date. Alternative: allow, with Holiday winning over SpecialHours. Not blocking.
- [x] **Resolved 2026-11-08 by localisation (ADR-010):** the default zone is named "Main zone" (English only). A company adds translations when it enables another language. Original question: Product owner — the default zone created with a new branch (ADR-008 §14). Confirm the names "Main zone" / "المنطقة الرئيسية".
- [ ] Carried from STORY-002 QA: Branch 1 sample working hours (seed uses Sunday–Thursday 08:00–15:00; spec §4 lists none). Not blocking.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends on STORY-005 (plumbing, `IBranchScope`, priority types). Independent of STORY-006; both can run after STORY-005.
- First consumer of `IBranchScope`: every `{id}` route loads the branch through the filtered DbSet (404) and then checks scope (403).
- Branch create writes three tables in one transaction (branch, setting, zone) under one audit row; the zone row is visible to STORY-009.
- Delete: the FKs restrict (23001), so the handler deletes the branch's own setting/hours/calendar/fees/empty default zone first and maps any remaining reference to 409 `branch.in_use`.
Open questions: Branch Manager rights, calendar overlaps, default zone names, sample hours; none blocks analysis.

### Stage 0 — Architecture review (amendment) — project-architect — 2026-11-08
Result: Done
Changed: this story (sections 7, 8, 10); ADR-009 (new); ADR-008 Amendment 1; `docs/PROJECT_SPEC.md` §8–§13 (approved)
Notes for next stage:
- Product owner decisions applied: permission-based authorization (permission codes listed in section 7), BM rights now come from the seeded Branch Manager role (default read-only), calendar overlaps rejected.
- Now depends on STORY-048 as well (permission catalog, DB resolver, role branch scope). Stage plan unchanged (no schema change).
Open questions: default zone names, sample hours (unchanged, not blocking).

### Stage 0 — Architecture review (amendment: localisation) — project-architect — 2026-11-08
Result: Done
Changed: this story (sections 7, 10); ADR-010 (new); ADR-008 Amendment 2; ADR-009 Amendment 1; `docs/PROJECT_SPEC.md` §2, §7–§13 (approved by the product owner 2026-11-08)
Notes for next stage:
- Branch names follow the localised-text pattern (`name` + `translations`) built by STORY-051; the default zone has an English name only. Depends on STORY-051. Stage plan unchanged (no schema change here).
Open questions: none.
