# TASK-004 — Identity, JWT, refresh rotation and auth endpoints

| Field | Value |
|---|---|
| ID | TASK-004 |
| Story | STORY-003 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | To do |
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
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] Bilingual text added (en + ar) if user-facing — error codes only; texts in the web app
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- …

## 7. Notes / result
