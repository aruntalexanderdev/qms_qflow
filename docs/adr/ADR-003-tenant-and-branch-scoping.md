# ADR-003 — Tenant (company) and branch scoping

Status: Proposed
Date: 2026-11-07
Story: STORY-002, STORY-003

## Context
The spec says all data is filtered by company id and every endpoint checks role and branch scope. It does not say how.

## Decision
1. **Current context:** `ICurrentUser` (from JWT claims: `sub`, `company_id`, roles) and `ICurrentDevice` (from `X-Device-Key` → `device` → branch → company) are scoped services in `Qms.Application`, implemented in `Qms.Api`.
2. **Company filter:** every tenant entity implements `ITenantEntity { CompanyId }`. `QmsDbContext` applies a global query filter `CompanyId == currentCompanyId` and sets `CompanyId` on insert. A save that writes another company's row throws.
3. **Super Admin:** has no `company_id` claim. Only handlers in the `Companies` feature, marked `[CrossTenant]`, call `IgnoreQueryFilters()`.
4. **Branch scope:** Branch Manager / Supervisor and Counter Staff carry their branch ids (`user_branch`) and counter ids (`user_counter`), loaded per request and cached for the request. Handlers that take a `branchId`, or reach one through a zone, counter or token, call `IBranchScope.EnsureAccess(branchId)`, which returns `Forbidden`. Company Admin passes for every branch in the company.
5. **Devices:** device endpoints act only on their own branch and their mapped zones and services.
6. **Tests:** an integration test per protected endpoint proves a user from another company or branch gets 403/404.

## Consequences
- Forgetting a filter is hard: it is on by default and the escape hatch is explicit and greppable (`[CrossTenant]`).
- Branch checks are explicit in handlers, so the code reviewer can see them (code-review skill, item 3).

## Spec impact
None (implements sections 4, 8 and 11).
