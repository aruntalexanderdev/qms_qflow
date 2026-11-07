---
name: frontend-angular
description: Senior Angular 22 method for the QMS web app — standalone components and signals, typed API services, mapping grids, counter console, dashboard, zone/counter displays, ngx-translate English/Arabic with RTL, SignalR client, and tests. Use in stage 4.
---

# Frontend (Angular) skill

## Structure
```
frontend/src/app/
  core/        api/ (typed services per resource), auth/, signalr/, i18n/, guards/
  shared/      ui components, pipes (token number, masked phone), directives
  features/    admin/, counter/, dashboard/, reports/, display/
```
- Standalone components, `ChangeDetectionStrategy.OnPush`, signals for state, `computed` for derived state.
- Lazy-loaded routes per feature; route guards by role.
- Typed reactive forms; server validation errors (problem details) shown per field.

## Design system (rules section 11)
- Read `docs/design/DESIGN_SYSTEM.md` before building a screen; values are in `docs/design/tokens.json`.
- Theme files: `src/styles/_tokens.scss` (`--qms-*` CSS variables, mirroring `tokens.json`), `src/styles/_material-theme.scss` (Angular Material M3 mapped to the variables), `src/styles/_typography.scss` (Instrument Sans + IBM Plex Sans Arabic from `@fontsource`).
- Component SCSS uses only `var(--qms-*)`: no hex, rgb or named colours, no raw font sizes. `npm run lint` (Stylelint) must pass.
- Shell: dark teal sidebar (`nav.*`), white 64px top bar, `neutral.bg` page, flat white cards with a 12px radius. Forms: two-column card, label above input, primary Save plus text-button Cancel.
- Shared badges: `<qms-token-state-badge>`, `<qms-priority-badge>`, `<qms-counter-status-badge>`. Never restyle a state inline.
- Displays: `data-theme="display"` on `<body>` (dark tokens, `display.*`).

## API and real-time
- One typed service per resource, matching spec section 10 routes; DTO interfaces in `core/api/models`.
- `QmsHubService` wraps `@microsoft/signalr`: auto-reconnect, joins groups, exposes events as signals/observables; on reconnect, components re-fetch their snapshot.

## Screens to get right
- **Mapping grids** (company, branch, counter, dispenser): rows = service, columns = priority type (or zone); only allowed cells enabled; save whole grid; show API validation errors in the cell.
- **Counter console:** big current token with priority badge; buttons enabled by token status; F2 Call Next, F3 Recall, F4 Complete; waiting counts per mapped pair.
- **Zone display:** full-screen, large latest call, last 5 calls, voice clips played in order (Arabic then English, or per setting), no login (device key), works offline-tolerant with reconnect.
- **Dashboard:** live queues per zone + service + priority, limits used, alerts.

## Bilingual / RTL
- All text via ngx-translate keys; `en.json` and `ar.json` updated together.
- `dir="rtl"` on `<html>` for Arabic; use CSS logical properties only; mirror icons that imply direction.
- Numbers in token numbers stay Latin (`VB0001`).

## Tests
Component tests (Jest or Karma per repo setup) for main actions; service tests with HttpTestingController; `npm run build` with no warnings treated as errors in CI.
