import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from '../api/models/problem-details';

/** Codes that have a translation under `errors.<code>` in en.json and ar.json. Anything else shows `errors.unknown`. */
export const KNOWN_ERROR_CODES = [
  'auth.invalid_credentials',
  'auth.locked_out',
  'auth.refresh_reused',
  'auth.unauthorized',
  'auth.forbidden',
  'request.rate_limited',
  'request.invalid',
  'request.conflict',
  'resource.not_found',
  'validation.failed',
  'server.unexpected_error',
] as const;

/** An API or network error ready to show: a translation key plus parameters, never a raw code. */
export interface ApiError {
  /** The problem-details `code`, or `network` for status 0, or `unknown`. */
  code: string;
  status: number;
  /** Key under `errors.` to translate, with `params`. */
  messageKey: string;
  params: Record<string, string | number>;
  /** Shown with `server.unexpected_error` so support can find the log entry. */
  correlationId: string | null;
  /** Field name to messages, from `validation.failed`. */
  fieldErrors: Record<string, string[]>;
}

/** Whole minutes from a Retry-After header in seconds, rounded up; null when missing or not a number. */
export function retryAfterMinutes(headerValue: string | null): number | null {
  if (headerValue === null) {
    return null;
  }
  const seconds = Number(headerValue.trim());
  if (!Number.isFinite(seconds) || seconds < 0 || headerValue.trim() === '') {
    return null;
  }
  return Math.max(1, Math.ceil(seconds / 60));
}

export function mapHttpError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return build('unknown', 0);
  }
  if (error.status === 0) {
    return build('network', 0);
  }
  const problem = isProblemDetails(error.error) ? error.error : null;
  const code = problem?.code ?? 'unknown';
  const result = build(code, error.status);
  result.correlationId = problem?.correlationId ?? null;
  result.fieldErrors = problem?.errors ?? {};
  if (code === 'request.rate_limited') {
    const minutes = retryAfterMinutes(error.headers.get('Retry-After'));
    if (minutes === null) {
      result.messageKey = 'errors.request.rate_limited_generic';
    } else {
      result.params = { minutes };
    }
  }
  return result;
}

function build(code: string, status: number): ApiError {
  const known = (KNOWN_ERROR_CODES as readonly string[]).includes(code);
  return {
    code,
    status,
    messageKey: code === 'network' ? 'errors.network' : known ? `errors.${code}` : 'errors.unknown',
    params: {},
    correlationId: null,
    fieldErrors: {},
  };
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
