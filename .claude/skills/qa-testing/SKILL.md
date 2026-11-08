---
name: qa-testing
description: Senior QA method for the QMS — test design per acceptance criterion, standard negative and edge cases for queues and dispensers, API/web/Android/display checks, bug reporting and sign-off. Use in stage 7.
---

# QA testing skill

## Test case format
```
TC-<story>-<n> | AC-<n> | <title>
Pre-conditions: <seed data: company, branch, zone, counter, dispenser, mode>
Steps: 1 … 2 … 3 …
Expected: …
Result: Pass / Fail (BUG-###) / Blocked (<reason>)
```

## Always-check list for this project
- **Numbering:** format `VB0001`; sequence per priority + service; daily reset; unique under 20 parallel issues.
- **Priority order:** VIP (level 1) called before older Normal tokens at a counter mapped to both; counter never calls unmapped pairs.
- **Limits:** issue #100 succeeds, #101 rejected with "Service full for today"; Not Available pair hidden on dispenser.
- **Mapping chain:** invalid pairs rejected at each level with a clear message.
- **Dispenser modes:** PrintOnly (no phone row), Optional (Skip works, number saved when given), Required (Print disabled until valid); priority row hidden when only one type.
- **Zones:** token shows correct zone; call appears only on that zone's display.
- **Counter actions:** Recall limit, Skip → NoShow/Hold per setting, Transfer keeps number and priority, flow Auto/Ask.
- **Permissions:** each role can/can't; other branch's data returns 403.
- **Real-time:** console, display, dashboard update within 1 s; reconnect restores state.
- **Localisation:** every touched screen in English (default); in each other enabled language incl. RTL when one is enabled; ticket text per language; no missing keys in `en.json`; untranslated data falls back to English.
- **Privacy:** phone masked; not in logs.
- **Resilience:** dispenser out of service when offline or printer error; no token lost.

## How to run
- Automated: `dotnet test`, `npm test`, `./gradlew testDebugUnitTest`.
- API: run `docker compose up`, call endpoints with curl/HTTP files using seeded data; record request + response for failures.
- Web: Playwright or manual walkthrough; screenshot failures.
- Android: build + unit tests here; list manual Sunmi device checks with expected results for the user.

## Bugs
One bug per problem, from `docs/templates/bug.md`; exact steps, expected (with AC/spec reference), actual, evidence. Severity per the template guide.

## Sign-off (append to Stage log)
```
### Stage 7 — QA — qa-tester — YYYY-MM-DD
Result: Done | Failed
AC results: AC-1 Pass, AC-2 Pass, AC-3 Fail (BUG-004)
Bugs: BUG-004 (Major, stage 3)
Manual device checks pending: <list or none>
```
