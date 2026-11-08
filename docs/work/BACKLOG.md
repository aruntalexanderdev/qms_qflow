# Backlog

The plan from Phase 0 to Phase 2. Each line becomes a story file in `docs/work/stories/` when its milestone starts. Spec: `docs/PROJECT_SPEC.md` section 12.

Status: ✅ done · 🟡 ready (stages 0–1 done) · ⬜ not started

## Phase 0 — Readiness (2026-11-07)

| Item | Status |
| --- | --- |
| Open decisions in spec §13 resolved (sequence, VIP on dispenser + VIP fee, Service 3 VIP, VIP forwarded, devices, Azure, hosted services) | ✅ |
| ADR-002 handlers + Result, ADR-003 tenant/branch scope, ADR-004 auth tokens, ADR-005 background jobs, ADR-006 Azure | ✅ (002–004 Proposed) |
| M1 stories written (STORY-001 to STORY-004) with tasks | ✅ |
| CI (GitHub Actions), PR template, `.editorconfig`, `backend/Directory.Build.props` | ✅ |
| Branch protection on `main` (GitHub settings, by the repository owner) | ⬜ |

## Phase 1A — Backend core

### M1 Foundation (walking skeleton)
| Story | Title | Stages | Status |
| --- | --- | --- | --- |
| STORY-001 | Backend solution, API host, logging, OpenAPI, health, Docker Compose | 3 | ✅ |
| STORY-002 | Full database schema, migration and seed data (Company 1, Branch 1) | 2, 3 | ✅ |
| STORY-003 | Sign-in: Identity, JWT, refresh token rotation, roles, seeded admin (+ STORY-002 follow-ups S2, S3, S5, N7) | 2, 3 | ✅ |
| STORY-004 | Angular shell: theme, fonts, English/Arabic + RTL, layout, login page, web container (note STORY-003 review N2: coordinate token refresh across browser tabs) | 4 | ✅ |

Order: 001 → 002 → 003 → 004. M1 is done when `docker compose up` runs, an admin signs in through the web app, and Swagger lists the auth endpoints.

### M2 Setup API
Conventions for all M2 stories: [ADR-008](../adr/ADR-008-setup-api-conventions.md) (Accepted with changes 2026-11-08) and [ADR-009](../adr/ADR-009-roles-permissions-password-policy.md) (roles, permissions, password policy; Accepted 2026-11-08). Stage 0 done for all twelve stories (product owner decisions applied 2026-11-08); STORY-005 in `Backend`, the others `Analysis`.

| Story | Title | Stages | Status |
| --- | --- | --- | --- |
| [STORY-005](stories/STORY-005-service-and-priority-types.md) | Service types and priority types CRUD (incl. paid flag and fee) + shared setup-API plumbing (ADR-008), permission-based endpoints with a static resolver | 2, 3 | ⬜ |
| [STORY-048](stories/STORY-048-roles-and-permissions.md) | Roles and permissions: role CRUD per company, permission catalog endpoint, DB permission resolver, default roles seeded, Identity roles migrated, `/auth/me` permissions (ADR-009) | 2, 3, 4 | ⬜ |
| [STORY-047](stories/STORY-047-company-management.md) | Company management (Super Admin): `CRUD /companies`, activate/deactivate, first Company Admin | 3 | ⬜ |
| [STORY-049](stories/STORY-049-password-policy.md) | Password policy per company: length, classes, expiry, history, lockout; change-password (ADR-009 §8) | 2, 3, 4 | ⬜ |
| [STORY-006](stories/STORY-006-company-service-mapping.md) | Company service mapping grid API (4.1) | 3 | ⬜ |
| [STORY-007](stories/STORY-007-branches.md) | Branches: details, working hours, calendar, settings, VIP fee override | 3 | ⬜ |
| [STORY-008](stories/STORY-008-branch-service-mapping.md) | Branch service mapping grid API (4.2) | 3 | ⬜ |
| [STORY-009](stories/STORY-009-zones-counters-counter-mapping.md) | Zones, counters and counter service mapping (4.3), validation chain; `is_active` on zones and counters | 2, 3 | ⬜ |
| [STORY-010](stories/STORY-010-service-flows.md) | Service flows (Auto / Ask / None), one flow per from-service (unique index) | 2, 3 | ⬜ |
| [STORY-011](stories/STORY-011-users-roles-assignments.md) | Users with one or more roles (from STORY-048), branch and counter assignment, self-protection (+ STORY-003 follow-ups: user-table company scope, assignment 23503 mapping; password validation via STORY-049; STORY-004 follow-up: endpoint to save a user's language) | 3 | ⬜ |
| [STORY-012](stories/STORY-012-devices-dispenser-settings-mapping.md) | Devices: create, registration code, active flag, dispenser settings (Show VIP default yes, consent text en/ar, idle banner), zone + service mapping (4.4) (+ STORY-002 follow-ups: §4.4 "no counter serves this service" warning, N3 device type check) | 2, 3 | ⬜ |
| [STORY-013](stories/STORY-013-ticket-templates.md) | Ticket templates: name, logo URL, fee-line text en/ar per template, preview | 2, 3 | ⬜ |

Order (dependencies): 005 → 048 → (047 ∥ 049) → (006 ∥ 007 ∥ 013) → 008 → (009 ∥ 010) → (011 ∥ 012); 012 also uses 013; 011 needs 049. STORY-048 comes right after 005 because every later endpoint is declared with permissions and tested through roles.
Single track: 005 → 048 → 047 → 049 → 006 → 007 → 008 → 009 → 010 → 013 → 012 → 011. One branch and PR per story (rules §9).
Product owner decisions of 2026-11-08 (companies story, configurable roles, password policy, active flags, consent/banner, template name and fee line, smaller rules, ADR-008 accepted) are applied to the spec, ADR-008 Amendment 1, ADR-009 and the stories.

### M3 Queue engine
| Story | Title |
| --- | --- |
| STORY-014 | Issue token: numbering per priority + service, issue rules, daily limit, row lock |
| STORY-015 | Device register, config and heartbeat endpoints |
| STORY-016 | Counter session (open, pause, resume, close) and Call Next (priority, `is_front`, `SKIP LOCKED`) |
| STORY-017 | Call specific, recall, start, skip (NoShow / Hold) |
| STORY-018 | Complete with service flow (incl. VIP forwarded to front of the Normal queue), transfer |
| STORY-019 | VIP fee: Paid / Waived, Complete blocked while Due, change priority to Normal, cancel on refusal |
| STORY-020 | Supervisor cancel / reinstate; audit log for all token actions |
| STORY-021 | SignalR hub, groups and events |
| STORY-022 | Background jobs: daily reset, no-show timeout, device offline, data retention (ADR-005) |

## Phase 1B — Web

### M4 Admin web
| Story | Title |
| --- | --- |
| STORY-023 | Company setup screens: service types, priority types (fee), company mapping grid (**first**: STORY-004 review S7 — interceptor must fail the request when the pre-request refresh returns `signed-out`/`user-changed`; plus N8, N9) |
| STORY-024 | Branch screens: details, hours, calendar, settings, fee override, branch mapping grid |
| STORY-025 | Zones, counters, counter mapping grid, service flows |
| STORY-026 | Devices screen with dispenser preview, ticket templates, users with one or more roles (+ STORY-011 follow-up: the shell language switch saves through `PUT /auth/me/language`, ADR-007 B.4) |
| STORY-050 | Companies screen (Super Admin, first Company Admin), roles screen with the permission grid (module × action), password policy screen (STORY-047, 048, 049) |

### M5 Counter console
| Story | Title |
| --- | --- |
| STORY-027 | Counter console: call, recall, start, complete, complete & send, skip, transfer, pause, keyboard shortcuts |
| STORY-028 | VIP fee at the counter: "Fee to collect" badge, Paid / Waived, change to Normal, cancel |
| STORY-029 | Staff issue-token screen (`/counter/issue`) |

### M6 Displays
| Story | Title |
| --- | --- |
| STORY-030 | Display device registration and `/display` shell (dark theme) |
| STORY-031 | Zone display: latest call, last 5, now serving, voice clips (AR/EN) |
| STORY-032 | Counter display |

## Phase 1C — Android (can run alongside 1B once M3 is done)

### M7 Android dispenser
`minSdk` 25, `targetSdk` latest; Sunmi V2 / V2 Pro / V2s / V3, T2s / K2 (spec 3.2).

| Story | Title |
| --- | --- |
| STORY-033 | App skeleton: theme from design tokens, EN/AR, kiosk (lock task), setup and registration |
| STORY-034 | Config download and refresh; one-screen UI: phone row, service, priority (Show VIP), VIP fee notice |
| STORY-035 | Issue token, confirmation screen, out-of-service states |
| STORY-036 | Sunmi printing (ticket template with fee line), ESC/POS fallback, printer status, heartbeat |

## Phase 1D — Operations and go-live

### M8 Dashboard and reports
| Story | Title |
| --- | --- |
| STORY-037 | Supervisor live dashboard, limits, offline devices, wait alerts |
| STORY-038 | Reports incl. VIP fees Paid / Waived, Excel export |

### Go-live hardening
| Story | Title |
| --- | --- |
| STORY-039 | Azure infrastructure in Bicep (dev, uat, prod), Key Vault, managed identities (ADR-006) |
| — | **Before any deployment:** forwarded headers so the login rate limit sees real client IPs (STORY-003 review S1; consider per IP + username). Also HTTPS/forwarded headers from STORY-001 review N4 |
| STORY-040 | CD pipeline: build images → ACR → Container Apps, migration bundle job, OIDC |
| STORY-041 | Monitoring: OpenTelemetry → Application Insights, alerts |
| STORY-042 | Security review (OWASP top 10), load test (token issue and Call Next < 300 ms), backup / restore drill |
| STORY-043 | UAT with the pilot branch, deployment and operations runbook |

## Phase 2 — Messaging (spec §12)

| Story | Title |
| --- | --- |
| STORY-044 | `IMessageSender`, SMS and WhatsApp adapters, messaging settings |
| STORY-045 | Message templates (AR/EN) and outbox queue with retry, `message_log` |
| STORY-046 | PhoneOnly dispenser mode and feedback rating |

Later candidates: VIP fee card / online payment on the dispenser, appointments, QR check-in, core-banking lookup.
