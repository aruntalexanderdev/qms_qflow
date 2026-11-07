# ADR-005 — Background jobs with hosted services

Status: Accepted
Date: 2026-11-07
Story: — (Phase 0 decision by product owner)

## Context
Spec section 2 allowed "Hosted services (Hangfire optional)". The product owner chose hosted services for phase 1.

## Decision
1. Each job is a `BackgroundService` in `Qms.Infrastructure/Jobs/` using `PeriodicTimer` and the injected `TimeProvider` (testable with `FakeTimeProvider`).
2. The job logic lives in `Qms.Application` services; the hosted service only schedules and calls them.
3. Phase 1 jobs:

   | Job | Interval | Work |
   | --- | --- | --- |
   | `DailyResetJob` | 1 min | At each branch's opening time (branch timezone), close the previous business date: open tokens → NoShow/Cancelled as configured, counters → Closed. Sequences need no reset (they are keyed by business date). |
   | `NoShowTimeoutJob` | 30 s | Apply the recall / no-show timeouts from branch settings. |
   | `DeviceOfflineJob` | 30 s | Mark devices offline after 3 missed heartbeats; raise `DeviceStatusChanged`. |
   | `DataRetentionJob` | daily | Anonymise phone numbers after the configured number of days (spec section 11). |

4. **Several API instances:** each job run takes a PostgreSQL advisory lock (`pg_try_advisory_lock(<job id>)`), so only one instance runs it. Jobs are idempotent.
5. Phase 2 message sending uses the same pattern, with a `message_log` outbox and retry with backoff.

## Consequences
- No extra dependency, no dashboard; runs are logged with Serilog and visible in Application Insights.
- If retries, dashboards or many job types are needed later, Hangfire (PostgreSQL storage) can replace the scheduler without changing the application services.

## Spec impact
Section 2 "Background jobs" row updated.
