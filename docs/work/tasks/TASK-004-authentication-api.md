# TASK-004 — Identity, JWT, refresh rotation and auth endpoints

| Field | Value |
|---|---|
| ID | TASK-004 |
| Story | STORY-003 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | Done |
| Estimate | M |
| Created | 2026-11-07 |

## 1. Goal
Implement sign-in, refresh, logout and `me` per ADR-004, seed the roles and the dev users, and make every endpoint protected by default.

## 2. Acceptance criteria covered
- AC-1 to AC-8 (STORY-003)

## 3. Technical details
- `Qms.Application/Auth/`: `Login`, `RefreshSession`, `Logout`, `GetCurrentUser` use cases (ADR-002), `ITokenService`, `IRefreshTokenStore`, error codes `auth.invalid_credentials`, `auth.locked_out`, `auth.refresh_invalid`, `auth.refresh_reused`.
- `Qms.Infrastructure/Identity/`: `AddIdentityCore<AppUser>` + roles + `SignInManager` (lockout 5 / 15 min), `JwtTokenService` (HMAC-SHA256, 15 min), `RefreshTokenStore` (SHA-256 hash, family, rotation, reuse revokes the family), role and user seeding (from `Seed:*` configuration; skipped when not configured).
- `Qms.Api/Controllers/AuthController` (`/api/v1/auth`): sets and clears the cookie (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/v1/auth`, 7 days).
- `AddAuthentication().AddJwtBearer()`: validate issuer, audience, lifetime and key; `ClockSkew` 30 s; also read `access_token` from the query for `/hubs` (ready for M3).
- `AddAuthorization` with a fallback policy that requires an authenticated user; policies per role for later use.
- `AddRateLimiter`: a fixed window on `auth/login` per IP (10 per minute).
- `ICurrentUser` / `ICurrentTenant` from claims (ADR-003).
- OpenAPI: bearer security scheme.
- Tests: integration tests for every AC, including reuse detection and lockout; unit tests for token creation and hashing.

## 4. Dependencies
- Needs: TASK-003 (DbContext, Identity tables, `refresh_token`).
- Provides to: TASK-005 (auth endpoints), all M2+ endpoints (`ICurrentUser`).

## 5. Done checklist
- [x] Code follows common project rules
- [x] Tests added and passing (list them below)
- [x] Bilingual text added (en + ar) if user-facing — error codes only; texts in the web app
- [x] No business logic outside `Qms.Application`
- [x] Spec / ADR updated if behaviour changed (no behaviour change; ADR-004 implemented as written; `backend/README.md`, `CLAUDE.md`, `.env.example`, `docker-compose.yml` updated for the new settings)
- [x] Handoff written in the story's Stage log

## 6. Tests
All in `backend/Qms.Tests/Auth` unless noted. Integration tests run the real API host on a copy of the seeded PostgreSQL template (Testcontainers).
- `AuthEndpointsTests` (integration): AC-1 (claims, 900 s, HS256, cookie attributes, hash-only storage, Super Admin without `company_id`), AC-2 (same 401 body for wrong password and unknown user, comparable timing), AC-3 (5 failures then correct password gives `auth.locked_out`, reset on success, 15 minute lockout), login rate limit (11th attempt gives 429), AC-4 (rotation, `replaced_by_id`, same family, expired / unknown / missing cookie, deactivated user), AC-5 (reuse revokes the family, other sessions of the user survive, concurrent refresh), AC-6 (`/me`, expired token, 30 s skew, assignments through `IgnoreQueryFilters`), AC-7 (logout clears the cookie, revokes the family, idempotent, anonymous), role and user seeding (idempotent, skipped when not configured, not in Production).
- `ProtectionTests` (integration): AC-8 (openapi paths and Bearer scheme, Swagger UI), fallback policy (attribute-less endpoint is 401, unknown route stays 404), JWT validation (wrong issuer, audience, key, algorithm, `alg: none`, expired), `access_token` query only under `/hubs`, one policy per role.
- `TokenAndScopeUnitTests` (unit): JWT creation and claims with a fixed clock, key validation, host fails to start without a signing key, refresh token generation and SHA-256 hashing (known vector), `HttpContextCurrentUser`, `RequestTenantContext`.
- `HandlerUnitTests` (unit, fakes): Login, RefreshSession, Logout, GetCurrentUser rules and validators.
- Existing suites (152) stay green; `TestThingsController` got `[AllowAnonymous]` (the fallback policy now protects everything else).

## 7. Notes / result
Result: Done. Implemented per ADR-004.
- **Application** (`Qms.Application/Auth`): `Login`, `RefreshSession`, `Logout`, `GetCurrentUser` use cases with validators; ports `IUserAccounts`, `ITokenService`, `IRefreshTokenStore`, `IUserDirectory`; `Roles`, `AuthErrors` (`auth.invalid_credentials`, `auth.locked_out`, `auth.refresh_invalid`, `auth.refresh_reused`, `auth.unauthorized`, `auth.forbidden`); `ICurrentUser` in `Common`.
- **Infrastructure** (`Qms.Infrastructure/Identity`): `AddIdentityCore<AppUser>().AddRoles<AppRole>()` with lockout 5 / 15 min; `UserAccounts` (password check with a decoy hash for unknown users); `JwtTokenService` (HMAC-SHA256, 15 min, `JsonWebTokenHandler`); `RefreshTokenStore` (SHA-256 hash, family, atomic rotation in a transaction, reuse revokes the family); `IdentitySeeder` (four roles; Super Admin and Company 1 Company Admin only from `Seed:*`).
- **Api**: `AuthController` (`/api/v1/auth`), `RefreshCookie`, `HttpContextCurrentUser`, `AddQmsAuthentication` (JWT bearer with issuer / audience / lifetime / key / 30 s skew, `access_token` for `/hubs`, fallback policy, one policy per role, login rate limiter 10 / min per address), OpenAPI Bearer scheme.
- **Deviations**: (1) `UserManager` instead of `SignInManager` (no cookie scheme or `HttpContext` dependency in Infrastructure; same lockout API). (2) Login answers `auth.invalid_credentials` for the failing attempt that reaches the limit; the next attempt answers `auth.locked_out`. (3) `POST /auth/logout` is `[AllowAnonymous]`: the refresh cookie is the credential and the access token may be expired. (4) `Jwt:SigningKey` is required in every environment (fail fast); `docker-compose.yml` and `.env.example` pass `JWT_SIGNING_KEY` and `SEED_*`. (5) A custom `IAuthorizationMiddlewareResultHandler` lets requests that match no endpoint stay 404 (the fallback policy would otherwise make them 401). (6) `AddHandlersAndValidators` got an optional type filter so the test host registers only `Qms.Tests.Support` handlers.
- **Follow-ups**: behind a reverse proxy the rate limiter needs the real client address (forwarded headers, ADR-006 / hosting story); deactivating a user does not revoke an access token already issued (at most 15 minutes); password policy is the Identity default (decide in STORY-011).
