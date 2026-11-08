# ADR-007 — Web session handling: one login, refresh or logout at a time across browser tabs, language precedence

Status: Accepted (2026-11-08; part A updated to the built behaviour at STORY-004 code review, item S6; part B amended by ADR-010, see Amendment 1)
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
**A. Serialise login, refresh and logout across tabs (frontend only; ADR-004 decision 3 unchanged).**
This part records the behaviour built in STORY-004 (stage 4 review loop 1, code in `frontend/src/app/core/auth/`). The numbering of A.1 to A.8 is the same as in the first version, because code comments refer to it.

1. **One coordinator.** `SessionCoordinator` (`session-coordinator.ts`) is the only code that calls `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh` and `POST /api/v1/auth/logout`. The session lives only in memory (`SessionStore`: `accessToken`, `validUntil`, `user`).
2. **Each refresh runs under the lock, and uses a newer session when one arrived.**
   - **Lock.** Each refresh runs inside the exclusive Web Lock `qms-auth-session` (`navigator.locks`); login and logout use the same lock (A.5). The browser therefore runs at most one of the three at a time across all tabs of the origin. A tab keeps the lock until the complete response has arrived, so the rotated cookie is stored before the next tab sends its request. Each refresh presents the current cookie, so no reuse is detected, even for a tab that was just opened and has heard no broadcast.
   - **"Newer" = later `validUntil`.** `validUntil` is a local epoch time in ms: the time the response arrived plus `expiresIn` × 1000. A refresh call passes the `validUntil` of the session that needed refreshing (`null` at start-up). A session counts as newer, and is used without a request, when its `validUntil` is later than that value (or any session exists when the value is `null`) **and** it is still fresh (more than 30 s left). This is checked before asking for the lock and again inside it. Access tokens are not compared: two tokens issued for the same user in the same second are byte-identical, so "the token changed" cannot tell a new session from the old one. `validUntil` is valid across tabs because all tabs of one browser share the PC clock.
   - **Contention probe.** The coordinator first asks for the lock with `ifAvailable: true`. If the lock is free, the call runs at once. This is an *uncontended* call and never waits. If the lock is held, the call queues with a normal exclusive request and is marked *contended*. This is detected by the probe, not by timing.
   - **Grace wait (contended refresh only).** If a contended refresh finds no newer session inside the lock, it waits once for the previous holder's `session` broadcast. That broadcast travels between browser processes and can trail the lock release by a few ms. The wait is event-driven: it ends when a newer session arrives over the channel, or when a single 150 ms timer fires, whichever comes first. Then the tab checks again and sends its own refresh only if still needed. Login and logout never wait.
3. **Single flight in a tab.** Concurrent refresh callers in one tab (interceptor, app initialiser, `authGuard`, later a SignalR `accessTokenFactory`) share one in-flight promise.
4. **Messages on `BroadcastChannel('qms-auth')`, and user switch.** Messages stay in memory and on the same origin. A tab does not receive its own messages.
   - `{ type: 'session', accessToken, validUntil, expiresAt, user }` is sent after a successful login or refresh. A receiving tab with no session, or with the same `user.id`, adopts it if its `validUntil` is later than its own, and wakes a waiting refresh (A.2). Older messages are ignored.
   - `{ type: 'signed-out', reason? }` is sent after logout or after a refresh 401. `reason` is set only to `auth.refresh_reused`. A receiving tab clears its memory and goes to `/login` if it was signed in or a reason is given. With `auth.refresh_reused`, the login page shows `errors.auth.refresh_reused`.
   - **User switch.** The shared cookie can come to belong to another user, for example after a sign-in in another tab.
     - A received `session` whose `user.id` differs from the tab's signed-in user clears the tab and navigates to `/`.
     - A refresh response whose `user.id` differs from the tab's current user is handled the same way: the tab clears its memory, broadcasts the new `session` for the other tabs, navigates to `/`, and returns the outcome `user-changed`. It does not adopt the new session itself.
     - In practice the cleared tab ends on `/login`, because `authGuard` does not restore a cleared tab. The new user's session reaches it when the next `session` broadcast arrives (the login page adopts it and moves on), when the user reloads (start-up refresh), or when the user signs in.
     - The interceptor never replays a request under another identity. After a 401 and refresh it returns the original 401 when the outcome is `signed-out` or `user-changed`, or when the user in the store differs from the user the request was sent under.
   Sharing the access token over a `BroadcastChannel` is acceptable: it never touches storage, it is limited to the same origin, and script that could read the channel could read the tab's memory too. The rule "no access token in local or session storage" still holds.
5. **Login and logout under the same lock.** Login runs under `qms-auth-session`, so no other tab refreshes with the old cookie while login replaces it. Logout runs under it too, so it cannot interleave with a refresh in another tab. If `POST /auth/logout` fails (network error or 5xx), the user stays signed in and sees an error message. Nothing is cleared or broadcast, because the server has not revoked the refresh token. On success, the tab clears its memory, broadcasts `signed-out` and goes to `/login`.
6. **When to refresh, and which calls the interceptor handles.** There are no background timers, so idle tabs cause no refresh traffic.
   - At start-up (app initialiser, before the first route), with `validUntil` `null`, to restore the session after a reload. A 401 here means "signed out" and shows no error. A network error or 5xx leaves the tab signed out.
   - Before an API call, when the token expires within 30 s. If that refresh fails with a network error or 5xx and the token has not expired yet, the call goes ahead with it.
   - Once after a 401 on an API call. The original request is retried once, and only under the same user (A.4). A second 401 reaches the caller.
   - In `authGuard` on every navigation inside the shell (`runGuardsAndResolvers: 'always'`), when the token expires within 30 s. This runs on user navigation and is not a timer.
   - **Interceptor scope.** The bearer token is added only to same-origin URLs (relative, or absolute on `location.origin`) whose path starts with `/api/`. `/auth/login`, `/auth/refresh` and `/auth/logout` never carry the bearer token and never trigger a refresh, not even on their own 401.
   - Expiry is computed from `expiresIn` (seconds) relative to the time the response arrived, not from `expiresAt`, so a wrong PC clock does not matter.
7. **Errors.**
   - Refresh 401 (`auth.refresh_invalid` / `auth.refresh_reused`): the session is over in every tab. Clear memory, broadcast `signed-out`, go to `/login`.
   - Refresh network error or 5xx: keep the session state and fail only the current request. In `authGuard`, show the error and stay on the current page. Do not sign out.
   - Sign-out failure: see A.5.
8. **Without Web Locks.** If `navigator.locks` is missing (a non-secure context such as plain `http://<LAN IP>`), each call runs at once, with no probe and no grace wait. Only the single flight inside the tab applies, so two tabs can still race. Without `BroadcastChannel`, tabs do not share sessions or sign-out. Such contexts are not supported, because the `Secure` cookie is not stored there either. Production is HTTPS only (spec 11).

**B. Language precedence (interpretation of spec 8 "language choice" + "language per user").** Amended by ADR-010 (Amendment 1 below): the language list is no longer fixed to `en` | `ar`.
1. One UI preference key, `qms.lang` (`en` | `ar`), is kept in `localStorage`. It is not personal data and not a credential.
2. Before sign-in: use `qms.lang` if set, else `en`. The login page switch writes `qms.lang`.
3. After login, refresh or start-up restore: if `qms.lang` is set, it wins (the person at this PC chose it, for example on the login page). Otherwise apply `user.language` from the API response and store it in `qms.lang`.
4. The switch in the shell changes `qms.lang` only. Saving the choice to the user record needs an API that does not exist yet. That is a follow-up for STORY-011 or a later "my profile" story. It is not built in STORY-004.
5. The app never decodes the JWT for UI data. Name, role, language and assignments come from the `user` object in the login and refresh responses (or `GET /auth/me`). The `lang` claim is for the server only.

## Consequences
- Two or more tabs, reloads and simultaneous expiry no longer sign the user out. Reuse detection keeps its full strength against replayed or stolen tokens.
- Remaining case, accepted: the server rotates the token but the response never reaches the browser. The next refresh then presents the old cookie, reuse detection revokes the family, and the user must sign in again. This can happen when the network is cut mid-refresh, or when the user reloads or closes the tab during a refresh, because the browser aborts the request after the server has rotated the token. It is the same as before this ADR, and it is rare: a refresh takes milliseconds, and refreshes happen only at start-up, near expiry, after a 401 or on navigation.
- Remaining limit, accepted: a queued tab that is hidden waits on one browser timer for the grace wait (A.2). Browsers may throttle that timer to about 1 s. So a hidden tab that queued behind another tab's lock, and gets no broadcast, can hold `qms-auth-session` for about 1 s. During that time the visible tab's login, refresh or sign-out waits. A broadcast that does arrive ends the wait at once, and an uncontended refresh never waits.
- Requires Web Locks and BroadcastChannel: Chrome/Edge 69+/54+, Firefox 96+/38+, Safari 15.4+. All are evergreen on branch PCs.
- Sign out in one tab signs out every tab of that browser, which matches the single cookie. A failed sign-out signs out no tab.
- A sign-in as another user in one tab sends the other tabs of that browser to the login page. They never keep showing the previous user's page under the new identity.
- On a shared branch PC, the last language chosen in that browser persists until someone changes it.
- Tests (frontend, `session-coordinator.spec.ts`, `auth.interceptor.spec.ts`, `auth.guards.spec.ts`) cover:
  - two coordinator instances sharing a fake lock and channel send exactly one refresh request;
  - a queued tab adopts the broadcast session, including a broadcast that arrives after the lock is released (fake timers);
  - a logout broadcast clears the other instance, and a sign-out while another tab's refresh is queued leaves that tab on `/login` with no message;
  - a network error does not sign out;
  - user switch by broadcast and by refresh response, and no retry under another identity;
  - only same-origin `/api/` URLs get the bearer token.

## Spec impact
None. Section 11 (refresh token rotated) and ADR-004 (including decision 3, reuse revokes the family) are unchanged; the S6 update only records frontend behaviour. Decision B interprets section 8 ("language choice") and section 13 ("per device and user") without changing them; persisting the in-app language switch per user is a follow-up.

## Amendment 1 — Language precedence with configurable languages (ADR-010, 2026-11-08)
Part B is restated for configurable languages (product owner decision 2026-11-08, ADR-010). Part A is unchanged.

1. `qms.lang` in `localStorage` holds one language code (BCP-47, e.g. `en`). It is not personal data and not a credential.
2. **Offered languages.** The candidates come from `GET /api/v1/languages` (ADR-010 §8): anonymous → the deployment's available languages; signed-in company user → the company's enabled languages; Super Admin → available languages. The response also gives `defaultLanguage` (`en` in phase 1) and each language's `direction`.
3. **Before sign-in:** `qms.lang` if it is offered, else `defaultLanguage`. The login page switch writes `qms.lang`.
4. **After login, refresh or start-up restore** (offered list re-read for the signed-in user): `qms.lang` if it is offered; else `user.language` if it is offered; else `defaultLanguage`. The result is applied and stored in `qms.lang`. A stored choice that is no longer offered (language disabled) is replaced, never shown.
5. **Switch.** The switch lists only the offered languages by `nativeName` and is **hidden when only one language is offered** (phase 1: English only, so no switch). Choosing a language writes `qms.lang` and, once STORY-011 provides it, saves it with `PUT /auth/me/language` (M4 follow-up, STORY-026). After a switch the app re-reads `GET /auth/me`, because role names are resolved per `Accept-Language` (ADR-010 §7).
6. `<html lang>` = the code; `<html dir>` = the language's `direction` (not an `ar` check).
7. B.5 is unchanged: no UI data from the JWT.

Spec impact of the amendment: spec §8 and §11 updated with ADR-010 (approved 2026-11-08).
