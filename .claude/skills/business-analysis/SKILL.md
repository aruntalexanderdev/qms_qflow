---
name: business-analysis
description: Business Analyst method for the QMS — writing user stories, Given/When/Then acceptance criteria with concrete bank QMS data, scope, business rules from the spec, and task breakdown per stage. Use in stage 1.
---

# Business analysis skill

## Story quality bar (INVEST)
Independent, Negotiable, Valuable, Estimable, Small (fits one milestone), Testable.

## Acceptance criteria
- Format: **AC-n: name** + Given / When / Then. One behaviour per AC.
- Use concrete data from the `qms-project` example: "Given Branch 1 has Service 2 Normal Available with max 100 per day and 100 tokens issued today, When a customer taps Service 2 on Token Dispenser 1, Then no token is issued and the screen shows 'Service full for today' in the selected language."
- Always consider:
  - Happy path
  - Each limit: daily max, Not Available, branch closed / holiday, no Open counter in the zone
  - Mapping chain violations (invalid pair rejected with a clear message)
  - Permissions: which role can and cannot do it; branch scope
  - Arabic / RTL behaviour
  - Real-time effect (which display or console updates)
  - Audit entry written
- Avoid vague words: "fast", "user-friendly", "properly".

## Business rules
Quote the spec section number for every rule ("Spec 6 – Call Next rule 2"). A rule not in the spec needs an ADR from the architect.

## Task breakdown
- One task per applicable stage (2–5), from `docs/templates/task.md`.
- Each task lists the ACs it covers and its dependencies (stage 3 needs the stage 2 migration, etc.).
- Technical details: name the tables, endpoints, screens from the architect's impact analysis.

## UI notes
Describe layout and every label in English; mark texts needing Arabic. Reference the dispenser layout in spec section 7 and screens in section 8.
