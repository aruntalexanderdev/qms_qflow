---
name: run-feature
description: Run a QMS story or bug through the full workflow in order — project-architect (0), business-analyst (1), database-architect (2), backend-developer (3), frontend-developer (4), android-developer (5), code-reviewer (6), qa-tester (7) — with review and QA loops. Use when the user says /run-feature STORY-### or asks to run a feature or bug end to end.
---

# Run a feature through the workflow

Argument: a work item ID (`STORY-###` or `BUG-###`) or a short feature description.

## Steps
1. **Find or create the work item.**
   - ID given → open `docs/work/stories/STORY-###-*.md` (or `bugs/`).
   - Description given → create the next `STORY-###` from `docs/templates/feature-story.md` with the title, user-story line and status `New`.
2. **Check prerequisites.** `docs/PROJECT_SPEC.md` must contain the real spec (not the placeholder). If not, stop and tell the user.
3. **Run the stages in order**, one subagent call each, passing the work item path. Start at the stage that matches the work item's current status; stages that already have a handoff in the Stage log (e.g. stages 0–1 done in Phase 0) are not run again.
   | Order | Subagent | Skip when |
   |---|---|---|
   | 0 | `project-architect` | never |
   | 1 | `business-analyst` | bug that is not a requirement gap |
   | 2 | `database-architect` | stage plan says N/A |
   | 3 | `backend-developer` | stage plan says N/A |
   | 4 | `frontend-developer` | stage plan says N/A |
   | 5 | `android-developer` | stage plan says N/A |
   | 6 | `code-reviewer` | never |
   | 7 | `qa-tester` | never |
   For an N/A stage, append its handoff yourself: `Result: N/A — <reason from stage plan>`.
4. **After each stage**, re-read the work item status:
   - `Blocked` → stop and show the user the open questions.
   - Status is not the expected next stage → stop and report.
5. **Loops.**
   - Stage 6 "Changes requested" → run the owning stage(s) of the Must fix items, then stage 6 again.
   - Stage 7 bugs → for each bug run its owning stage, then stage 6, then stage 7 retest.
   - Stop after 3 loops and ask the user.
6. **Finish.** When status is `Done`, report to the user: story ID, files changed per stage, AC results, open follow-ups and pending manual device checks.

## Rules
- Never run two stages at once and never change stage order.
- Do not do a stage's work yourself; always call its subagent.
- Ask the user before starting stages 2–5 when the stage plan touches more than one milestone.
