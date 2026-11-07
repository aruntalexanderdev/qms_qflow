---
name: qms-project
description: Bank QMS domain knowledge shared by every agent — glossary, organization hierarchy, the four-level service mapping chain, token number format, queue rules, token lifecycle and example data. Load before any work on this project.
---

# QMS project knowledge

Full detail lives in `docs/PROJECT_SPEC.md`; this is the summary every agent must know.

## Hierarchy
Company (tenant) → Branch → Zone → Counter. Devices (token dispensers, zone displays, counter displays) belong to a branch and are installed in a zone.

## Mapping chain (spec section 4)
1. **Company service mapping** — service type (prefix A, B, C …) × allowed priority types (Normal `N` level 2, VIP `V` level 1 …).
2. **Branch service mapping** — service + priority pairs the branch offers, with Max tokens per day and Available / Not Available.
3. **Counter service mapping** — counter (inside a zone) × service + priority pairs from the branch mapping.
4. **Dispenser mapping** — dispenser × zone + service type. Priority buttons come from the branch mapping for the chosen service.

Each level may only use what the level above allows.

## Token number
`[Priority prefix][Service prefix][4-digit sequence]` → `VB0001`, `NA0012`. Sequence per branch + business date + priority type + service type; resets daily; never changes during a visit.

## Queue rules
- Queue = branch + zone + service type + priority type.
- Call Next: among the counter's mapped pairs in its zone, lowest priority level first, then earliest `queued_at`; lock with `FOR UPDATE SKIP LOCKED`.
- Issue: service in dispenser mapping, pair Available, under daily limit, branch open.
- Service flow modes: Auto / Ask / None; forwarded token keeps number and priority.

## Token statuses
Waiting → Called → Serving → Completed; also Held, NoShow, Cancelled; Transfer and flow put the token back to Waiting. Each stage of a visit = one `token_visit` row.

## Dispenser token modes
PrintOnly · PrintPhoneOptional · PrintPhoneRequired · PhoneOnly (phase 2).

## Roles
Super Admin · Company Admin · Branch Manager / Supervisor · Counter Staff.

## Example data (use in tests and seed)
| Counter | Zone | Serves |
|---|---|---|
| Counter 1 | Zone 1 | Service 1, 2, 3 — Normal |
| Counter 2 | Zone 1 | Service 2 — VIP |
| Counter 3 | Zone 2 | Service 4 — Normal |
| Counter 4 | Zone 2 | Service 5 — Normal |

Prefixes: Service 1 A, 2 B, 3 C, 4 D, 5 F, 6 G. Dispenser 1 → Zone 1: Service 1, 2. Dispenser 2 → Zone 1: Service 3; Zone 2: Service 4, 5. Branch 1: all pairs max 100/day; Service 2 VIP Not Available.

## Stack
.NET 10 + EF Core + Npgsql + SignalR · PostgreSQL 16+ · Angular (standalone, signals, ngx-translate, RTL) · Kotlin + Compose + Retrofit + Sunmi printer SDK · Docker.
