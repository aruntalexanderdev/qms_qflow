---
name: architecture
description: Project Architect method for the QMS — impact analysis across layers, stage planning, design decisions, ADR writing and bug triage. Use in stage 0.
---

# Architecture skill

## Impact analysis checklist
For each request, answer in the story's section 7:
- **Spec:** which sections apply; does the request change any rule? (If yes → Blocked, ask the user.)
- **Database:** new/changed tables, columns, constraints, indexes, seed data; data migration needed?
- **Backend:** use cases, endpoints (method + route), validators, SignalR events, background jobs.
- **Web:** screens/routes, roles that can see them.
- **Android:** dispenser screens, config fields, printing.
- **Displays:** zone/counter display changes, voice.
- **Cross-cutting:** security (role + branch scope), audit log, localisation (ADR-010), performance (token issue / Call Next < 300 ms), concurrency.

## Stage plan rules
- A stage applies if any file it owns must change. Otherwise N/A with a one-line reason.
- Stages 0, 1, 6, 7 always apply.
- If backend changes an API used by web or Android, those stages apply at least for verification.

## Design principles to defend
- Business rules only in `Qms.Application`. Clients are thin.
- Mapping chain enforced in code and by foreign keys.
- One SignalR hub, group-based routing.
- Configuration over code: new behaviour variants become settings (company → branch → device inheritance), not branches in code.
- Phase 2 hooks (`IMessageSender`) exist but do nothing in phase 1.

## When to write an ADR
New library or pattern; an interpretation of an unclear spec rule; a deviation from the spec schema or API; anything a later developer would ask "why?" about. Use the format in `docs/adr/README.md`.

## Bug triage
1. Reproduce from the steps or reason from the code.
2. Confirm severity with the guide in the bug template.
3. Owning stage = layer where the root cause lives (rule wrong in Application → 3; constraint missing → 2; screen shows wrong data correctly returned → 4; dispenser → 5).
4. If the "bug" is a missing requirement → route to stage 1.
