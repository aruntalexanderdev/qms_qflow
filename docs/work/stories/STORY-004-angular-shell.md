# STORY-004 — Angular shell: theme, fonts, English/Arabic + RTL, layout, login page, web container

| Field | Value |
|---|---|
| ID | STORY-004 |
| Status | Frontend |
| Priority | Must |
| Milestone | M1 (part of M4's shell, delivered early as the walking skeleton) |
| Spec sections | 2, 8 (Login, roles), 11 (Languages, no CDN); docs/design/DESIGN_SYSTEM.md |
| Requested by | Alexa (product owner) |
| Created | 2026-11-07 |

## 1. User story
As a **Company Admin**,
I want **to open the QMS web app, choose English or Arabic, sign in and see the application shell**,
so that **every later screen plugs into a themed, bilingual, secured layout**.

## 2. Background
Depends on STORY-003 (auth endpoints). The look is fixed by the design system (ADR-001, rules section 11): dark teal sidebar, white top bar, light grey page, Instrument Sans + IBM Plex Sans Arabic, all bundled.

## 3. Scope
**In scope**
- `frontend/` Angular 22 workspace (standalone, signals, zoneless if stable in 22, strict TypeScript), Angular Material 22 (M3), ESLint, Stylelint (`color-no-hex`, `color-named: never`; token files excluded), test runner per the Angular 22 default.
- Theme: `src/styles/_tokens.scss` mirroring `docs/design/tokens.json`, `_material-theme.scss`, `_typography.scss` with `@fontsource` packages (no CDN).
- i18n: ngx-translate with `public/i18n/en.json` and `ar.json`; language switch sets `<html lang dir>`; the choice is kept per user (`/auth/me` `lang`) and in local storage before sign-in.
- Layout: sidebar (navigation sections from spec 8 by role, only Dashboard live for now), top bar (menu toggle, avatar, name, role, Sign out), page header component, responsive drawer below 1024px, full RTL mirroring.
- `/login` page: username, password, language choice; problem-details errors shown by `code`.
- Auth: in-memory access token, HTTP interceptor (bearer + refresh on 401, single in-flight refresh), `authGuard` and `roleGuard`, logout.
- `/dashboard` placeholder page ("Welcome, <name>").
- `frontend/Dockerfile`: multi-stage (Node 24 build → Nginx unprivileged), Nginx config serving the SPA and proxying `/api` and `/hubs` to `api:8080`; added to `docker-compose.yml` as `web` on port 4200 → 8080.
- Dev server proxy (`proxy.conf.json`) for `/api` and `/hubs`.

**Out of scope**
- Admin screens (M4), counter console (M5), displays (M6), SignalR client (M5/M6).

## 4. Business rules
1. Every user-facing text exists in English and Arabic (rules section 7).
2. Only design tokens are used for styling (rules section 11).
3. No external CDN calls (spec 11).

## 5. Acceptance criteria

**AC-1: Sign in and see the shell**
- Given the stack runs with `docker compose up --build`
- When the seeded Company Admin opens `http://localhost:4200`, is redirected to `/login`, and signs in
- Then `/dashboard` shows the sidebar, the top bar with the user's initials, name and role, and "Welcome, <name>".

**AC-2: Wrong credentials**
- Given the login page
- When a wrong password is submitted
- Then a translated error is shown under the form ("Incorrect username or password" / its Arabic text), and the password field is cleared.

**AC-3: Arabic and RTL**
- Given the login page
- When the user switches to العربية
- Then `<html dir="rtl" lang="ar">` is set, all texts are Arabic, the font is IBM Plex Sans Arabic, and after sign-in the sidebar is on the right.

**AC-4: Session refresh**
- Given a signed-in user whose access token has expired
- When the user navigates to another page
- Then the app refreshes the token once through the cookie and the navigation succeeds without a new sign-in.

**AC-5: Guard and sign out**
- Given a signed-out user
- When `/dashboard` is opened directly
- Then the app redirects to `/login`; and after Sign out, the back button does not show protected pages.

**AC-6: Design system enforced**
- Given a component SCSS file containing `color: #ff0000`
- When `npm run lint` runs
- Then Stylelint fails. The shipped code passes lint with no hex or named colours outside `src/styles/_tokens.scss`.

**AC-7: No CDN**
- Given the production build served by the `web` container
- When the login page loads with network logging on
- Then all requests go to the same origin (fonts included).

**AC-8: Accessibility basics**
- Given the login page and the shell
- When checked with axe (or the Lighthouse accessibility audit)
- Then there are no critical violations; every input has a label, and focus rings are visible.

**AC-9: CI**
- Given a pull request changing `frontend/**`
- When CI runs
- Then `npm ci`, lint, tests and the production build pass.

## 6. UI / UX
- Follow `docs/design/DESIGN_SYSTEM.md` sections 1, 5 and 6, and `docs/design/preview.html` (shell and form).
- Login: centred white card (max 420px) on `neutral.bg`, logo + "QFlow", fields, primary "Sign in" button full width, language switch at the top end.
- Translation keys: `auth.*`, `shell.*`, `nav.*`, `errors.<code>`.

## 7. Data and API impact
- Uses `POST /auth/login`, `/auth/refresh`, `/auth/logout`, `GET /auth/me` (STORY-003).
- No schema change.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-001, ADR-004 (token in memory, cookie), ADR-006 (one origin via Nginx) |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | No schema change |
| 3 Backend | N/A | — | Uses STORY-003 endpoints as they are |
| 4 Frontend | Yes | TASK-005 | |
| 5 Android | N/A | — | |
| 6 Code review | Yes | — | Design system and accessibility check |
| 7 QA | Yes | — | EN + AR, RTL |

## 9. Non-functional notes
- First load of `/login` under 2 s on a branch PC (production build, gzip / brotli).
- No access token in local or session storage.

## 10. Open questions
- [ ] Logo: "QFlow" wordmark with a "Q" tile (as in `preview.html`) until the bank supplies its logo.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-07
Result: Done
Changed: this story (sections 7–8)
Notes for next stage: Frontend only. Nginx in the web container proxies `/api` and `/hubs`, so the browser sees one origin (ADR-006).
Open questions: none

### Stage 1 — Analysis — business-analyst — 2026-11-07
Result: Done
Changed: this story (sections 1–6, 9–10), docs/work/tasks/TASK-005-angular-shell.md
Notes for next stage: Stages 2 and 3 N/A. Start at stage 4 (TASK-005) once STORY-003 is Done.
Open questions: logo (non-blocking)
