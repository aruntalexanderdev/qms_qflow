# QMS Frontend — Angular Web App

One Angular app for admin setup, the counter console, the supervisor dashboard, reports, and the zone and counter displays (`/display/*`, which sign in with a device key). Spec: [`docs/PROJECT_SPEC.md`](../docs/PROJECT_SPEC.md) section 8.

> Status: not scaffolded yet. The shell is built in milestone **M4 Admin web**.

## Versions

| Item | Version |
| --- | --- |
| Angular (core, CLI) | **22.x** (latest stable) |
| TypeScript | 6.0.x (the range Angular 22 supports) |
| Node.js | **24 LTS**, pinned in [`.nvmrc`](.nvmrc) (Angular 22 needs Node `^22.22` or `^24.15`) |
| @microsoft/signalr | 10.x (matches the .NET 10 server) |
| @ngx-translate/core | 18.x |

Upgrade one major at a time with `ng update @angular/core @angular/cli`. Angular ships a new major about every six months.

## Structure

```
frontend/
  src/app/
    core/       api/ (typed services + models), auth/, signalr/, i18n/, guards/, interceptors/
    shared/     UI components, pipes (token number, masked phone), directives
    features/   admin/, counter/, dashboard/, reports/, display/
  public/i18n/  en.json, ar.json
  public/audio/ pre-recorded Arabic and English voice clips
```

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
npm run build
```

```bash
npm test -- --watch=false
```

The dev server proxies `/api` and `/hubs` to the backend with `proxy.conf.json`, so the app needs no CORS setup in development.
