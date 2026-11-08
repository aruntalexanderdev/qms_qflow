# TASK-016 — Web shell: permission-based navigation, guards, role label

| Field | Value |
|---|---|
| ID | TASK-016 |
| Story | STORY-048 |
| Stage | 4 Frontend |
| Agent | frontend-developer |
| Status | To do |
| Estimate | M |
| Created | 2026-11-08 |

## 1. Goal
Adapt the Angular shell to the new `user` contract: menus and route guards by permission, role names in the top bar, `/auth/me` re-read after a 403 and after a language switch. No new screen (`/admin/roles` is STORY-050).

## 2. Acceptance criteria covered
- AC-33 to AC-39

## 3. Technical details
- `core/api/models/auth.models.ts`: `CurrentUser` drops `role`; add `isSuperAdmin`, `roles: { id; name }[]`, `permissions: string[]`, `branchScope: 'Company' | 'Assigned' | null`; remove `UserRole`.
- `core/navigation/navigation.ts`: `NavItem` declares `permission?: string` or `superAdmin?: true` instead of `roles`; mapping: Home (none), Companies (`superAdmin`), Service types `service-types.view`, Priority types `priority-types.view`, Service mapping `service-mapping.view`, Branches `branches.view`, Users `users.view`, Templates `ticket-templates.view`; not-live entries keep their planned codes (`dashboard.view`, `counter-console.operate`, `reports.view`) and stay hidden. `navigationFor(user)` filters by `live` and permission, drops empty sections.
- `core/auth/auth.guards.ts`: replace `roleGuard` with `permissionGuard(code)` and `superAdminGuard`; update `app.routes.ts`. Unauthorised signed-in users go to `/home`; no session to `/login`.
- Top bar (`layout/topbar`): role label = `roles[].name` joined by ", " as returned, or the translation key for Super Admin; nothing when no role. Use design tokens only; logical CSS properties.
- 403 handling in the HTTP error interceptor / session coordinator: on `auth.forbidden`, call `GET /auth/me` once (single in-flight request, no loop), update `SessionStore`, still show the 403 message.
- Language switch: after the language is applied (`language-switch.component`, ADR-007 Amendment 1), call `GET /auth/me` so role names follow `Accept-Language`.
- `en.json`: error codes `role.not_found`, `role.name_taken`, `role.system`, `role.in_use`, `role.permission_unknown`, `role.view_required`, `role.cannot_change_self`, `role.last_admin`; `permissions.modules.<module>` for the 19 modules and `permissions.actions.<action>` for each action (view, create, edit, delete, reset-password, registration-code); Super Admin label. A test compares the keys with the catalog list (the 19 modules and their actions, ADR-009 §3 plus `company-languages`).
- Update test fakes (`src/testing/auth-fakes.ts`) and specs: `navigation.spec.ts`, `auth.guards.spec.ts`, `shell.component.spec.ts`, `session-coordinator.spec.ts`, `translations.spec.ts`, top bar, language switch.

## 4. Dependencies
- Needs: TASK-015 (new `user` contract in login, refresh and `/auth/me`).
- Provides to: STORY-050 (roles screen), every later screen story (permission guards and nav entries).

## 5. Done checklist
- [ ] Code follows common project rules
- [ ] Tests added and passing (list them below)
- [ ] User-facing text localised (translation keys / translated fields; English required, ADR-010)
- [ ] No business logic outside `Qms.Application`
- [ ] Spec / ADR updated if behaviour changed
- [ ] Handoff written in the story's Stage log

## 6. Tests
- Component/unit: navigation per permission set (CA-C1, BM-B1, CS, SA, `users.view` only); guards (allow, redirect to `/home`, `/login`); top bar label (one role, two roles, Super Admin, none); 403 triggers one `/auth/me` re-read (concurrent 403s, no loop); language switch triggers a re-read with `Accept-Language`; translation keys cover the catalog.
- Commands: `cd frontend && npm ci && npm run build && npm test -- --watch=false`; RTL check of the top bar when a right-to-left language is enabled.

## 7. Notes / result
<Filled by the agent when done.>
