# TASK-006 — STORY-001 review follow-ups S1–S3

| Field | Value |
|---|---|
| ID | TASK-006 |
| Story | STORY-001 |
| Stage | 3 Backend |
| Agent | backend-developer |
| Status | Done |
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
- [x] Code follows common project rules
- [x] Tests added or updated and passing (list them below)
- [x] `dotnet build backend/Qms.slnx -c Release` has 0 warnings
- [x] Handoff written in the story's Stage log

## 6. Tests
- `ProblemDetailsTests.Invalid_request_returns_400_with_field_errors_and_code`: exact keys `name`, `quantity` (FluentValidation path).
- `ProblemDetailsTests.Invalid_nested_request_returns_camel_case_indexed_keys`: exact key `parts[1].name`.
- `ProblemDetailsTests.Model_binding_error_returns_camel_case_keys_without_json_path_prefix`: `[ApiController]` path, exact keys `command`, `quantity` (no `$.`).
- Suite: 37 passed, 0 failed, 0 skipped (run twice). No `logs/` folder under `Qms.Tests/bin` after the run.

## 7. Notes / result
- S1: compose publishes the API on `127.0.0.1`; `.env.example` comment and `backend/README.md` explain Development vs Production and how to expose the port for device testing. `docker compose config` shows `host_ip: 127.0.0.1` for api.
- S2: new `Qms.Api/Common/ErrorKeyNaming.Normalize` (camelCase per segment, strips `$.`, keeps `[n]`). Used by `ResultExtensions.ToProblem` (FluentValidation path) and by `InvalidModelStateResponseFactory` in `Program.cs` (built-in path). Application layer is unchanged. Built-in binding errors also carry MVC's extra `command` ("field is required") entry when a body cannot be bound; this is standard MVC behaviour.
- S3: `QmsApiFactory` overrides Serilog sink 1 (File) to a Console sink restricted to Fatal, so no file is written; console and the in-memory sink still work.
