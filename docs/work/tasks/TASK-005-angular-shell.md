# TASK-005 — Angular workspace, theme, i18n/RTL, shell layout, login and web container

| Field | Value |
|---|---|
| ID | TASK-005 |
| Story | STORY-004 |
| Stage | 4 Frontend |
| Agent | frontend-developer |
| Status | Done |
| Estimate | L |
| Created | 2026-11-07 |

## 1. Goal
Create the Angular 22 app in `frontend/` with the design-system theme, English/Arabic + RTL, the authenticated shell, the login page, and the Nginx web container in Docker Compose.

## 2. Acceptance criteria covered
- AC-1 to AC-18 (STORY-004, scope amendment 2026-11-08). Contract: story section 7; design: ADR-007, ADR-004, ADR-001.
- Mapping: AC-1, AC-5, AC-18 (login, `/home`, guards); AC-2, AC-10 (error messages); AC-3, AC-17 (language, RTL); AC-4, AC-13 (refresh, restore); AC-11, AC-12, AC-15, AC-16 (session coordinator); AC-14 (Super Admin shell); AC-6, AC-8, AC-9 (lint, accessibility, CI); AC-7 (container, CSP, same origin).

## 3. Technical details
- `ng new` in `frontend/` (standalone, SCSS, routing, strict). Packages: `@angular/material` 22, `@ngx-translate/core` + http-loader, `@fontsource-variable/instrument-sans`, `@fontsource/ibm-plex-sans-arabic`, Stylelint + `stylelint-config-standard-scss`, ESLint (`angular-eslint`).
- `src/styles/`: `_tokens.scss` (`--qms-*` from `docs/design/tokens.json`, plus the `[data-theme="display"]` block), `_material-theme.scss` (`mat.theme` + overrides: radius 8px, density, typography), `_typography.scss`, `styles.scss`.
- `.stylelintrc.json`: `color-no-hex: true`, `color-named: "never"`, `ignoreFiles: ["src/styles/_tokens.scss"]`; `npm run lint` runs ESLint + Stylelint.
- `src/app/core/`:
  - `api/auth.api.ts` (typed, base `/api/v1/auth`, relative URLs, no `withCredentials`): `login({ userName, password })`, `refresh()`, `logout()`, `me()` (typed for later, not on the critical path). `AuthResponse` = `{ accessToken, tokenType, expiresIn, expiresAt, user }`; `CurrentUser` = `{ id, userName, name, role, companyId | null, language, branchIds, counterIds }`. Do not decode the JWT.
  - `auth/session-coordinator.ts` (ADR-007 A): the only caller of refresh and logout; each runs in `navigator.locks.request('qms-auth-session', { mode: 'exclusive' })`; inside the lock first adopt a newer broadcast session (later `expiresAt`), else call refresh and wait for the full response before releasing; single flight per tab; `BroadcastChannel('qms-auth')` messages `session` `{ accessToken, expiresAt, user }` and `signed-out`; a received `session` with a different `user.id` discards state and goes to `/`; a received `signed-out` clears memory and goes to `/login`. Expiry from `expiresIn` relative to arrival time (not `expiresAt`). Fallback to single flight only when `navigator.locks` is missing. Refresh 401: clear, broadcast `signed-out`, go to `/login` (message only for `auth.refresh_reused`); network error / 5xx: keep the session, fail the current request only.
  - `auth/auth.service.ts` (signals: user, token in memory only, never in storage); app initialiser restores the session by refresh before the first route; `authInterceptor`: bearer, refresh when the token expires within 30 s and once after a 401, retry the original request once, and skip `/auth/login`, `/auth/refresh`, `/auth/logout` (no bearer refresh logic and no refresh on their 401); `authGuard`, `roleGuard(roles[])` (redirect to `/home`).
  - `i18n/` `LanguageService` (ADR-007 B): sets `lang` / `dir` on `<html>`; `localStorage['qms.lang']` wins, else `user.language` (then stored), default `en`; the shell switch changes local storage only, no server call.
  - `errors/`: maps problem-details `code` to `errors.<code>` (en + ar): `auth.invalid_credentials`, `auth.locked_out`, `request.rate_limited` (minutes from `Retry-After`, rounded up), `validation.failed` (field `errors` map keys `userName` / `password`), `auth.refresh_reused`, `auth.unauthorized`, `auth.forbidden`, `request.invalid`, `server.unexpected_error` (+ `correlationId`), client `errors.network` (status 0), fallback. `auth.refresh_invalid` at start-up shows nothing.
- `src/app/layout/`: `ShellComponent` (sidebar + top bar + `<router-outlet>`), `SidebarComponent` (sections per role from spec 8; only the welcome entry live, others hidden; Super Admin gets the same shell with no company or branch context), `TopbarComponent` (initials, `user.name`, `roles.<role>` label, Sign out; no company name), `PageHeaderComponent`.
- `src/app/features/auth/login/`: typed reactive form (`userName`, `password`); clears the password on error; a signed-in user is redirected to `/home`.
- `src/app/features/home/`: welcome placeholder at `/home`, open to every signed-in role; `**` redirects to `/home`. No `/dashboard` route (spec 8 Supervisor dashboard belongs to the dashboard story).
- `public/i18n/en.json`, `ar.json` with `auth.*`, `shell.*`, `nav.*`, `roles.*`, `errors.*`.
- `proxy.conf.json` (`/api`, `/hubs` → `http://localhost:8080`, `ws: true`, no `pathRewrite`, no cookie rewrite).
- `frontend/Dockerfile` (`node:24` build → `nginxinc/nginx-unprivileged`), `frontend/nginx.conf`: SPA fallback, gzip, `proxy_pass` to `http://api:8080` for `/api/` and `/hubs/` with WebSocket upgrade headers, no path change and no `proxy_cookie_path` / `proxy_cookie_domain`, `Host`, `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host` set, no proxy for `/swagger` or `/openapi`, security headers and a self-only Content-Security-Policy (`default-src 'self'`; exact directives chosen by stage 4).
- `docker-compose.yml`: `web` service (`build: ./frontend`, `depends_on: [api]`, ports `127.0.0.1:${WEB_PORT:-4200}:8080`); update the header comment. `.env.example`: add `WEB_PORT=4200`. AC-1 prerequisites (`ASPNETCORE_ENVIRONMENT=Development`, `JWT_SIGNING_KEY`, `SEED_COMPANYADMIN_*`, and `SEED_SUPERADMIN_*` for AC-14) are documented in the README run steps; no secrets in Git.
- `frontend/README.md`: correct "shell comes in M4" to M1 / STORY-004 and add the local run steps (`http://localhost:4200`, Chrome / Edge / Firefox; Safari on plain http and LAN addresses are not supported).
- Tests: `SessionCoordinator` (two instances sharing a fake lock and channel give exactly one refresh request; a queued tab adopts the broadcast session; logout broadcast clears the other instance; refresh 401 broadcasts `signed-out`; network error / 5xx does not sign out; different `user.id` resets), `AuthService` (login, restore at start-up, logout), `authInterceptor` (401 → refresh once → retry once; proactive refresh within 30 s; no refresh for `/auth/login|refresh|logout`), `authGuard` / `roleGuard`, `LanguageService` (dir switch, `qms.lang` precedence over `user.language`), error mapping (`auth.locked_out`, `request.rate_limited` with `Retry-After`, `errors.network`), `LoginComponent` (success and error), Super Admin shell (no company, Super Admin nav only).
- Manual check list for QA (Chrome / Edge, `http://localhost:4200`): AC-11 two tabs, AC-12 sign-out in one tab, AC-13 F5 reload, AC-16 stop the `api` container.

## 4. Dependencies
- Needs: STORY-003 endpoints (TASK-004).
- Provides to: M4–M6 screens (shell, auth, i18n, theme).

## 5. Done checklist
- [x] Code follows common project rules (sections 5, 7, 11)
- [x] Tests added and passing (list below)
- [x] Bilingual text added (en + ar)
- [x] No business logic in components
- [x] Lint (ESLint + Stylelint) clean
- [x] Spec / ADR updated if behaviour changed (none: no spec change; ADR-007 followed, see notes for two small refinements)
- [x] Handoff written in the story's Stage log

## 6. Tests
`npm test -- --watch=false`: 12 files, 78 tests, all green (Vitest 5 + jsdom, the Angular 22 default runner).
- `session-coordinator.spec.ts` (14): two tabs sharing a fake lock and channel give exactly one refresh; a tab that heard no broadcast refreshes only after the first (never overlapping); single flight; newer session adopted without a request; 30 s margin; logout broadcast clears the other tab; refresh 401 / `auth.refresh_reused` end the session everywhere; network error and 5xx keep the session; different `user.id` resets to `/`; no Web Locks fallback; no storage writes.
- `auth.interceptor.spec.ts` (11): bearer; 401, one refresh, retry once with the new token; second 401 reaches the caller; proactive refresh within 30 s; network error on an expired token keeps the session; no bearer and no refresh for `/auth/login|refresh|logout`; non-API calls untouched.
- `auth.guards.spec.ts` (10): `authGuard` (signed out, signed in, renew on navigation, refresh 401, network failure stays and shows `errors.network`), `guestGuard`, `roleGuard` (allowed, other role to `/home`, signed out).
- `auth.service.spec.ts` (5): store facade, restore (one refresh, no navigation, swallows network error), login/logout, initialiser order.
- `language.service.spec.ts` (5): default en/ltr, switch to ar sets `lang`/`dir=rtl` and stores, `user.language` applied and stored, `qms.lang` wins, invalid stored value ignored.
- `api-error.spec.ts` (9): `auth.invalid_credentials`, `auth.locked_out`, 429 with `Retry-After` minutes (rounded up) and without, status 0, 500 with `correlationId` (only there), field errors, unknown code fallback.
- `login.component.spec.ts` (10): labels, request body, empty form, wrong credentials (password cleared), locked out, 429 minutes, network, `validation.failed` field marking, `auth.refresh_reused` message, language switch labels.
- `shell.component.spec.ts` (6): Company Admin initials, name and role label; Super Admin shell (only Home, no company or other roles' entries); Arabic role label; Sign out; failed sign-out keeps the session; menu toggle.
- `navigation.spec.ts` (3), `translations.spec.ts` (2, en and ar have identical keys; every error code, role and nav label translated), `home.component.spec.ts` (2), `initials.spec.ts` (2).
- Lint: `npm run lint` clean; Stylelint rejects `color: #ff0000` and `background: red` (AC-6, checked by hand).
- Build: `npm run build` green, initial 442 kB raw / 112 kB transfer, no warnings.
- End-to-end (Playwright + Chromium against the real API and `postgres:18`, dev server with the proxy; two more runs against the production build served by Nginx): 25 checks + 5 more all passed. Covers AC-1 to AC-5, AC-7, AC-10 to AC-17 (see the Stage log).

## 7. Notes / result
Delivered as specified. Refinements and deviations:
- **ADR-007 A, refinements** (no behaviour change for the ADR's goals):
  1. A session is identified as "newer" by its `validUntil` (local expiry), not by comparing token strings: two tokens issued in the same second are byte-identical, so a string comparison missed the other tab's refresh and sent a second request. The `session` broadcast carries `validUntil` (local epoch ms; all tabs share the PC clock) next to `accessToken`, `expiresAt`, `user`.
  2. A tab that had to wait for the lock listens up to 150 ms for the other tab's broadcast before refreshing itself (the message crosses browser processes and can trail the lock release by a few ms). Without it the e2e run sent two sequential refreshes; with it, one.
  3. Login also runs under the lock (so another tab cannot refresh with the old cookie while a new family is set).
- `authGuard` (and the shell route with `runGuardsAndResolvers: 'always'`) renews a token that expires within 30 s on every navigation. This is what makes AC-4/AC-11/AC-16 observable now, when the only live page makes no API call. Failed renewal: 401 goes to `/login`, network/5xx stays on the page with a toast.
- Sign out: if `POST /auth/logout` fails (network/5xx) the user stays signed in and sees the error, because signing out locally would leave a valid refresh cookie behind.
- Sidebar toggle: above 1024 px it hides and shows the sidebar (no 72 px icon rail: the nav has no icons yet); below 1024 px it is a drawer on the start side (right in RTL).
- `stylelint` is 17.16 (not 18): `stylelint-config-standard-scss` 17 does not accept 18 as a peer yet. Angular Material components are used for buttons and toasts; text inputs are native styled controls (label above input, per the design system). No icon font: icons are inline SVG.
- Fonts: `@fontsource-variable/instrument-sans` registers `Instrument Sans Variable`; the stack in `--qms-font-active` names it first. `inlineCritical` is off in the production build so the CSP can forbid inline scripts (`style-src` keeps `'unsafe-inline'` for Angular/Material component styles).
- Arabic wording of the new texts needs native review (non-blocking, story open question).
- Docker: `docker build ./frontend` fails in this sandbox at `npm ci` (proxy CA), the known sandbox limit; `npm ci` from the committed lockfile works locally. Nginx config verified by running `nginxinc/nginx-unprivileged` with the production `dist/` mounted.
- Not covered by automated tests: axe/Lighthouse (AC-8) was not run; inputs have labels, focus ring is global, contrast follows the design tokens. QA to run axe.
