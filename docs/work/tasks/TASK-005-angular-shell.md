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
- AC-1 to AC-9 (STORY-004)

## 3. Technical details
- `ng new` in `frontend/` (standalone, SCSS, routing, strict). Packages: `@angular/material` 22, `@ngx-translate/core` + http-loader, `@fontsource-variable/instrument-sans`, `@fontsource/ibm-plex-sans-arabic`, Stylelint + `stylelint-config-standard-scss`, ESLint (`angular-eslint`).
- `src/styles/`: `_tokens.scss` (`--qms-*` from `docs/design/tokens.json`, plus the `[data-theme="display"]` block), `_material-theme.scss` (`mat.theme` + overrides: radius 8px, density, typography), `_typography.scss`, `styles.scss`.
- `.stylelintrc.json`: `color-no-hex: true`, `color-named: "never"`, `ignoreFiles: ["src/styles/_tokens.scss"]`; `npm run lint` runs ESLint + Stylelint.
- `src/app/core/`: `auth/` (`AuthService` with signals: user, token in memory; `authInterceptor` with one in-flight refresh; `authGuard`, `roleGuard`), `api/auth.api.ts` (typed), `i18n/` (`LanguageService`: sets `lang` / `dir` on `<html>`, persists before sign-in, uses `/auth/me` `lang` after).
- `src/app/layout/`: `ShellComponent` (sidebar + top bar + `<router-outlet>`), `SidebarComponent` (sections per role from spec 8), `TopbarComponent`, `PageHeaderComponent`.
- `src/app/features/auth/login/`: typed reactive form; maps the problem-details `code` to `errors.<code>`.
- `src/app/features/dashboard/`: placeholder page.
- `public/i18n/en.json`, `ar.json` with `auth.*`, `shell.*`, `nav.*`, `errors.*`.
- `proxy.conf.json` (`/api`, `/hubs` → `http://localhost:8080`).
- `frontend/Dockerfile` (`node:24` build → `nginxinc/nginx-unprivileged`), `frontend/nginx.conf` (SPA fallback, gzip, `proxy_pass` to `api:8080` for `/api/` and `/hubs/` with WebSocket upgrade headers, security headers). Add `web` to `docker-compose.yml` (port 4200:8080).
- Tests: `AuthService` (login, refresh, logout), `authInterceptor` (401 → refresh once → retry), `LanguageService` (dir switch), `LoginComponent` (success and error).

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
