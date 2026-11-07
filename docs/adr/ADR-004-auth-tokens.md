# ADR-004 — Access and refresh tokens for users, keys for devices

Status: Proposed
Date: 2026-11-07
Story: STORY-003

## Context
The spec requires ASP.NET Core Identity, a JWT access token valid for 15 minutes, a rotated refresh token, and hashed device keys. It does not say where refresh tokens live, nor how the browser keeps them.

## Decision
1. **Access token:** a JWT signed with HMAC-SHA256, 15 minutes, with the claims `sub`, `company_id`, `role`, `name`, `lang`. The signing key comes from configuration (Key Vault on Azure, user-secrets locally), never from Git.
2. **Refresh token:** 256 random bits, sent to the browser in an `HttpOnly; Secure; SameSite=Strict` cookie with `Path=/api/v1/auth`. Only its SHA-256 hash is stored, in a new table **`refresh_token`** (`user_id`, `token_hash`, `family_id`, `expires_at`, `created_at`, `revoked_at`, `replaced_by_id`, `created_ip`). Lifetime is 7 days, rotated on every refresh.
3. **Reuse detection:** if a refresh token that was already rotated or revoked is presented, the whole family is revoked and the user must sign in again.
4. **Browser:** the Angular app keeps the access token in memory only (not localStorage). It refreshes on a 401 or shortly before expiry. Web and API are served from one origin (ADR-006), so the cookie works without CORS.
5. **Logout:** `POST /auth/logout` revokes the family and clears the cookie.
6. **Login protection:** Identity lockout (5 failures → 15 minutes) and an ASP.NET Core rate limiter on `/auth/login`.
7. **Devices:** the registration code is single-use and expires after 24 hours. The device key is 256 random bits, stored as a SHA-256 hash in `device.device_key_hash`, sent in `X-Device-Key`. An admin can revoke it by regenerating the registration code.
8. **SignalR:** browsers pass the access token as `access_token` on the hub connection (the standard SignalR pattern). Displays pass the device key.

## Consequences
- A stolen access token works for at most 15 minutes. A stolen refresh token is caught on reuse.
- XSS cannot read the refresh token; CSRF is limited by `SameSite=Strict` and the narrow cookie path.

## Spec impact
Section 9: add table `refresh_token` (columns above). **Needs product owner approval**, and is part of the Phase 0 pull request.
