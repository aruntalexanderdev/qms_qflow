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
- i18n: ngx-translate with `public/i18n/en.json` and `ar.json`; language switch sets `<html lang dir>`. Language rule (ADR-007 B): `localStorage['qms.lang']` wins when set; otherwise the app applies `user.language` (from the login or refresh response, not the `lang` claim) and stores it; before sign-in the default is `en`. The in-app switch is not saved to the user record (no endpoint; follow-up in section 10).
- Layout: sidebar (navigation sections from spec 8 by role; only the welcome entry is live), top bar (menu toggle, avatar, name, translated role label `roles.<role>`, Sign out), page header component, responsive drawer below 1024px, full RTL mirroring.
- `/login` page: username (`userName`), password, language choice; problem-details errors shown by `code` (`auth.invalid_credentials`, `auth.locked_out`, `request.rate_limited` with `Retry-After`, `validation.failed`, `errors.network`).
- Auth (ADR-007): in-memory access token and `user` from `AuthResponse`; HTTP interceptor (bearer; refresh when the token expires within 30 s and once after a 401 on API calls, original request retried once; never for `/auth/login`, `/auth/refresh`, `/auth/logout`); `SessionCoordinator` (Web Lock `qms-auth-session`, `BroadcastChannel('qms-auth')`, single flight per tab); session restore by refresh at start-up; cross-tab sign-out; `authGuard` and `roleGuard`; logout.
- `/home` welcome placeholder ("Welcome, <name>") open to every signed-in role. `/dashboard` is not built here: it is the spec 8 Supervisor dashboard (role-restricted) and belongs to the dashboard story.
- `frontend/Dockerfile`: multi-stage (Node 24 build → Nginx unprivileged), Nginx config serving the SPA and proxying `/api/` and `/hubs/` to `api:8080` (same origin, no path or cookie rewrite), self-only Content-Security-Policy; added to `docker-compose.yml` as `web` on `127.0.0.1:${WEB_PORT:-4200}` → 8080; `WEB_PORT=4200` in `.env.example`; compose header comment updated.
- Dev server proxy (`proxy.conf.json`) for `/api` and `/hubs` (`ws: true`), no `pathRewrite`.
- `frontend/README.md` corrected (shell is M1 / STORY-004, not M4).

**Out of scope**
- Admin screens (M4), counter console (M5), displays (M6), SignalR client (M5/M6), the Supervisor dashboard (`/dashboard`, spec 8).
- Any backend change, including an endpoint to save the user's own language and forwarded headers for the rate limiter.
- LAN and https access to the local stack; Safari on plain-http localhost.

## 4. Business rules
1. Every user-facing text exists in English and Arabic (rules section 7).
2. Only design tokens are used for styling (rules section 11).
3. No external CDN calls (spec 11).
4. Menu and route access follow the spec 8 role table: Super Admin = companies only; Company Admin = setup and reports; Branch Manager / Supervisor = branch setup and dashboard; Counter Staff = Counter console only (Spec 8). The welcome page carries no data, so it is open to every signed-in role.
5. Language: `qms.lang` in local storage wins, else `user.language`, else `en` (ADR-007 B; interprets spec 8 "language choice" and spec 2/13 "language per user").
6. The access token lives in memory only; the refresh token is the HttpOnly cookie; the app never decodes the token for UI data (ADR-004, ADR-007 B.5; spec 11).
7. Only one refresh runs at a time across all tabs of the browser; a refresh 401 ends the session in all tabs; a network error or 5xx does not (ADR-007 A; ADR-004 decision 3 unchanged).
8. The shell needs no company: Super Admin has `companyId` null, no branches, and the API has no company name, so none is shown.

## 5. Acceptance criteria

**AC-1: Sign in and see the shell**
- Given `.env` (copied from `.env.example`) has `ASPNETCORE_ENVIRONMENT=Development`, `JWT_SIGNING_KEY` set, and `SEED_COMPANYADMIN_USERNAME` / `SEED_COMPANYADMIN_PASSWORD` set, and the stack runs with `docker compose up --build`
- When the seeded Company Admin of Company 1 opens `http://localhost:4200` in Chrome or Edge, is redirected to `/login`, and signs in
- Then the app opens `/home` and shows the sidebar, the top bar with the user's initials, name (`user.name`) and the translated role label "Company Admin", and "Welcome, <name>"; the login request body was `{ "userName", "password" }` and no access token is in local or session storage.

**AC-2: Wrong credentials**
- Given the login page
- When a wrong password is submitted for the seeded Company Admin (API answers 401 `auth.invalid_credentials`)
- Then the text for `errors.auth.invalid_credentials` ("Incorrect username or password" / its Arabic text) is shown under the form, and the password field is cleared. The same text is shown for an unknown user name.

**AC-3: Arabic and RTL**
- Given the login page
- When the user switches to العربية
- Then `<html dir="rtl" lang="ar">` is set, all texts are Arabic (including the role label), the font is IBM Plex Sans Arabic, `localStorage['qms.lang']` = `ar`, and after sign-in the sidebar is on the right.

**AC-4: Session refresh**
- Given a signed-in Company Admin whose access token has expired (or whose page was reloaded)
- When the user navigates to another page (or the app loads)
- Then exactly one `POST /api/v1/auth/refresh` request is sent (cookie only, no body), the original request is retried once, the navigation succeeds without a new sign-in, and the top bar still shows the same user.

**AC-5: Guard and sign out**
- Given a signed-out user (no valid refresh cookie)
- When `/home` is opened directly
- Then the app redirects to `/login` with no error message (refresh 401 `auth.refresh_invalid` at start-up is the normal signed-out case); and after Sign out, the back button does not show protected pages and `POST /api/v1/auth/logout` was sent once.

**AC-6: Design system enforced**
- Given a component SCSS file containing `color: #ff0000`
- When `npm run lint` runs
- Then Stylelint fails. The shipped code passes lint with no hex or named colours outside `src/styles/_tokens.scss`.

**AC-7: No CDN**
- Given the production build served by the `web` container
- When the login page loads with network logging on
- Then all requests go to the same origin `http://localhost:4200` (fonts included), the response carries a self-only Content-Security-Policy, and `/swagger` is not served by the web container. LAN and https access are out of scope.

**AC-8: Accessibility basics**
- Given the login page and the shell
- When checked with axe (or the Lighthouse accessibility audit)
- Then there are no critical violations; every input has a label, and focus rings are visible.

**AC-9: CI**
- Given a pull request changing `frontend/**`
- When CI runs
- Then `npm ci`, lint, tests and the production build pass.

**AC-10: Account locked and rate limited**
- Given the seeded Company Admin of Company 1
- When a wrong password is submitted 5 times in a row and the account is then locked
- Then the sixth attempt (API 401 `auth.locked_out`) shows the translated `errors.auth.locked_out` text (en and ar) and not the invalid-credentials text.
- And Given more than 10 login requests in 60 s from the same address, When another sign-in is submitted, Then the API answers 429 `request.rate_limited` and the form shows the translated text with the minutes from the `Retry-After` header (rounded up; a generic "try again later" text when the header is missing). Note: the local stack shares one client address through Nginx, so QA may reach this limit while testing in bulk (known, section 7).

**AC-11: Multi-tab refresh (N2)**
- Given the seeded Company Admin is signed in on `http://localhost:4200` in two tabs of the same Chrome or Edge profile, and both access tokens have expired (token lifetime 900 s, or the browser was restarted with both tabs restored, so both tabs start by refreshing)
- When the user navigates to another page in both tabs at the same moment (or reloads both tabs at once)
- Then both tabs stay signed in on the same user, the network log shows exactly one `POST /api/v1/auth/refresh` reaching the server for that moment, the other tab uses the session broadcast on `BroadcastChannel('qms-auth')` (or, if it heard no broadcast yet, runs its own refresh only after the first one finished, with the rotated cookie), and the API never returns `auth.refresh_reused`.

**AC-12: Cross-tab sign-out**
- Given the seeded Company Admin is signed in in two tabs
- When the user clicks Sign out in tab 1
- Then tab 1 shows `/login`, tab 2 also navigates to `/login` without any action and without an error message, and a later `POST /api/v1/auth/refresh` from either tab is not needed to know it (no access token remains in either tab's memory).

**AC-13: Session restore after reload**
- Given the seeded Company Admin is signed in at `/home` and the refresh cookie `qms_refresh` is present
- When the user reloads the page (F5)
- Then before the first route resolves the app sends one `POST /api/v1/auth/refresh`, stays on `/home` with the same name and role label, and shows no login page flash.

**AC-14: Super Admin shell**
- Given `.env` has `SEED_SUPERADMIN_USERNAME` and `SEED_SUPERADMIN_PASSWORD` set (`companyId` of this user is null, no branches)
- When the seeded Super Admin signs in at `http://localhost:4200`
- Then `/home` shows the shell with "Welcome, <name>", the role label "Super Admin", no company name or branch selector, and only the Super Admin navigation (Companies, as per spec 8; entries not yet built stay hidden); no Company Admin, Branch Manager or Counter console entries are shown.

**AC-15: Refresh rejected ends the session everywhere**
- Given the seeded Company Admin is signed in in two tabs and the refresh token has been revoked or expired (API answers 401 `auth.refresh_invalid`, or `auth.refresh_reused`)
- When tab 1 needs a refresh (token expired, or the page is reloaded)
- Then tab 1 clears its memory and goes to `/login`, tab 2 receives `signed-out` and goes to `/login`; for `auth.refresh_reused` the login page shows the translated `errors.auth.refresh_reused` message, for `auth.refresh_invalid` it shows no message.

**AC-16: Network error or server error during refresh keeps the session**
- Given the seeded Company Admin is signed in and the access token has expired
- When the user navigates to another page while the refresh fails with a network error (status 0, for example the `api` container is stopped) or a 5xx
- Then the user is not signed out and stays on the current page, the translated `errors.network` (or `errors.server.unexpected_error` with the `correlationId` on a 500) is shown for the failed action, and after the API is back the next navigation refreshes once and succeeds without a new sign-in.

**AC-17: Language precedence**
- Given the seeded Company Admin has `language` = `en` in the database and `localStorage['qms.lang']` is not set
- When the user signs in, switches the shell to العربية, reloads the page, and signs out and in again
- Then after the first sign-in the UI is English and `qms.lang` = `en`; after the switch `qms.lang` = `ar` and the UI stays Arabic after the reload and the next sign-in (local storage wins); the switch sends no request to the server; with `qms.lang` cleared, the next sign-in applies `user.language` again.

**AC-18: Role-based routes**
- Given a signed-in user whose role is not allowed by a route's `roleGuard` (spec 8 role table)
- When the user opens that route
- Then the app redirects to `/home` and the menu shows no entry for it; any signed-in role can open `/home`, and `/dashboard` is not a route of this story.

## 6. UI / UX
- Follow `docs/design/DESIGN_SYSTEM.md` sections 1, 5 and 6, and `docs/design/preview.html` (shell and form).
- Login: centred white card (max 420px) on `neutral.bg`, logo + "QFlow", fields, primary "Sign in" button full width, language switch at the top end.
- Translation keys: `auth.*`, `shell.*`, `nav.*`, `roles.<role>` (`SuperAdmin`, `CompanyAdmin`, `BranchManager` = "Branch Manager / Supervisor", `CounterStaff`), `errors.<code>`.
- Error texts (en + ar, never raw codes): `auth.invalid_credentials`, `auth.locked_out` (account locked, try again later), `request.rate_limited` ("Too many attempts. Try again in {minutes} minutes."), `validation.failed` (field errors under the matching field, keyed `userName` / `password`), `auth.refresh_reused` (session ended, sign in again), `auth.unauthorized`, `auth.forbidden`, `request.invalid`, `server.unexpected_error` (shows `correlationId`), `errors.network` ("Cannot reach the server"), generic fallback for unknown codes. Exact English wording is the frontend developer's; Arabic wording needs review.
- Top bar: initials from `user.name`, name, role label `roles.<role>`; no company name (not provided by the API).
- Welcome page route is `/home`; the login page switch is at the top end; the language buttons read "English" and "العربية".

## 7. Data and API impact
Original (2026-11-07): uses `POST /auth/login`, `/auth/refresh`, `/auth/logout`, `GET /auth/me` (STORY-003); no schema change.

**Scope amendment 2026-11-08: reconciled with what STORY-003 delivered (main), plus STORY-003 review N2. Design: ADR-007.**

**Spec:** sections 2, 8, 10, 11. No business rule changes. ADR-007 B interprets "language choice" (spec 8) and "language per user" (spec 2, 13).

**Database:** none.

**Backend:** none. The contract below is taken from `backend/Qms.Api/Controllers/AuthController.cs`, `Qms.Api/Auth/RefreshCookie.cs`, `AuthenticationSetup.cs`, `Common/ProblemDetailsSetup.cs` and `Qms.Application/Auth/**`. The frontend consumes it as is.

*Endpoints.* All are under `/api/v1/auth` and relative to the app origin. JSON uses camelCase; ids are uuid strings.

| Method + route | Request | Success | Errors (problem details `code`) |
|---|---|---|---|
| `POST /api/v1/auth/login` | `{ "userName": string, "password": string }` | 200 `AuthResponse` + `Set-Cookie qms_refresh`, `Cache-Control: no-store` | 400 `validation.failed` (with an `errors` map whose keys are `userName` / `password`; empty or longer than 256 characters) · 401 `auth.invalid_credentials` (also returned for an unknown or inactive user) · 401 `auth.locked_out` (5 failures in a row lock the account for 15 min) · 429 `request.rate_limited` with a `Retry-After` header in seconds (10 per 60 s per client address) |
| `POST /api/v1/auth/refresh` | no body, cookie only | 200 `AuthResponse` + rotated cookie | 401 `auth.refresh_invalid` (no cookie, unknown, expired, or user inactive) · 401 `auth.refresh_reused` (family revoked). The cookie is cleared on every 401 |
| `POST /api/v1/auth/logout` | no body, cookie only, anonymous (no bearer needed) | 204, cookie cleared, family revoked; idempotent (also 204 with no cookie) | none expected |
| `GET /api/v1/auth/me` | bearer | 200 `CurrentUser` (read fresh from the database) | 401 `auth.unauthorized` |

- `AuthResponse` = `{ accessToken: string, tokenType: "Bearer", expiresIn: number (seconds, 900), expiresAt: string (ISO 8601), user: CurrentUser }`.
- `CurrentUser` = `{ id: string, userName: string, name: string (display name), role: "SuperAdmin" | "CompanyAdmin" | "BranchManager" | "CounterStaff", companyId: string | null (null for Super Admin), language: "en" | "ar", branchIds: string[], counterIds: string[] }`.
  - The Company Admin and the Super Admin have empty `branchIds` / `counterIds`. A Company Admin sees every branch of the company without assignments.
  - The DTO has no company name.
- Any other API endpoint returns:
  - 401 `auth.unauthorized` without a valid bearer token;
  - 403 `auth.forbidden` for a role it does not allow;
  - 404 `resource.not_found`, 409 `request.conflict`, 400 `request.invalid`, 500 `server.unexpected_error`.

  Problem details also carry `traceId` and `correlationId`. The web app can show `correlationId` on a 500 error.
- Access token claims: `sub`, `company_id` (absent for Super Admin), `role`, `name`, `lang`, `iss` = `qms-api`, `aud` = `qms-clients`, HS256.
  - The web app never decodes the token for UI data (ADR-007 B.5). All user data comes from `user`.
  - SignalR (later stories) sends the token as the `access_token` query parameter on `/hubs/*`. `/hubs/qms` is not mapped yet.
- The app does not need `GET /auth/me` on the critical path, because login and refresh both return `user`. Start-up restore is a refresh (ADR-007 A.6). `auth.api.ts` still types `/me` for later use.

*Refresh cookie.* `qms_refresh`; `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`; no `Domain`; 7 days, rotated on every refresh.
- The browser sends it only on same-origin requests to `/api/v1/auth/*`. Angular `HttpClient` calls with relative URLs send it by default. No `withCredentials` and no CORS.
- `Secure` means:
  - Chrome, Edge and Firefox store it on `http://localhost` (a secure context). Use `http://localhost:4200`.
  - Not supported: Safari on plain-http localhost, and any plain-http LAN address. There the cookie is dropped and the session cannot survive a reload or the 15-minute expiry. Production is HTTPS (spec 11, ADR-006).
- Same origin must hold in every setup:
  - dev server: `proxy.conf.json` proxies `/api` and `/hubs` (`ws: true`) to `http://localhost:8080`, with no `pathRewrite` and no cookie rewrite;
  - web container: Nginx proxies `/api/` and `/hubs/` to `http://api:8080`, with no path change and no `proxy_cookie_path` / `proxy_cookie_domain`.
- Nginx sets `Host`, `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`. The API does not read them yet; that is review S1 in the hosting story.
- Nginx does not proxy `/swagger` or `/openapi`.

*Multi-tab refresh (N2):* ADR-007 A.
- Every refresh and logout runs under `navigator.locks.request('qms-auth-session', …)`.
- The new session goes to the other tabs over `BroadcastChannel('qms-auth')`. Sign-out is broadcast too.
- Single flight inside a tab.
- When to refresh: at start-up, when the token expires within 30 s, and once after a 401. The original request is retried once.
- Never on a 401 from `/auth/login`, `/auth/refresh` or `/auth/logout`.
- No server grace window; ADR-004 decision 3 is unchanged.

*Language:* ADR-007 B.
- `localStorage['qms.lang']` wins when set. Otherwise the app applies `user.language` after login or restore and stores it.
- Before sign-in the default is `en`.
- The shell switch is not saved to the user record: there is no endpoint (follow-up, STORY-011 or a later profile story).

**Web:**
- Routes:
  - `/login` (anonymous; a signed-in user is redirected to the home route);
  - a welcome placeholder for every signed-in role ("Welcome, <name>"; see correction 3 below on its route);
  - `**` goes to the home route.
- Guards: `authGuard` (memory session or start-up restore) and `roleGuard(roles[])` (on `user.role`).
- Sidebar: sections by role from the spec 8 role table:
  - Super Admin: Companies;
  - Company Admin: setup and reports;
  - Branch Manager: branch setup and dashboard;
  - Counter Staff: Counter console.

  Only the welcome entry is live; the other entries are hidden until their stories ship. Nothing in the shell needs a company:
  - Super Admin (`companyId` null, no branches) gets the same shell with no company or branch context;
  - no company name is shown, because the API has none.
- Top bar: initials from `user.name`, `user.name`, and the role label `roles.<role>` (en + ar; `BranchManager` = "Branch Manager / Supervisor"), then Sign out.
- Error translation keys `errors.<code>` (en + ar), at least:
  - `auth.invalid_credentials`, `auth.locked_out`, `request.rate_limited` (show the `Retry-After` minutes when present);
  - `validation.failed` (and field errors), `auth.refresh_reused` (session ended, shown on `/login`);
  - `auth.unauthorized`, `auth.forbidden`, `request.invalid`, `server.unexpected_error`;
  - client-only `errors.network` (status 0) and a generic fallback for unknown codes.
- `auth.refresh_invalid` at start-up is the normal signed-out case and shows no message.
- Container: `frontend/Dockerfile` + `nginx.conf` as in TASK-005, plus the proxy rules above. Add a self-only Content-Security-Policy (`default-src 'self'`; exact directives are stage 4's choice, checked in review) so AC-7 is enforced.
- Compose changes (stage 4 owns them in this story):
  - a `web` service, `build: ./frontend`, `depends_on: [api]`, ports `127.0.0.1:${WEB_PORT:-4200}:8080` (loopback like the API; LAN access needs TLS, hosting story);
  - `WEB_PORT=4200` in `.env.example`;
  - update the compose header comment.

  The API already requires `JWT_SIGNING_KEY`. AC-1 needs `ASPNETCORE_ENVIRONMENT=Development` (the compose default is Production, which does not seed) and `SEED_COMPANYADMIN_USERNAME` / `_PASSWORD` in `.env`. `.env.example` already sets Development.
- `frontend/README.md` still says the shell comes in M4; stage 4 corrects it to M1 / STORY-004.

**Android / displays:** none.

**Cross-cutting:**
- Security:
  - the access token stays in memory only;
  - no token in storage or logs;
  - no CORS;
  - same-origin CSP;
  - a refresh 401 ends the session in all tabs.
- Local rate limit: every login through Nginx (or the dev proxy) shares one client address, so the whole local stack gets 10 logins per minute. QA may see 429 when testing in bulk. That is expected until review S1 (forwarded headers) is done in the hosting story. No backend change here.
- Bilingual: all new keys in en + ar. Role labels and error codes are translated, never shown raw.
- Performance: the first `/login` load under 2 s (section 9). Start-up adds one refresh call.

**Corrections the BA must make in sections 3, 5 and 6 (stale since STORY-003 or in conflict with spec 8):**
1. Section 3, i18n: "kept per user (`/auth/me` `lang`)" becomes the ADR-007 B rule:
   - `user.language` is the field;
   - `lang` is a claim the app does not read;
   - no server write.
2. Section 3, auth: add start-up session restore through refresh, the multi-tab coordination (ADR-007 A) and the cross-tab sign-out. The 401 refresh applies to API calls only, never to the `/auth/*` calls.
3. `/dashboard` is the spec 8 Supervisor dashboard, and its access is by role. Counter Staff have "Counter console only" and Super Admin has company administration only. A welcome page for every role at `/dashboard` therefore conflicts with spec 8. Put the placeholder on a role-neutral route (proposal: `/home`) and leave `/dashboard` to the dashboard story. Update AC-1 and AC-5 accordingly.
4. AC-1: precondition `.env` with `ASPNETCORE_ENVIRONMENT=Development`, `JWT_SIGNING_KEY` and the `SEED_COMPANYADMIN_*` pair. The role shown is the translated label.
5. AC-2: the error comes from code `auth.invalid_credentials`. Add an AC (or extend AC-2) for `auth.locked_out` and for 429 `request.rate_limited`.
6. AC-4: say "the access token expired or the page was reloaded", and state that exactly one refresh request is sent.
7. New AC for N2:
   - two tabs signed in, both tokens expired (or both reloaded at once), both navigate; both stay signed in and the server sees no `auth.refresh_reused`;
   - signing out in one tab sends the other tab to `/login`.
8. New AC: a Super Admin signs in and sees the shell with no company, and only the Super Admin sections.
9. AC-7 / section 9: the local stack is reached on `http://localhost:4200` (Chrome, Edge or Firefox). LAN and https are out of scope.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-001, ADR-004 (token in memory, cookie; cross-reference to ADR-007 added), ADR-006 (one origin via Nginx), **ADR-007** (multi-tab refresh, language precedence; amendment 2026-11-08) |
| 1 BA | Yes | — (TASK-005 updated) | Amendment (done 2026-11-08): apply the corrections in section 7 to sections 3, 5 and 6, and update TASK-005 (contract, ADR-007, home route, compose `web`, tests) |
| 2 Database | N/A | — | No schema change |
| 3 Backend | N/A | — | The STORY-003 endpoints cover everything this story consumes (checked against the code on main). Saving the user's own language needs a new endpoint; that is a follow-up, not in scope (ADR-007 B.4). Forwarded headers for the rate limiter belong to review S1 / the hosting story |
| 4 Frontend | Yes | [TASK-005](../tasks/TASK-005-angular-shell.md) (AC-1 to AC-18) | Also owns `frontend/**`, `docker-compose.yml` (`web` service) and `.env.example` (`WEB_PORT`) for this story |
| 5 Android | N/A | — | No dispenser change |
| 6 Code review | Yes | — | Design system and accessibility; auth: no token in storage, interceptor exclusions, `SessionCoordinator` lock and broadcast per ADR-007, Nginx same-origin (no cookie or path rewrite), CSP |
| 7 QA | Yes | — | EN + AR, RTL; multi-tab refresh and cross-tab sign-out; Super Admin shell; invalid-credentials, locked-out and 429 messages; Chrome or Edge on `http://localhost:4200` |

## 9. Non-functional notes
- First load of `/login` under 2 s on a branch PC (production build, gzip / brotli).
- No access token in local or session storage.

## 10. Open questions
- [ ] Logo: "QFlow" wordmark with a "Q" tile (as in `preview.html`) until the bank supplies its logo.
- [ ] Arabic wording of the new error texts and role labels needs a native review (non-blocking).

Follow-ups (not in this story, no backend scope added):
- Endpoint for a user to save their own language (for example `PUT /api/v1/auth/me/language` or a profile endpoint), so the shell switch can persist per user (ADR-007 B.4). Target: STORY-011 (users) or a later "my profile" story; add to `docs/work/BACKLOG.md`.
- Forwarded headers for the rate limiter (review S1, hosting story), so the local stack no longer shares one login limit.
- Supervisor dashboard at `/dashboard` (spec 8) belongs to the dashboard story (M8).

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

### Stage 0 — Architecture review (scope amendment) — project-architect — 2026-11-08
Result: Done
Changed: this story (status, sections 7–8), docs/adr/ADR-007-web-session-handling.md (new, Proposed), docs/adr/ADR-004-auth-tokens.md (cross-reference to ADR-007 under decision 4; decisions unchanged)
Notes for next stage:
- STORY-004 was planned before the backend existed. Section 7 now holds the real STORY-003 contract:
  - routes under `/api/v1/auth`;
  - `AuthResponse` / `CurrentUser` shapes (`language`, not `lang`; `companyId` null for Super Admin; `branchIds` / `counterIds`);
  - the `qms_refresh` cookie flags and path;
  - every error code the login page and interceptor must handle, including 429 `request.rate_limited` with `Retry-After`.
- N2 decision (ADR-007 A):
  - frontend only: Web Locks (`qms-auth-session`) serialise every refresh and logout across tabs;
  - `BroadcastChannel('qms-auth')` shares the new session and the sign-out;
  - single flight inside a tab.
  - A server grace window was rejected, because it weakens reuse detection. ADR-004 decision 3 is unchanged.
- Language (ADR-007 B): `localStorage['qms.lang']` wins; otherwise `user.language`. The in-app switch is not saved to the server (no endpoint). Follow-up for STORY-011 or a profile story.
- BA: apply the nine corrections at the end of section 7 to sections 3, 5 and 6, add the N2 and Super Admin ACs, and update TASK-005. The most important correction is a role-neutral welcome route instead of `/dashboard`, because spec 8 limits the dashboard by role.
- Stages 2, 3 and 5 stay N/A. Stage 4 also owns the compose `web` service and `WEB_PORT` in `.env.example` for this story.
Open questions:
- Follow-up, not blocking: an endpoint for a user to save their own language (STORY-011 or later). Add it to the backlog.
- The local rate limit is shared through the proxy (review S1, hosting story). Known, not blocking.

### Stage 1 — Analysis (scope amendment) — business-analyst — 2026-11-08
Result: Done
Changed: this story (status, sections 3, 4, 5, 6, 10, this log), docs/work/tasks/TASK-005-angular-shell.md
Notes for next stage:
- Stages 2 and 3 are N/A per the stage plan; the orchestrator logs their N/A handoffs. Next is stage 4 (TASK-005), which now covers AC-1 to AC-18.
- Welcome route is `/home` (open to every signed-in role); `/dashboard` is left to the dashboard story (spec 8). AC-1 and AC-5 changed accordingly.
- AC-1 to AC-9 keep their IDs. Changed: AC-1, AC-2, AC-3, AC-4, AC-5, AC-7. Unchanged: AC-6, AC-8, AC-9. New: AC-10 (locked out, 429), AC-11 (multi-tab refresh), AC-12 (cross-tab sign-out), AC-13 (restore after reload), AC-14 (Super Admin shell), AC-15 (refresh 401 ends session everywhere), AC-16 (network/5xx keeps session), AC-17 (language precedence), AC-18 (role-based routes).
- AC-14 needs `SEED_SUPERADMIN_*` in `.env`; AC-1 needs `SEED_COMPANYADMIN_*`, `ASPNETCORE_ENVIRONMENT=Development` and `JWT_SIGNING_KEY`. Test on Chrome or Edge at `http://localhost:4200`.
- The language-save endpoint is a follow-up in section 10 (STORY-011 or later); no backend scope added.
Open questions: none blocking (Arabic wording review, logo)

### Stage 2 — Database — database-architect — 2026-11-08
Result: N/A — No schema change (stage plan)
Changed: none
Notes for next stage: none
Open questions: none

### Stage 3 — Backend — backend-developer — 2026-11-08
Result: N/A — STORY-003 already provides every endpoint the shell consumes (stage plan)
Changed: none
Notes for next stage: Start stage 4 with TASK-005. Language-save endpoint added to BACKLOG (STORY-011).
Open questions: none

