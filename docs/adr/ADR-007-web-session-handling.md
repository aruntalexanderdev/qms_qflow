# ADR-007 — Web session handling: one refresh at a time across browser tabs, language precedence

Status: Proposed (accepted when the STORY-004 stage 0 amendment is merged)
Date: 2026-11-08
Story: STORY-004 (STORY-003 review follow-up N2)

## Context
ADR-004 keeps the access token in memory and the refresh token in one `HttpOnly` cookie (`qms_refresh`, `Path=/api/v1/auth`), rotated on every `POST /api/v1/auth/refresh`. Decision 3 revokes the whole family when a rotated token is presented again.

All tabs of one browser share that one cookie but each tab has its own memory. If two tabs refresh at the same moment, both send the same cookie R1. One wins and gets R2. The other presents R1, which is now rotated, so the server revokes the family and both tabs are signed out (STORY-003 review N2). This happens in normal use: a teller reopens the browser with several restored tabs; every tab's token expires at about the same time; a supervisor opens the dashboard in a second tab. Because the access token lives only in memory, every page load starts with a refresh, so the race is common.

Options:
1. **Server grace window**: accept a just-rotated token for a few seconds. This weakens reuse detection, because a stolen token replayed inside the window gets a live session. It also needs server state ("already answered R1 with R2") or it issues a second family. It changes ADR-004 decision 3.
2. **Leader tab**: one tab owns refreshing; other tabs ask it. This needs leader election, hand-over when the leader closes, and timeouts. It is more code and has more failure modes.
3. **Browser lock plus broadcast**: every refresh runs inside a named Web Lock, so the browser runs at most one refresh at a time across tabs of the origin. The tab that refreshed sends the new session to the other tabs over a `BroadcastChannel`.

The spec (section 8: "Login: username + password, language choice"; section 2 and 13: "language per user") also leaves open how the language picked on the login page relates to the user's stored `language`. No API exists to change a user's own language (spec section 10 lists none; user management is STORY-011).

## Decision
**A. Serialise refresh across tabs (frontend only; ADR-004 decision 3 unchanged).**
1. One `SessionCoordinator` service (in `core/auth/`) is the only code that calls `POST /api/v1/auth/refresh` and `POST /api/v1/auth/logout`.
2. Each refresh runs inside `navigator.locks.request('qms-auth-session', { mode: 'exclusive' }, callback)`. Inside the lock the tab:
   - checks whether a newer session reached it while it waited (a broadcast with a later `expiresAt` than the token that needed refreshing). If so, it uses that session and makes no request;
   - otherwise it calls refresh. It waits for the complete response before it releases the lock, so the rotated cookie is already stored when the next tab sends its request.
   Because refreshes never overlap, each one presents the current cookie. No reuse is detected, even for a tab that was just opened and has heard no broadcast yet.
3. Inside one tab, concurrent callers (the interceptor, the app initialiser, a later SignalR `accessTokenFactory`) share one in-flight promise (single flight).
4. `BroadcastChannel('qms-auth')` messages, same origin and in memory only:
   - `session` `{ accessToken, expiresAt, user }` after a successful login or refresh. A receiving tab adopts it. If its own signed-in user has a different `user.id`, it discards its state and navigates to `/` (the cookie now belongs to the other user).
   - `signed-out` after logout or after a refresh 401 (`auth.refresh_invalid` / `auth.refresh_reused`). A receiving tab clears its memory and navigates to `/login`.
   Sharing the access token over a `BroadcastChannel` is acceptable: it never touches storage, it is limited to the same origin, and script that could read the channel could read the tab's memory too. The rule "no access token in local or session storage" still holds.
5. Logout runs inside the same lock, so it cannot interleave with a refresh in another tab.
6. When to refresh:
   - at start-up (app initialiser, before the first route resolves), to restore the session after a reload; a 401 there means "signed out" and shows no error;
   - before a request, when the token expires within 30 seconds;
   - once after a 401 on any API call except `/auth/login`, `/auth/refresh` and `/auth/logout`. The original request is then retried once.
   There are no background refresh timers, so idle tabs do not cause refresh traffic.
   Expiry is computed from `expiresIn` (seconds) relative to the time the response arrived, not from `expiresAt`, so a wrong PC clock does not matter.
7. Error handling:
   - Refresh 401: the session is over. Clear memory, broadcast `signed-out`, go to `/login`. For `auth.refresh_reused`, show `errors.auth.refresh_reused`.
   - Network error or 5xx: keep the session state and fail only the current request. Do not sign out.
8. Fallback: without `navigator.locks` (a non-secure context such as plain `http://<LAN IP>`), only the single flight inside the tab applies. Such contexts are not supported, because the `Secure` cookie is not stored there either. Production is HTTPS only (spec 11).

**B. Language precedence (interpretation of spec 8 "language choice" + "language per user").**
1. One UI preference key, `qms.lang` (`en` | `ar`), is kept in `localStorage`. It is not personal data and not a credential.
2. Before sign-in: use `qms.lang` if set, else `en`. The login page switch writes `qms.lang`.
3. After login, refresh or start-up restore: if `qms.lang` is set, it wins (the person at this PC chose it, for example on the login page). Otherwise apply `user.language` from the API response and store it in `qms.lang`.
4. The switch in the shell changes `qms.lang` only. Saving the choice to the user record needs an API that does not exist yet. That is a follow-up for STORY-011 or a later "my profile" story. It is not built in STORY-004.
5. The app never decodes the JWT for UI data. Name, role, language and assignments come from the `user` object in the login and refresh responses (or `GET /auth/me`). The `lang` claim is for the server only.

## Consequences
- Two or more tabs, reloads and simultaneous expiry no longer sign the user out. Reuse detection keeps its full strength against replayed or stolen tokens.
- Remaining case, accepted: if the server rotates the token but the response is lost (network cut mid-refresh), the next refresh presents the old cookie and the user must sign in again. This is the same as today and is rare.
- Requires Web Locks and BroadcastChannel: Chrome/Edge 69+/54+, Firefox 96+/38+, Safari 15.4+. All are evergreen on branch PCs.
- Sign out in one tab signs out every tab of that browser, which matches the single cookie.
- On a shared branch PC, the last language chosen in that browser persists until someone changes it.
- Tests (frontend): two coordinator instances sharing a fake lock and channel give exactly one refresh request; a queued tab adopts the broadcast session; logout broadcast clears the other instance; network error does not sign out.

## Spec impact
None. Section 11 (refresh token rotated) and ADR-004 decision 3 (reuse revokes the family) are unchanged. Decision B interprets section 8 ("language choice") and section 13 ("per device and user") without changing them; persisting the in-app language switch per user is a follow-up.
