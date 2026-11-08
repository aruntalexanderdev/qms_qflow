---
name: code-review
description: Code review method for the QMS — what to check in order, severity levels, and the review report format. Use in stage 6.
---

# Code review skill

## Review order
1. **Correctness vs spec and ACs** — every AC implemented; rules match spec section 6 exactly (numbering format, priority order, limits, flows).
2. **Mapping chain** — no path lets a level use what the level above does not allow.
3. **Security and privacy** — role + branch scope on every endpoint; device key auth; no secrets; phone numbers masked and not logged; input validation.
4. **Architecture** — business logic only in `Qms.Application`; agents stayed in their owned files; schema changes only via migrations from stage 2.
5. **Concurrency and data** — atomic token sequence; `SKIP LOCKED` in Call Next; events published after commit; transactions around state change + audit.
6. **Tests** — rules and endpoints covered; tests meaningful (would fail without the change); all green.
7. **Localisation / RTL** — every user-facing text via translation keys (`en.json` / `values/` required) or translated fields (`<field>` + `<field>_translations`); logical CSS; no hard-coded language (ADR-010).
8. **Design system**: only design tokens are used (`var(--qms-*)` on the web, `ui/theme` on Android); no hex or named colours or raw font sizes; badges use the shared components; contrast and focus ring meet the rule in section 11; layout matches `docs/design/DESIGN_SYSTEM.md`.
9. **Quality** — naming per glossary (Zone, Service type, Priority type), no dead code, clear errors, no N+1.

## Severity
- **Must fix:** wrong behaviour, rule or AC not met, security/privacy issue, missing tests for a rule, broken build, architecture boundary broken. Blocks QA.
- **Should fix:** maintainability or small risk; may become a follow-up task.
- **Nice to have:** style or minor improvement; never blocks.

## Report format (append to Stage log)
```
### Stage 6 — Code review — code-reviewer — YYYY-MM-DD
Result: Approved | Changes requested
Builds/tests: backend ✅/❌ · web ✅/❌/N/A · android ✅/❌/N/A
ACs checked: AC-1 ✅, AC-2 ❌ (see M1) …

Must fix
- M1 [stage 3] path/File.cs:42 — <problem> → <expected fix>

Should fix
- S1 [stage 4] … 

Nice to have
- N1 …
```
