# BUG-### — <Short title: what is wrong, where>

| Field | Value |
|---|---|
| ID | BUG-### |
| Status | New / Triaged / In fix / Code Review / Retest / Done / Rejected |
| Severity | Critical / Major / Minor / Trivial |
| Priority | P1 / P2 / P3 |
| Found in | STORY-### / Milestone / Version or commit |
| Component | Backend / Web – <screen> / Android dispenser / Display / Database |
| Owning stage | 2 / 3 / 4 / 5 (set by architect at triage) |
| Reported by | qa-tester / <name> |
| Created | YYYY-MM-DD |

Severity guide: **Critical** = tokens cannot be issued or called, data loss, security hole · **Major** = a workflow or rule is wrong with no workaround · **Minor** = wrong behaviour with a workaround, or wrong text · **Trivial** = cosmetic.

## 1. Summary
<One sentence.>

## 2. Environment
- Build / commit:
- Device / browser / OS:
- Language: <language code, e.g. en>
- Test data: company, branch, zone, counter, dispenser used

## 3. Steps to reproduce
1. …
2. …
3. …

## 4. Expected result
<What should happen, with the spec section or acceptance criterion: e.g. "AC-2 of STORY-012; spec 6 – Call Next rule 2".>

## 5. Actual result
<What happens. Include token numbers, error messages, HTTP status, log extract (no phone numbers).>

## 6. Evidence
<Screenshots, request/response, log lines.>

## 7. Root cause (filled by owning dev stage)
…

## 8. Fix (filled by owning dev stage)
- Changed files:
- Tests added (must reproduce the bug before the fix):

## 9. Retest (filled by QA)
| Date | Build | Result | Notes |
|---|---|---|---|
| | | Pass / Fail | |

## 10. Stage log
<Handoffs, same format as stories.>
