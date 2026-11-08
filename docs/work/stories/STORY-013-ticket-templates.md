# STORY-013 — Ticket templates (incl. fee line)

| Field | Value |
|---|---|
| ID | STORY-013 |
| Status | Analysis |
| Priority | Must |
| Milestone | M2 |
| Spec sections | 5.2 (print ticket with fee line), 6 (Paid priority: "the ticket prints the fee line"), 7 (settings per dispenser: ticket template), 8 (Company Admin: templates; `/admin/templates`), 9 (`ticket_template`, `dispenser_setting.ticket_template_id`), 10 (`/ticket-templates`), 11 (Languages, no external CDN, Audit), 12 (M2) |
| Requested by | Alexa (product owner) |
| Created | 2026-11-08 |

## 1. User story
As a **Company Admin**, I want **to maintain bilingual ticket templates (header, footer, logo, which optional lines to show) and preview a ticket with the VIP fee line**, so that **every dispenser prints a correct, branded ticket**.

## 2. Background
Spec §7 lists the ticket content: logo, header and footer (Arabic + English), large token number, service, zone, people ahead, estimated wait, date and time, and the fee line for a paid priority ("VIP service fee: AED 50.00 – payable at the counter"). §9 stores only header/footer (en + ar), logo URL, `show_wait`, `show_people_ahead`; the token number, service, zone, date/time and the fee line are always printed (§6: "the ticket prints the fee line"), so the fee line has no flag — "incl. fee line" means the preview (and later the print data, M3 STORY-014) builds it from the priority type's fee (branch override, else company fee) and the company currency. One template is seeded (`SeedIds.TicketTemplate1`, used by both dispensers). Depends on STORY-005 (plumbing, priority types for the fee); independent of the branch stories.

## 3. Scope
To be completed by the BA.

## 4. Business rules
To be completed by the BA.

## 5. Acceptance criteria
To be completed by the BA.

## 6. UI / UX
To be completed by the BA. (Templates screen with preview is M4 STORY-026.)

## 7. Data and API impact
**Spec:** sections 5.2, 6, 7, 8, 9, 10, 11. No business rule changes.

**Database**
- Tables: `ticket_template` (company_id, header_en, header_ar, footer_en, footer_ar (1–500), logo_url, show_wait, show_people_ahead); read `dispenser_setting` (in-use check, FK `fk_dispenser_setting_ticket_templates_company_id_ticket_templa`), `priority_type`, `branch_priority_fee`, `company` (currency) for the preview; `audit_log`.
- **Schema change: none by default.** Only if the product owner approves open question 1 (a `name` column), stage 2 adds it with a migration.

**Backend**
| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/v1/ticket-templates` | CA, BM | BM reads (needed to pick a template in dispenser settings, STORY-012); sorted by `headerEn` (no name column) |
| GET | `/api/v1/ticket-templates/{id}` | CA, BM | 404 `ticket_template.not_found` |
| POST | `/api/v1/ticket-templates` | CA | `{ headerEn, headerAr, footerEn, footerAr, logoUrl?, showWait, showPeopleAhead }` → 201 |
| PUT | `/api/v1/ticket-templates/{id}` | CA | + `version` |
| DELETE | `/api/v1/ticket-templates/{id}` | CA | 409 `ticket_template.in_use` while a dispenser uses it |
| GET | `/api/v1/ticket-templates/{id}/preview?language=en\|ar&branchId=` | CA, BM (own branch if given) | ADR-008 §2 addition: returns the ordered ticket lines for a sample token `VB0001` (VIP + Service 2 of the seed, or the first paid priority), service and zone names, date/time in branch time, optional wait / people-ahead lines per flags, and the **fee line** with the fee (branch override when `branchId` is given, else company fee) and currency; no fee line when the company has no paid priority |
- The ticket lines come from one Application builder (e.g. `TicketContentBuilder`) so M3 `POST /tokens` print data (STORY-014), M5 staff issue and M7 printing use the same text and order. Fixed line texts (fee line, labels) are bilingual resources in Application, not template columns.
- `logoUrl`: optional; must be a relative path or an https URL on the deployment's own origin (spec §11: no external CDN calls from dispensers) → else 400 `ticket_template.logo_url_invalid`. No upload endpoint in M2 (open question 2).
- Audit on every write (`ticket_template`).
- `IDeviceConfigNotifier.ConfigChangedAsync` for every dispenser using the template after an update (no-op in M2).
- SignalR: none.

**Web / Android / displays:** none in this story.

**Cross-cutting:** company scope (CA-C2 → 404 on Company 1 templates; a Company 2 template id cannot be set on a Company 1 dispenser — composite FK, STORY-012); bilingual texts required; preview test: seeded data gives the line "VIP service fee: AED 50.00 – payable at the counter" (en) and the Arabic line.

## 8. Stage plan (filled by architect, stage 0)
| Stage | Applies? | Task | Notes |
|---|---|---|---|
| 0 Architect | Yes | — | ADR-008 |
| 1 BA | Yes | — | |
| 2 Database | N/A | — | Table exists; becomes Yes only if the product owner approves a `name` column (open question 1) |
| 3 Backend | Yes | BA creates | Template CRUD, preview with fee line, ticket content builder, tests |
| 4 Frontend | N/A | — | Templates screen is M4 (STORY-026) |
| 5 Android | N/A | — | Printing is M7 (STORY-036) |
| 6 Code review | Yes | — | |
| 7 QA | Yes | — | |

## 9. Non-functional notes
To be completed by the BA.

## 10. Open questions
- [ ] **Product owner — template name.** `ticket_template` (spec §9) has no name, so the admin list and the dispenser-settings picker can only show the English header. Proposal: add `name` (admin label, single column like `counter.name`) — a §9 change needing approval. Default without approval: list by `headerEn`. Not blocking.
- [ ] **Product owner — logo storage.** Spec §7/§9 have `logo_url` but no upload or storage, and dispensers may not call external services (§11). Proposal for M2: URL field only, restricted to the deployment's own origin; a logo upload (stored in the database or Azure Blob behind the API) becomes a follow-up story before M7 printing. Not blocking.
- [ ] **Product owner — Arabic fee line.** The spec gives only the English text "VIP service fee: <amount> – payable at the counter". Proposed Arabic: "رسوم خدمة <priority name>: <amount> – تُدفع عند الكاونتر" (priority name from `priority_type.name_ar`, amount with the currency code). Please confirm the wording; the same text is used on the dispenser notice (M7). Not blocking.
- [ ] Note for the BA: the preview endpoint is in scope as the M2 meaning of "incl. fee line"; if the product owner prefers, it can move to M4 with the screen, but the builder should still be written once here.

## 11. Stage log

### Stage 0 — Architecture review — project-architect — 2026-11-08
Result: Done
Changed: this story (new: header, sections 1, 2, 7, 8, 10)
Notes for next stage:
- Depends only on STORY-005; can run in parallel with STORY-006 to STORY-010. STORY-012 references templates.
- The fee line is not a template column: it is always printed for a paid priority (spec §6). The builder takes the fee as issue rule 6 defines it (branch override, else company fee).
Open questions: template name, logo storage, Arabic fee line wording; none blocks analysis.
