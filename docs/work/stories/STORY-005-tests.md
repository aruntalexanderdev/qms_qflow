# STORY-005 — Test cases and results (QA, 2026-11-08)

Environment: throwaway `postgres:18` container, API `dotnet run -c Release --project backend/Qms.Api` in Development (`http://127.0.0.1:8081`), seeded `admin1` (Company Admin, Company 1) and `super1` (Super Admin). Extra users created by SQL, reusing the seeded password hash: `bm1` (Branch Manager, Branch 1 via `user_branch`), `cs1` (Counter Staff), `ca2` (Company Admin of a hand-made Company 2 "C2", currency USD, with service type "Service X" prefix A and priority type N). References for in-use tests inserted by SQL (company mapping is seed; dispenser mapping, service flow, token, branch priority fee). Containers and processes were removed afterwards.

Automated: `dotnet test backend/Qms.slnx -c Release` = **392 passed, 0 failed, 0 skipped** (2 m 43 s). Build 0 warnings per code review.

"Live" = curl against the running stack, "Test" = verified by the named automated tests only (shared plumbing needs test-host endpoints or fault injection that the real API does not expose).

| TC | AC | Title | Steps (short) | Expected | Result |
|---|---|---|---|---|---|
| TC-005-1 | AC-1 | List service types | `admin1` GET `/api/v1/service-types`; `ca2` GET | 6 items A,B,C,D,F,G sorted; fields `id,name,translations,prefix,targetMinutes,sortOrder,isActive,version`; `ca2` sees only its rows | Pass (live) |
| TC-005-2 | AC-2 | Active filter | Deactivate G; GET `?isActive=false`, `=true`, none | `[G]`; `[A,B,C,D,F]`; all six | Pass (live) |
| TC-005-3 | AC-3 | Get one | GET Service 2 | `prefix` B, `name` "Service 2", `translations.ar` "الخدمة 2" | Pass (live) |
| TC-005-4 | AC-4 | Create | POST Cards/H with `translations.ar`, stray `companyId` of Company 2; no `translations`; `{}`; `{"ar":""}`; prefix " M "; `targetMinutes` null | 201 + `Location`; row in Company 1 (SQL); DTO `translations` `{}` when empty; " M " stored as `M` | Pass (live) |
| TC-005-5 | AC-5 | Service type validation (a)-(m) + duplicate-case key | 15 bodies | 400 on `name`, `prefix`, `targetMinutes`, `sortOrder`, `translations.ar`; (k) `translation.language_not_enabled` key `translations.fr`; (l) `translation.default_language_key` key `translations.en`; `{"ar":"x","AR":"y"}` 400 on `translations.ar`; no row (SQL count 0) | Pass (live) |
| TC-005-6 | AC-6 | Prefix unique per company | CA-1 POST A; PUT Service 2 to A; `ca2` POST B (201) and A (409); two parallel POST prefix U | 409 `service_type.prefix_taken`; 201 for B in Company 2; race = one 201, one 409 | Pass (live) |
| TC-005-7 | AC-7 | Update | PUT Service 3 with all fields and V1; GET; PUT `translations` `{}`; PUT `fr`, `en`; PUT missing `isActive`/`sortOrder`/`prefix`/`name` | 200, new `version`; `{}` clears Arabic; `fr`/`en` give the AC-5 codes; missing field 400 naming the field, row unchanged (SQL) | Pass (live) |
| TC-005-8 | AC-8 | Stale / missing version | PUT with V1 after another PUT; missing, empty, unparsable `version` | 409 `request.conflict`, row keeps first change; 400 `validation.failed` on `version` (3 cases) | Pass (live) |
| TC-005-9 | AC-9 | Deactivate / reactivate | PUT `isActive` false then true on G; also on Service 1 (in company mapping) | 200 both ways, row still listed, allowed while in mapping | Pass (live) |
| TC-005-10 | AC-10 | Delete unused | DELETE Cards (H) | 204; GET 404 `service_type.not_found`; audit row `service_type`/`delete` | Pass (live) |
| TC-005-11 | AC-11 | Delete in use | Service 1 (company mapping); new types with only a dispenser mapping row, only a service flow row, only a token | 409 `service_type.in_use` x4, rows remain, audit count unchanged. FK-bypass mapping: test `Ac11_real_foreign_key_errors_without_the_pre_check_map_to_the_in_use_codes` | Pass (live + test) |
| TC-005-12 | AC-12 | Cross-company | `ca2` GET/PUT/DELETE Company 1 row, random GUID, list; PUT invalid body on foreign id | 404 `service_type.not_found` for all, identical to random id; row unchanged; list only C2. Invalid body on a foreign id returns 400 from the validator before lookup, which reveals nothing | Pass (live) |
| TC-005-13 | AC-13 | List priority types | GET; `?isActive=false`; `ca2` GET | VIP first (V, level 1, paid, 50.00, AED), Normal second (N, 2, unpaid, null); empty for inactive filter; `ca2` sees only its row (USD) | Pass (live) |
| TC-005-14 | AC-14 | Get one | GET VIP, raw body | `"feeAmount":50.00` JSON number | Pass (live) |
| TC-005-15 | AC-15 | Create unpaid | POST Senior/S level 1 with ar; POST without `translations` | 201, `feeAmount` null, `currencyCode` AED; `{}` translations | Pass (live) |
| TC-005-16 | AC-16 | Create paid | POST Premium 75.50; fee 0 paid; fee 9999999999.99 | 201 with fee shown; 0 accepted; max accepted | Pass (live) |
| TC-005-17 | AC-17 | Priority validation (a)-(l) + missing `priorityLevel`/`isPaid` | 18 bodies | 400 on `name`, `prefix`, `priorityLevel`, `feeAmount` (3 distinct messages), codes `translation.*` for (k), (l); nothing saved (SQL count 0) | Pass (live) |
| TC-005-18 | AC-18 | Prefix unique | POST V; PUT Normal to V; service type V; `ca2` V | 409 `priority_type.prefix_taken` x2; 201 for service type V (independent); 201 for Company 2 | Pass (live) |
| TC-005-19 | AC-19 | Update fee | PUT VIP fee 60.00 with V1; GET | 200, 60.00, new version, GET 60.00 | Pass (live) |
| TC-005-20 | AC-20 | Paid to unpaid | PUT unpaid with 50 (400); unpaid null (200, null); back to paid 50 (200); branch fee row present before and after | 400 on `feeAmount`; `branch_priority_fee` row kept (count 1) | Pass (live) |
| TC-005-21 | AC-21 | Stale / missing version | Stale PUT on VIP and Normal; PUT without version | 409 `request.conflict` (fee still 60.00 in SQL); 400 `version` | Pass (live) |
| TC-005-22 | AC-22 | Deactivate | PUT Normal `isActive` false; list; `?isActive=false`; reactivate | 200 false, row stays, listed under filter | Pass (live) |
| TC-005-23 | AC-23 | Delete | Senior (204 + audit row); VIP (company mapping), Premium (branch fee only), "Free paid" (token only) | 204 + `priority_type`/`delete`; 409 `priority_type.in_use` x3; no audit rows for 409 | Pass (live) |
| TC-005-24 | AC-24 | Cross-company | `ca2` GET/PUT/DELETE VIP, random id | 404 `priority_type.not_found` x4; VIP unchanged (V, paid, 50.00) | Pass (live) |
| TC-005-25 | AC-25 | Permission matrix | For both resources: `bm1`, `cs1`, `super1`, anonymous x 5 actions; `admin1` all actions throughout | `bm1`: GET 200, POST/PUT/DELETE 403 `auth.forbidden`, nothing created; `cs1`, `super1`: 403 on all ten; anonymous 401 `auth.unauthorized`. BM-B2 and the `Permissions.All` / map unit test: Test (`AccessAndAuditTests`, `ArchitectureTests`) | Pass (live + test) |
| TC-005-26 | AC-26 | Audit per write | SQL on `audit_log` after the run | 35 rows = every successful create/update/delete, one each; `company_id` = tenant of caller, `user_id` = caller, `device_id` null, `at` UTC, `entity_id`, `data` = command JSON (update has `feeAmount` 60.00 and `version`); no GETs; no `password/token/secret/hash` text in any row; stray `companyId` not stored | Pass (live) |
| TC-005-27 | AC-27 | Failed write writes nothing | 409 prefix, 409 stale, 400 validation, 409 in use, 403, 404: audit count and rows compared | Count unchanged (34 before/after the in-use batch; no row for any failed call). Rollback when the audit insert fails: Test (`AuditDecoratorTests`) | Pass (live + test) |
| TC-005-28 | AC-28 | Enums as strings | Test-host endpoint | `"Waiting"` in/out; auth contract unchanged | Pass (test: `PlumbingTests.Ac28_*`; Auth tests green) |
| TC-005-29 | AC-29 | Postgres error catalog | Test-host endpoints for 23505/23503/23001/23514, user-assignment names, bare `PostgresException`, catalog names exist in schema | Codes and statuses per AC, no SQL in body | Pass (test: `PlumbingTests.Ac29_*`; live: real 23505 race returned 409 `prefix_taken`) |
| TC-005-30 | AC-30 | Branch scope | CA/BM/other-company/unknown, query count | As AC | Pass (test: `BranchScopeTests`, `PlumbingTests.Ac30_*`) |
| TC-005-31 | AC-31 | Audit decorator order/`NotAudited` | Test commands | As AC | Pass (test: `AuditDecoratorTests`, 14 tests) |
| TC-005-32 | AC-32 | Version helpers | Unit tests; live: `version` non-empty string on all DTOs, unparsable version gives 400 on `version` | As AC | Pass (test: `SetupPlumbingUnitTests`; live part) |
| TC-005-33 | AC-33 | Device config notifier | Recording double | Called once after commit on name/prefix/`isActive`/`isPaid`/fee only | Pass (test: `AccessAndAuditTests`/`ServiceTypesApiTests`/`PriorityTypesApiTests`) |
| TC-005-34 | AC-34 | Architecture | NetArchTest | As AC | Pass (test: `ArchitectureTests`, 11 tests) |
| TC-005-35 | AC-35 | Shared test data | `SetupTestData` twice, tokens | As AC | Pass (test: `PlumbingTests.Ac35_*`) |
| TC-005-36 | AC-36 | Row version, no DDL | Tests; live: no `xmin` column in `information_schema.columns`; every DTO `version` changes after each write | As AC | Pass (test: `RowVersionTests`, `SchemaTests`; live part) |
| TC-005-37 | AC-37 | Error format | Raw headers/bodies of 400/401/403/404/409; malformed JSON; `X-Correlation-Id` supplied | `application/problem+json`, `type,title,status,detail,errors,traceId,correlationId,code`; supplied correlation id echoed; malformed JSON gives 400 `request.invalid` without stack trace or SQL | Pass (live) |

## Observations (not defects)
- `audit_log.data` stores the raw command JSON, so a prefix sent as " M " appears untrimmed in the audit row while the stored row has `M`. Matches AC-26 ("the command JSON").
- A service type without translations stores `name_ar` as an empty string (column is NOT NULL); the DTO returns `translations` `{}`. This is the documented temporary mapping until STORY-051.
- A `PUT` with an invalid body on another company's id returns 400, not 404, because validation runs first. It reveals nothing about the row.
- Login is rate limited (about 10 per minute); the QA script cached tokens.
- Not tested live: BM-B2, fault-injected audit failure, FK-bypass paths, SignalR/enum test endpoints; covered by the automated tests named above.
