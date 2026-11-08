# STORY-047 — Company management (Super Admin)

| Field | Value |
|---|---|
| ID | STORY-047 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 4 (Company), 5.1 step 1, 8 (Super Admin, `/admin/companies`), 9 (`company`, Identity users), 10 (`/companies`, `/companies/{id}/admin-users`), 11 (Security, Audit), 12 (M2), 13 (companies resolved) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Super Admin**, I want **to create, edit, activate, deactivate and delete companies and create each company's first Company Admin**, so that **a new bank (tenant) can be onboarded and its own administrators take over the setup**.

## 2. Background
Spec §5.1 step 1 ("Company — Super Admin") and §10 `CRUD /companies` had no story (open point in BACKLOG and STORY-005 section 10). The product owner added it to M2 on 2026-11-08. The Super Admin is a fixed system role with no company (ADR-009 §1); company handlers are `[CrossTenant]` (ADR-003 Amendment 1 A.4) and read with `IgnoreQueryFilters()`. Creating a company seeds its default roles (`DefaultRoles`, STORY-048); the first Company Admin is a user holding the company's system role `CompanyAdmin`, with a password checked against the company's password policy (defaults until STORY-049 lets the admin change it; no `password_policy` row = defaults, ADR-009 §8). Depends on STORY-005 (plumbing) and STORY-048 (roles, `[RequireSuperAdmin]`, `DefaultRoles`).

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. The `/admin/companies` screen is M4 (STORY-050).

## 7. Data and API impact
**Spec:** sections 4, 5.1, 8, 9, 10, 11, 13 (companies resolved 2026-11-08). No queue rule changes.

**Amendment 2026-11-08 (localisation, ADR-010). It supersedes the conflicting lines below.**
- `company` stores `name` + `name_translations`, `default_language` and `enabled_languages` (all built by STORY-051).
- The companies API exchanges `name` (required) and `translations` (optional) through `LocalizedTextRules`. Because the Super Admin acts on another company, translation keys are checked against **that** company's enabled languages, and on create against `{en}`.
- A new company gets `defaultLanguage = en` and `enabledLanguages = {en}`. The Super Admin does not set languages here; the Company Admin enables them through `/company-languages` (STORY-051). The DTO returns `defaultLanguage` and `enabledLanguages` read-only.
- Company create also seeds the default roles with English names and no translations (ADR-009 Amendment 1).
- `POST /companies/{id}/admin-users`: `language` must be one of the target company's enabled languages (400 `language.not_enabled`); when omitted, the company default is used.
- Now depends on STORY-051 as well.

**Database**
- Tables: `company` (name, name_translations, default_language, enabled_languages, code unique globally, logo_url, currency_code `ck_company_currency_code`, is_active), `role` / `role_permission` (seeded on create), `asp_net_users` + `user_role` (first admin), `password_policy` (none written; defaults apply), `audit_log`; in-use reads of every tenant table for delete.
- **Schema change: none.** All `company` columns and the unique code exist (STORY-002).

**Backend**
| Method | Route | Access | Notes |
|---|---|---|---|
| GET | `/api/v1/companies` | Super Admin | all companies, sorted by `code`, `?isActive=`; no paging (ADR-008 §4) |
| GET | `/api/v1/companies/{id}` | Super Admin | 404 `company.not_found` |
| POST | `/api/v1/companies` | Super Admin | `{ name, translations?, code, logoUrl?, currencyCode, isActive }` → 201 (ADR-010; company starts with `en` / `{en}`); creates the three default roles in the same transaction; 409 `company.code_taken` |
| PUT | `/api/v1/companies/{id}` | Super Admin | all fields + `version`; `isActive` false = deactivate (activate / deactivate per spec §8) |
| DELETE | `/api/v1/companies/{id}` | Super Admin | only while unused: no branch, service type, priority type, template, user, token … → else 409 `company.in_use`; deletes its own default roles first |
| POST | `/api/v1/companies/{id}/admin-users` | Super Admin | `{ userName, displayName, password, language }` → 201 user DTO (no password); user gets the company's `CompanyAdmin` system role; usernames unique across companies → 409 `user.user_name_taken`; password policy of the target company → 400 `user.password_weak` (Identity default options = ADR-009 defaults until STORY-049 adds `IPasswordPolicyValidator`; switch to it if STORY-049 is merged first); also usable later to restore access to a locked-out company (ADR-009 §7) |
- Access: `[RequireSuperAdmin]` (role check, STORY-048) + `[CrossTenant]` handlers; Company Admin, Branch Manager, Counter Staff → 403 `auth.forbidden`.
- Validation: ADR-008 §8 (names, code `[A-Za-z0-9_-]` 1–50); `currencyCode` three upper-case letters (ISO 4217 format); `logoUrl` relative or https on the deployment's own origin (same rule as STORY-013).
- Deactivated company (interpretation of `is_active`, ADR-008 Amendment 1 B): its users cannot sign in or refresh (401 `auth.invalid_credentials`, same answer as wrong password; refresh families revoked on deactivation) and hold no permissions on the next request (ADR-009 §6); its devices are refused in M3 (STORY-015). Data stays.
- Audit: `company` create / update / delete and `company_admin_user` create, with `audit_log.company_id` = the target company and `user_id` = the Super Admin (password `[NotAudited]`).
- `IDeviceConfigNotifier`: currency or `isActive` change → `ConfigChangedAsync` for every branch of the company (no-op in M2).
- SignalR / jobs: none.

**Web / Android / displays:** none in this story (screen in M4 STORY-050).

**Cross-cutting:** cross-tenant writes only inside `[CrossTenant]` handlers (code reviewer greps `BeginCrossTenant` / `IgnoreQueryFilters`); a created Company Admin can sign in and sees only the new company; integration tests for every endpoint (Super Admin happy paths, 403 for company users, 401 anonymous, 409 code taken / in use, stale `version`).

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-009 §1, §7; ADR-003 Amendment 1; ADR-008 |
| 1 BA | Yes | BA creates | |
| 2 Database | N/A | — | `company` table complete (STORY-002); roles tables come from STORY-048 |
| 3 Backend | Yes | BA creates | Companies CRUD, admin-user creation, login refusal for inactive companies, tests |
| 4 Frontend | N/A | — | `/admin/companies` screen is M4 (STORY-050) |
| 5 Android | N/A | — | No device API change in M2 |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] Note: licences and global settings (spec §8 Super Admin) are not specified anywhere; they stay out of scope until the product owner describes them. Not blocking.
- [ ] Note: changing `currencyCode` while tokens exist is allowed (tokens store the amount only; reports show the current currency). The BA may restrict it if the product owner wants. Not blocking.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends on STORY-005 and STORY-048 (`DefaultRoles`, `[RequireSuperAdmin]`); independent of the branch stories.
- First `[CrossTenant]` handlers: the decorator checks the Super Admin role, then opens the cross-tenant section; new tenant rows (roles) carry `CompanyId` explicitly.
- Super Admin user management beyond the first/rescue Company Admin is not in scope.
Open questions: licences / global settings (out of scope), currency change (note); none blocking.

### Stage 0 — Architecture review (amendment: localisation) — project-architect — 2026-11-08
Result: Done
Changed: this story (sections 7); ADR-010 (new); ADR-007 Amendment 1; ADR-008 Amendment 2; ADR-009 Amendment 1; `docs/PROJECT_SPEC.md` §2, §7–§13 (approved by the product owner 2026-11-08)
Notes for next stage:
- Company name is localised; new companies start English-only (`en` / `{en}`); first admin language validated against the company's enabled languages. Depends on STORY-051. Stage plan unchanged (stage 2 N/A: STORY-051 adds the columns).
Open questions: none.
