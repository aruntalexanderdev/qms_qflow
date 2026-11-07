# TASK-006 — STORY-001 review follow-ups S1–S3

| Field | Value |
|---|---|
| ID | TASK-006 |
| Story | STORY-001 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | To do |
| Estimate | S |
| Created | 2026-11-07 |

## 1. Goal
Fix the three "Should fix" items from the STORY-001 code review (Stage log, stage 6) before STORY-002 builds on the foundation. Requested by the product owner on 2026-11-07.

## 2. Acceptance criteria covered
- STORY-001 AC-4 (validation error keys), AC-7 (compose defaults); no AC may regress.

## 3. Technical details
- **S1, safe local defaults:** `docker-compose.yml` publishes the API port on `127.0.0.1` only (e.g. `"127.0.0.1:${API_PORT:-8080}:8080"`), like postgres. `.env.example` keeps the Development default for a local stack, with a comment that Swagger is Development-only and a deployed environment must use Production. Document this in `backend/README.md`.
- **S2, camelCase error keys:** the `errors` dictionary keys in validation problem details use the JSON property naming of the API (camelCase), e.g. `name`, `quantity`, and nested paths like `items[0].name`. Apply the same convention to the built-in model-binding / `[ApiController]` invalid-model responses (keys without the `$.` prefix, camelCase), so clients see one style. Update the tests to assert the exact keys.
- **S3, test logging:** turn off the Serilog file sink in the test host (`QmsApiFactory`), so tests no longer write interleaved lines to a shared `logs/` file. Console and in-memory capture used by the correlation id tests keep working.

## 4. Dependencies
- Needs: STORY-001 done.
- Provides to: STORY-002+ (error key convention).

## 5. Done checklist
- [ ] Code follows common project rules
- [ ] Tests added or updated and passing (list them below)
- [ ] `dotnet build backend/Qms.slnx -c Release` has 0 warnings
- [ ] Handoff written in the story's Stage log

## 6. Tests
- …

## 7. Notes / result
