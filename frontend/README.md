# QMS Frontend — Angular Web App

One Angular app for admin setup, the counter console, the supervisor dashboard, reports, and the zone and counter displays (`/display/*`, which sign in with a device key). Spec: [`docs/PROJECT_SPEC.md`](../docs/PROJECT_SPEC.md) section 8.

> Status: the shell (theme, English/Arabic + RTL, sign-in, layout, web container) is built in milestone **M1** by STORY-004. Admin screens come in M4, the counter console in M5, displays in M6.

## Versions

| Item | Version |
| --- | --- |
| Angular (core, CLI) | **22.x** (latest stable) |
| TypeScript | 6.0.x (the range Angular 22 supports) |
| Node.js | **24 LTS**, pinned in [`.nvmrc`](.nvmrc) (Angular 22 needs Node `^22.22` or `^24.15`) |
| @microsoft/signalr | 10.x (matches the .NET 10 server) |
| @ngx-translate/core | 18.x |
| @angular/material + CDK | 22.x (Material 3), see ADR-001 |
| Fonts | `@fontsource-variable/instrument-sans`, `@fontsource/ibm-plex-sans-arabic` |
| Stylelint | 17.x with `stylelint-config-standard-scss` 17 (`stylelint` 18 is not yet supported by that config's peer range) |
| Tests | Vitest 5 + jsdom (the Angular 22 default `ng test` runner; no browser needed) |

Upgrade one major at a time with `ng update @angular/core @angular/cli`. Angular ships a new major about every six months.

## Structure

```
frontend/
  src/app/
    core/       api/ (typed services + models), auth/ (SessionCoordinator, interceptor, guards), errors/, i18n/, navigation/, notifications/, signalr/ (later)
    layout/     shell, sidebar, top bar, page header
    shared/     UI components, pipes (token number, masked phone), directives
    features/   admin/, counter/, dashboard/, reports/, display/
  public/i18n/  en.json, ar.json
  public/audio/ pre-recorded Arabic and English voice clips
```

## Design system

Colours, fonts and spacing come from [`docs/design/DESIGN_SYSTEM.md`](../docs/design/DESIGN_SYSTEM.md) and [`tokens.json`](../docs/design/tokens.json): a dark teal sidebar, teal primary (`#0D7E8F`), a light grey page, white cards, and the Instrument Sans and IBM Plex Sans Arabic fonts.

```
src/styles/
  _tokens.scss          --qms-* CSS variables (mirror of tokens.json)
  _material-theme.scss  Angular Material M3 theme mapped to the tokens
  _typography.scss      @fontsource fonts (bundled, no CDN)
  styles.scss           imports the three files above
```

Component styles use only `var(--qms-*)`. Stylelint (`color-no-hex`, `color-named: never`) fails the build otherwise.

## Conventions

- Standalone components, `OnPush`, signals and `computed`; lazy routes per feature, guarded by role.
- Typed reactive forms. Server problem-details errors are shown per field.
- No hard-coded UI text: every string goes through ngx-translate, in English and Arabic.
- RTL-safe CSS: use logical properties (`margin-inline-start`, not `margin-left`).
- No external CDN calls, because displays run inside the bank's network. Bundle fonts and audio.

## Prerequisites (macOS / Apple Silicon)

```bash
brew install nvm
```

```bash
nvm install && nvm use
```

```bash
npm install -g @angular/cli@22
```

## Commands

```bash
cd frontend && npm ci
```

```bash
npm start
```

```bash
npm run lint
```

```bash
npm test -- --watch=false
```

```bash
npm run build
```

`npm run lint` runs ESLint (`ng lint`, angular-eslint) and Stylelint (`color-no-hex`, `color-named: never`, no raw `px` font sizes; `src/styles/_tokens.scss` is excluded). CI runs `npm ci`, `npm run lint`, `npm test -- --watch=false`, `npm run build`.

## Run it locally

The browser must see one origin, because the refresh token is an `HttpOnly; Secure; SameSite=Strict` cookie with `Path=/api/v1/auth`. Use **`http://localhost`** in Chrome, Edge or Firefox. Safari on plain-http localhost and any plain-http LAN address are not supported (the browser drops the `Secure` cookie, so the session cannot survive a reload).

**Whole stack in Docker** (web on `http://localhost:4200`, API on `http://localhost:8080`):

```bash
cp .env.example .env
```

Edit `.env`: set `JWT_SIGNING_KEY` (`openssl rand -base64 48`), keep `ASPNETCORE_ENVIRONMENT=Development`, and set the seed users: `SEED_COMPANYADMIN_USERNAME` / `SEED_COMPANYADMIN_PASSWORD` (and `SEED_SUPERADMIN_*` to try the Super Admin). Passwords need 6+ characters with upper, lower, digit and symbol. Then:

```bash
docker compose up --build
```

**Dev server** (hot reload; the API must run on `http://localhost:8080`, see `backend/README.md`):

```bash
npm start
```

Open `http://localhost:4200`. `proxy.conf.json` forwards `/api` and `/hubs` (WebSocket) to `http://localhost:8080` with no path or cookie rewrite, so the app needs no CORS setup.

The web container (`Dockerfile`, `nginx.conf`, `security-headers.conf`) serves the build and proxies `/api/` and `/hubs/` to `api:8080` on the same origin, with a self-only Content-Security-Policy; `/swagger` is not proxied. All logins from the local stack share one client address, so the API's login limit (10 per minute) applies to everyone until the forwarded-headers follow-up (hosting story).

## Auth and language (ADR-007)

- The access token and `user` live in memory only. `SessionCoordinator` is the only code calling login, refresh and logout; each runs under the Web Lock `qms-auth-session`, and the new session or sign-out is broadcast on `BroadcastChannel('qms-auth')`, so several tabs never refresh at once.
- The app restores the session at start-up by one refresh. The interceptor adds the bearer, refreshes once after a 401 and retries once; it ignores `/auth/login`, `/auth/refresh`, `/auth/logout`. `authGuard` also renews a token that expires within 30 s on every navigation.
- Language: `localStorage['qms.lang']` wins, else `user.language`, else `en`. The switch does not call the server.
