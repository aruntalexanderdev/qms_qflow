# ADR-001 — UI design system: teal theme, design tokens, Angular Material

Status: Accepted
Date: 2026-11-07
Story: —

## Context
The spec (section 2) leaves the UI library open ("Angular Material or PrimeNG") and defines no colours, fonts or styling rules. Three clients (web, displays, Android dispenser) have to look like one product. Bank IT reviews accessibility, and the spec forbids external CDN calls from displays and dispensers.

The product owner supplied a reference UI: a dark teal sidebar, a teal primary colour, a light grey page with white cards, and the Instrument Sans font.

## Decision
1. **Design tokens are the source of truth:** `docs/design/tokens.json`, explained in `docs/design/DESIGN_SYSTEM.md`. Web and Android theme files mirror the tokens; no other place defines colours, fonts, spacing or radii.
2. **Palette:** taken from the reference UI (primary `#0D7E8F`, sidebar `#0F262A`, page `#F4F6F6`, surface `#FFFFFF`), adjusted where needed to pass WCAG 2.1 AA. The adjustments are the nav section label (`#8AA3A7`) and the input border (`#8A9094`, 3:1 for form controls).
3. **Fonts:** Instrument Sans (Latin) and IBM Plex Sans Arabic (Arabic), both OFL and bundled with the apps.
4. **Web UI library:** Angular Material 22 (Material 3), themed from the tokens. Material 3 colour roles match Jetpack Compose Material 3, so one token set covers both apps. It is maintained by the Angular team, and its CDK gives accessible overlays, tables and keyboard handling.
5. **Displays** use a dark variant built from the sidebar colour (`[data-theme="display"]`).
6. **Enforcement:** a project rule (common rules section 11), skill instructions, a code-review checklist item, and Stylelint (`color-no-hex`, `color-named: never`) in `frontend/`, excluding the token files.

## Consequences
- A brand change touches `tokens.json`, `_tokens.scss` and `Color.kt`, and nothing else.
- Components cannot use hex or named colours; reviews and lint reject them.
- Angular Material's default look has to be overridden (radius, density, typography) to match the flat reference style. This is done once, in `_material-theme.scss`.
- A future white-label build for other banks can swap the token values per company.

## Spec impact
Section 2, row "Web frontend": "Angular Material or PrimeNG" is resolved to **Angular Material (M3)**. The design system is referenced from section 2.
