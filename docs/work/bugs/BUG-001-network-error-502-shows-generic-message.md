# BUG-001 — Stopped `api` behind the dev proxy or Nginx shows "Something went wrong", not "Cannot reach the server"

| Field | Value |
|---|---|
| ID | BUG-001 |
| Status | New |
| Severity | Minor |
| Priority | P3 |
| Found in | STORY-004 (AC-16), frontend as reviewed in round 2 (HEAD 08be692) |
| Component | Web – core/errors (`api-error.ts`), shown by `authGuard` / interceptor |
| Owning stage | 4 (frontend) |
| Reported by | qa-tester |
| Created | 2026-11-08 |

Severity guide: **Minor** = wrong text with the session kept, no data loss.

## 1. Summary
When the API is stopped, the web proxy answers 502 with a plain-text or HTML body. The app maps it to `errors.unknown` ("Something went wrong. Please try again.") instead of `errors.network` ("Cannot reach the server"), which is the text AC-16 requires for exactly this scenario.

## 2. Environment
- Build: `frontend` production build and `ng serve`, STORY-004 at HEAD 08be692. API from `dotnet run -c Release`, `postgres:18`.
- Browser: Playwright Chromium, `http://localhost:4200`.
- Language: English and Arabic (both show the generic text).
- Test data: seeded Company Admin `admin1` (Company 1).

## 3. Steps to reproduce
1. Sign in as the Company Admin on `http://localhost:4200` (once with `npm start`, once with the production build behind `nginxinc/nginx-unprivileged` using `frontend/nginx.conf`).
2. Make the access token expire (wait until less than 30 s of its lifetime is left; QA used `Jwt__AccessTokenMinutes=1`).
3. Stop the API process or container, the AC-16 example.
4. Navigate to another page in the app.

## 4. Expected result
AC-16 of STORY-004: "the user is not signed out and stays on the current page, the translated `errors.network` (or `errors.server.unexpected_error` with the `correlationId` on a 500) is shown for the failed action". The AC names "the `api` container is stopped" as an example of the network error case. Expected toast: "Cannot reach the server" (Arabic equivalent).

## 5. Actual result
- The user stays signed in on `/home` (correct) and, after the API is back, the next navigation refreshes once and succeeds (correct).
- The toast reads "Something went wrong. Please try again." (Arabic: "حدث خطأ ما. حاول مرة أخرى.").
- Cause seen in the network log: `POST /api/v1/auth/refresh` returns a gateway error, not status 0:
  - `ng serve` proxy: `502 Bad Gateway`, `Content-Type: text/plain`.
  - Nginx: `502 Bad Gateway`, HTML body.
- `mapHttpError` (`frontend/src/app/core/errors/api-error.ts`) returns `errors.network` only for `status === 0`. A 502, 503 or 504 has no problem-details `code`, so it falls to `errors.unknown`.
- A 500 with a problem-details body and `correlationId` is handled correctly (checked with a stubbed response).

## 6. Evidence
- Screenshots (scratchpad, deviceScaleFactor 1): `.../scratchpad/shots/ac16-network-en.png`, `ac16-network-ar.png`, `ac16-nginx-apidown-en.png`.
- Playwright output: `refresh status behind nginx with API down: [ 502 ] toast: [ 'Something went wrong. Please try again.' ]`.
- `curl -i -X POST http://localhost:4200/api/v1/auth/refresh` with the API stopped: `HTTP/1.1 502 Bad Gateway`, `Content-Type: text/plain`.

## 7. Root cause (filled by owning dev stage)
…

## 8. Fix (filled by owning dev stage)
- Changed files:
- Tests added (must reproduce the bug before the fix):
- Suggested: treat status 0, 502, 503 and 504 without a problem-details `code` as `network`; add a spec case for each.

## 9. Retest (filled by QA)
| Date | Build | Result | Notes |
|---|---|---|---|
| | | Pass / Fail | Retest with the API stopped behind `ng serve` and behind Nginx, en and ar |

## 10. Stage log
<Handoffs, same format as stories.>
