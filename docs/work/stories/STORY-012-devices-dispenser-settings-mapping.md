# STORY-012 — Devices: create, registration code, dispenser settings (Show VIP default yes), zone + service mapping (4.4)

| Field | Value |
|---|---|
| ID | STORY-012 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 3.2, 4 (Device), 4.4 (+ validation rules), 5.1 steps 8–9, 6 (Issue rule 1, Zones), 7 (token modes, settings per dispenser, app behaviour 1), 8 (`/admin/branches/:id/devices`), 9 (`device`, `dispenser_setting`, `dispenser_zone_service`), 10 (`POST /branches/{id}/devices`, `PUT /devices/{id}/dispenser-settings`, `/zone-service-mapping`), 11 (Security: device keys hashed), 12 (M2), 13 (VIP on dispenser resolved) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Branch Manager** (or Company Admin), I want **to register token dispensers, zone displays and counter displays for my branch, get a one-time registration code, and configure each dispenser's token mode, settings and zone + service buttons**, so that **each device shows and issues exactly what my branch offers**.

## 2. Background
Spec §5.1 steps 8–9 and mapping level 4 of 4 (§4.4). The device later exchanges the registration code for a device key (`POST /device/register`, M3 STORY-015; ADR-004 §7: single use, 24 hours, key stored hashed). Dispenser mapping rows have no priority, so they are not backed by a mapping-chain FK (STORY-002 note): the API checks that the service is in the branch mapping and, per §4.4, **saves with a warning "no counter serves this service in this zone"** when no counter of that zone maps the service — this is the deferred STORY-002 follow-up. "Show VIP" is per dispenser, default yes (§7, §13). Depends on STORY-008 (branch mapping) and STORY-009 (zones, counters, counter mapping); uses ticket templates (STORY-013, seeded template exists).

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (Devices screen with preview is M4 STORY-026.)

## 7. Data and API impact
**Spec:** sections 4.4, 7, 8, 9, 10, 11, 13. No business rule changes. ADR-008 §14: a new dispenser gets its `dispenser_setting` with spec defaults.

**Database**
- Tables: `device` (branch_id, zone_id nullable, type, name, device_key_hash, registration_code (filtered unique), registration_code_expires_at, counter_id, last_seen_at, printer_status), `dispenser_setting` (one per device; token_mode, country_codes text[], default_country_code, phone_min, phone_max, languages text[], default_language, ticket_template_id, confirm_seconds, show_vip DB default true), `dispenser_zone_service` (branch_id, device_id, zone_id, service_type_id, sort_order; unique `(device_id, zone_id, service_type_id)`), read `branch_service_priority`, `counter`, `counter_service_priority`, `ticket_template`, `token` (in-use); `audit_log`.
- **Schema change: yes (stage 2), small:**
  1. `DispenserSetting.ConfirmSeconds` entity default is 5; spec §7 says "returns home after N seconds (default 8)". Change the entity default to 8 (no DB default exists, so no DDL); the seed may keep its explicit value — stage 2 decides and records it.
  2. STORY-002 review N3: check constraint for device type consistency, e.g. `ck_device_type_targets`: `counter_id IS NOT NULL` exactly when `type = 'CounterDisplay'`, and `zone_id IS NOT NULL` unless `type = 'ZoneDisplay'` (spec §9: zone is empty only for a lobby display). Migration e.g. `DeviceTypeConsistency`; the seed (two dispensers in Zone 1) already satisfies it.
  3. Only if the product owner approves open question 1: `consent_text_en`, `consent_text_ar`, `idle_banner_url` on `dispenser_setting`.

**Backend**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/v1/branches/{branchId}/devices` | CA, BM (own) | `[{ id, type, name, zoneId?, counterId?, isRegistered, registrationCodeExpiresAt?, lastSeenAt?, printerStatus, version }]` (live status read-only) |
| POST | `/api/v1/branches/{branchId}/devices` | CA, BM (own) | `{ type, name, zoneId?, counterId? }` → 201 with **`registrationCode` and `registrationCodeExpiresAt` (only in this response)**; Dispenser also gets default settings (PrintOnly, `showVip` true, confirm 8 s, languages en + ar, default en) |
| GET / PUT / DELETE | `/api/v1/devices/{id}` | CA, BM (own) | PUT `{ name, zoneId?, counterId?, version }` (type immutable); counter display: counter in the branch, zone = the counter's zone; DELETE removes its mapping rows and settings, refused if tokens reference it → 409 `device.in_use` |
| POST | `/api/v1/devices/{id}/registration-code` | CA, BM (own) | new single-use code, expires in 24 h, **clears `device_key_hash`** (revokes the device, ADR-004 §7) → `{ registrationCode, registrationCodeExpiresAt }` |
| GET / PUT | `/api/v1/devices/{id}/dispenser-settings` | CA, BM (own) | Dispenser only (else 409 `device.not_dispenser`); `tokenMode` PrintOnly / PrintPhoneOptional / PrintPhoneRequired — PhoneOnly → 400 `dispenser.token_mode_phase2` (spec §7, phase 2); `countryCodes` like `+971`, `defaultCountryCode` in the list; `phoneMin` ≤ `phoneMax` (1–15); `languages` non-empty subset of `en`, `ar`, `defaultLanguage` in it; `ticketTemplateId` of the company or null; `confirmSeconds` 1–120; `showVip` |
| GET / PUT | `/api/v1/devices/{id}/zone-service-mapping` | CA, BM (own) | Dispenser only; whole set `{ version, rows: [{ zoneId, serviceTypeId, sortOrder }] }`; zone of the device's branch (else 400 `zone.not_found`); service with at least one pair in the branch mapping, Available or not (else 400 `mapping.service_not_in_branch`); response and GET carry `warnings: [{ code: "mapping.no_counter_for_service", zoneId, serviceTypeId }]` when no counter of that zone maps the service with any priority (spec §4.4: save **with** the warning) |
- Registration code: cryptographically random, 8 characters from an unambiguous upper-case alphabet (no 0/O/1/I), unique among live codes (retry on 23505); stored as the spec column says; never returned by GET. Device key generation is STORY-015 (M3).
- Audit on every write (`device`, `device_registration_code` (code not in `data`), `dispenser_setting`, `dispenser_zone_service`).
- `IDeviceConfigNotifier.ConfigChangedAsync(deviceId)` after settings, mapping and device zone changes (no-op in M2; `DeviceConfigChanged` in STORY-021).
- SignalR: none in M2.

**Web / Android / displays:** none in this story. The Android app consumes these settings through `GET /device/config` (M3 STORY-015, M7).

**Cross-cutting:** branch scope through device → branch; registration codes and keys never logged; example data tests (spec §4.4): Dispenser 1 → Zone 1: Service 1, 2 and Dispenser 2 → Zone 1: Service 3, Zone 2: Service 4, 5 round-trip **without** warnings; Dispenser 1 + Zone 2 + Service 1 saves **with** the warning (no counter in Zone 2 serves Service 1); Service 6 (not in Branch 1 mapping) → 400; a Branch 2 zone on a Branch 1 dispenser → 400.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008, ADR-004 §7 |
| 1 BA | Yes | — | |
| 2 Database | Yes | BA creates | Entity default `ConfirmSeconds` 8 (spec §7); device type check constraint (STORY-002 N3) + migration; consent/banner columns only if approved |
| 3 Backend | Yes | BA creates | Device CRUD, registration code, dispenser settings, dispenser mapping with warning, tests |
| 4 Frontend | N/A | — | Devices screen is M4 (STORY-026) |
| 5 Android | N/A | — | App consumes `/device/config` in M3/M7; no app change here |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] **Product owner — settings in spec §7 with no column in §9.** §7 lists "consent text" and "idle banner image" per dispenser, and says settings "inherit company default → branch default → dispenser". §9 has only `dispenser_setting` and none of these columns, and no company/branch default tables. Proposal: phase 1 = settings per dispenser only, with code defaults (ADR-008 §14); add `consent_text_en`, `consent_text_ar` (needed for the phone modes in phase 1) and `idle_banner_url` to `dispenser_setting` if approved; company/branch defaults later. Without approval, this story ships without consent text and banner. Not blocking analysis; blocks only item 3 of stage 2.
- [ ] Product owner (low) — devices have no `is_active` (§9). A device that issued tokens cannot be deleted (history); it is retired by regenerating its code (revokes the key). Accept for phase 1? Not blocking.
- [ ] Note: the dispenser-mapping warning counts counters with **any** priority for the service (spec §4.4 wording "at least one counter mapped to that service"); the stricter per-priority check belongs to the M3 issue rules ("block when no counter", branch setting).

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends on STORY-008 and STORY-009; soft dependency on STORY-013 (`ticketTemplateId` is validated against `ticket_template`, which the seed already fills).
- Deferred follow-ups folded in: spec §4.4 "no counter serves this service in this zone" warning (STORY-002 stage 2 note); STORY-002 review N3 device type check; spec §7 default confirmation 8 s vs entity default 5.
- Device registration and keys (`/device/register`, `/device/config`, `/device/status`) are M3 STORY-015 — not here.
Open questions: consent text / idle banner / inheritance (product owner), device `is_active` (low); none blocks analysis.
