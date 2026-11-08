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

Each level may only use what the level above allows. A counter may map a branch pair that is Not Available (it still serves waiting tokens), never a pair missing from the branch mapping.

## Token number
`[Priority prefix][Service prefix][4-digit sequence]` → `VB0001`, `NA0012`. Every priority + service pair has its own sequence per branch + business date (`NB0001` and `VB0001` both exist); resets daily; never changes during a visit, not even when the priority changes to Normal.

## Queue rules
- Queue = branch + zone + service type + priority type.
- Call Next: among the counter's mapped pairs in its zone, lowest priority level first, then `is_front` tokens, then earliest `queued_at`; lock with `FOR UPDATE SKIP LOCKED`.
- Issue: service in dispenser mapping, pair Available, under daily limit, branch open.
- Service flow modes: Auto / Ask / None; forwarded token keeps number and priority. If no counter in the zone serves the next service with that priority, a VIP token goes to the **front of the Normal queue**, is served as Normal, no new fee.

## VIP fee (paid priority)
- `priority_type.is_paid` + `fee_amount` (company), optional `branch_priority_fee` override; currency from `company.currency_code` (AED in seed).
- Dispenser shows "VIP service fee: <amount> – payable at the counter" and prints the fee line; token saved with `fee_status` = Due.
- Counter: "Fee to collect" badge; mark Paid or Waived (user + time stored) before Complete — Complete returns 409 while Due.
- Customer refuses: change priority to Normal (keeps number, audited) or cancel. Fee charged once per visit. No payment on the dispenser in phase 1.
- "Show VIP" per dispenser, default yes; staff can always issue VIP from `/counter/issue`.

## Token statuses
Waiting → Called → Serving → Completed; also Held, NoShow, Cancelled (supervisor, or staff when a fee is refused); Transfer and flow put the token back to Waiting. Each stage of a visit = one `token_visit` row.

## Dispenser token modes
PrintOnly · PrintPhoneOptional · PrintPhoneRequired · PhoneOnly (phase 2).

## Roles
Super Admin · Company Admin · Branch Manager / Supervisor · Counter Staff.

## Example data (use in tests and seed)
| Counter | Zone | Serves |
|---|---|---|
| Counter 1 | Zone 1 | Service 1, 2, 3 — Normal |
| Counter 2 | Zone 1 | Service 2, 3 — VIP |
| Counter 3 | Zone 2 | Service 4 — Normal |
| Counter 4 | Zone 2 | Service 5 — Normal |

Prefixes: Service 1 A, 2 B, 3 C, 4 D, 5 F, 6 G. Dispenser 1 → Zone 1: Service 1, 2. Dispenser 2 → Zone 1: Service 3; Zone 2: Service 4, 5. Company mapping: Services 1–6 Normal; Services 2 and 3 also VIP. Branch 1: Services 1–5 Normal, Service 2 VIP and Service 3 VIP, all Available, max 100/day. Priority types: Normal N level 2 (free), VIP V level 1 (paid, fee 50.00 AED).

## Stack
.NET 10 + EF Core + Npgsql + SignalR · PostgreSQL 16+ · Angular 22 + TypeScript 6.0 + Node 24 LTS (standalone, signals, ngx-translate localisation, English default, RTL-ready) · Kotlin + Compose + Retrofit + Sunmi printer SDK (minSdk 25; Sunmi V2/V2 Pro/V2s/V3, T2s/K2) · Docker · Azure (Container Apps, PostgreSQL Flexible Server, Key Vault, ACR) · hosted services for background jobs.
