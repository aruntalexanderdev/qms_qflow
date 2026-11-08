# STORY-049 — Password policy per company

| Field | Value |
|---|---|
| ID | STORY-049 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 8 (password policy, `/admin/password-policy`, `/login` expired password), 9 (`password_policy`, `password_history`, `asp_net_users.password_changed_at`), 10 (`/password-policy`, `/auth/login`, `/auth/change-password`), 11 (Security, Audit), 13 (password policy resolved) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **to set my company's password rules — length, character classes, expiry, reuse and lockout**, so that **staff passwords meet the bank's security policy**.

## 2. Background
STORY-003 left the Identity default password policy (follow-up in STORY-011). The product owner decided on 2026-11-08 that the policy is configurable per company (defaults: 12 characters, upper + lower + digit, no expiry). ADR-009 §8 puts all five areas in scope (length, classes, expiry, history, lockout) and makes it its own story: it touches login (lockout, expiry) and needs schema, unlike user CRUD (STORY-011) or role management (STORY-048). STORY-011 and STORY-047 call the validator built here. Depends on STORY-005 (plumbing) and STORY-048 (`password-policy.*` permissions).

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA (source: ADR-009 §8).

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. The policy screen `/admin/password-policy` is M4 (STORY-050). The login page must handle `auth.password_expired` with a change-password step (stage 4 here), or users of a company with expiry could not sign in.

## 7. Data and API impact
**Spec:** sections 8, 9, 10, 11, 13 (changed 2026-11-08 with approval, ADR-009).

**Database (stage 2)**
- New `password_policy` (company_id unique, min_length 8–128 default 12, require_upper true, require_lower true, require_digit true, require_symbol false, expiry_days 0–365 default 0, history_count 0–24 default 0, lockout_threshold 3–20 default 5, lockout_minutes 1–1440 default 15; check constraints for the ranges). Tenant entity with `xmin`.
- New `password_history` (company_id, user_id, password_hash, created_at; index `(user_id, created_at desc)`; raw-SQL FK `fk_password_history_user_company` `(user_id, company_id)` → `asp_net_users (id, company_id)` cascade).
- New column `asp_net_users.password_changed_at` (timestamptz null; null = `created_at`).
- No seed row (no row = defaults).

**Backend (stage 3)**
| Method | Route | Access | Notes |
|---|---|---|---|
| GET | `/api/v1/password-policy` | `password-policy.view` | the caller's company; defaults with `version` null when no row exists |
| PUT | `/api/v1/password-policy` | `password-policy.edit` | all fields + `version` (null when creating the row); upsert; audited `password_policy` / `update` |
| POST | `/api/v1/auth/login` | anonymous | unchanged, plus: company lockout threshold/duration; expired password (correct password, `expiry_days > 0`, older than that) → 403 `auth.password_expired`, no session |
| POST | `/api/v1/auth/change-password` | anonymous, rate-limited like login | `{ userName, currentPassword, newPassword }`; wrong credentials → 401 `auth.invalid_credentials` (counts toward lockout); policy failures → 400 `user.password_weak` with rule codes; success → session like login, `password_changed_at` = now, history row added and trimmed, other refresh families revoked |
- `IPasswordPolicyValidator` (Application port, Infrastructure implementation with `IPasswordHasher`): length, classes, history (`password.too_short`, `password.requires_upper`, `password.requires_lower`, `password.requires_digit`, `password.requires_symbol`, `password.reused`). Used by this story's change-password and by STORY-011 (`POST /users`, `POST /users/{id}/password`) and STORY-047 (`POST /companies/{id}/admin-users`). Identity's own `PasswordOptions` set to the defaults for Super Admin (no company).
- Lockout per company: login handler applies `lockout_threshold` / `lockout_minutes` (Identity global lockout stays the Super Admin backstop, ADR-004 §6).
- Audit: policy update. Change-password is not a setup change: not audited (like login), never logs passwords.

**Web (stage 4)**
- Login page: on 403 `auth.password_expired`, show a change-password form (current, new, repeat) calling `POST /auth/change-password` through the `SessionCoordinator` (ADR-007 A.1: it is the only code calling auth endpoints; run under the same Web Lock as login). Translations en + ar for `auth.password_expired` and every `password.*` code. Component tests.

**Android / displays:** none.

**Cross-cutting:** company isolation (CA-C2 never sees Company 1 policy); policy change applies to the next password set and next login only; no password or hash in logs or `audit_log`; rate limit on change-password.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-009 §8 |
| 1 BA | Yes | BA creates | |
| 2 Database | Yes | BA creates | `password_policy`, `password_history`, `password_changed_at`, raw-SQL FK |
| 3 Backend | Yes | BA creates | Policy endpoints, validator, login lockout/expiry, change-password, tests |
| 4 Frontend | Yes | BA creates | Expired-password step on the login page (policy screen is M4 STORY-050) |
| 5 Android | N/A | — | Devices do not use passwords |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] Product owner (low) — should an admin-set password (`POST /users/{id}/password`) force the user to change it at next sign-in? Not requested; default no (it restarts the expiry clock). Not blocking.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Runs after STORY-048 (permissions) and before STORY-011, which validates passwords with `IPasswordPolicyValidator`.
- STORY-047 may run before this story: without a `password_policy` row the defaults apply, which the Identity options already enforce.
Open questions: forced change after admin reset (low); none blocking.
