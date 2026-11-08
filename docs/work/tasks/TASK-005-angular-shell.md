# TASK-005 — Angular workspace, theme, i18n/RTL, shell layout, login and web container

| Field | Value |
|---|---|
| ID | TASK-005 |
| Story | STORY-004 |
| Stage | 4 Frontend |
| Agent | frontend-developer |
| Status | To do |
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
- [ ] Code follows common project rules (sections 5, 7, 11)
- [ ] Tests added and passing (list them below)
- [ ] Bilingual text added (en + ar)
- [ ] No business logic in components
- [ ] Lint (ESLint + Stylelint) clean
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- …

## 7. Notes / result
