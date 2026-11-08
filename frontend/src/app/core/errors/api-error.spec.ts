import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { mapHttpError, retryAfterMinutes } from './api-error';

const fail = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new HttpErrorResponse({ status, error: body, headers: new HttpHeaders(headers) });

describe('mapHttpError', () => {
  it('maps auth.invalid_credentials and auth.locked_out to their own keys', () => {
    expect(mapHttpError(fail(401, { code: 'auth.invalid_credentials' })).messageKey).toBe('errors.auth.invalid_credentials');
    expect(mapHttpError(fail(401, { code: 'auth.locked_out' })).messageKey).toBe('errors.auth.locked_out');
  });

  it('429 shows the minutes from Retry-After, rounded up', () => {
    const mapped = mapHttpError(fail(429, { code: 'request.rate_limited' }, { 'Retry-After': '61' }));
    expect(mapped.messageKey).toBe('errors.request.rate_limited');
    expect(mapped.params).toEqual({ minutes: 2 });
  });

  it('429 without Retry-After uses the generic text', () => {
    expect(mapHttpError(fail(429, { code: 'request.rate_limited' })).messageKey).toBe('errors.request.rate_limited_generic');
  });

  it('status 0 is errors.network', () => {
    expect(mapHttpError(new HttpErrorResponse({ status: 0 })).messageKey).toBe('errors.network');
  });

  it('500 keeps the correlationId', () => {
    const mapped = mapHttpError(fail(500, { code: 'server.unexpected_error', correlationId: 'abc' }));
    expect(mapped.messageKey).toBe('errors.server.unexpected_error');
    expect(mapped.correlationId).toBe('abc');
  });

  it('keeps no correlationId for other errors', () => {
    expect(mapHttpError(fail(401, { code: 'auth.locked_out', correlationId: 'abc' })).correlationId).toBeNull();
  });

  it('validation.failed exposes field errors', () => {
    const mapped = mapHttpError(fail(400, { code: 'validation.failed', errors: { userName: ['x'] } }));
    expect(mapped.fieldErrors).toEqual({ userName: ['x'] });
  });

  it('an unknown code or a non-JSON body uses the generic fallback, never the raw code', () => {
    expect(mapHttpError(fail(418, { code: 'weird.thing' })).messageKey).toBe('errors.unknown');
    expect(mapHttpError(fail(502, '<html>')).messageKey).toBe('errors.unknown');
  });

  it('retryAfterMinutes parses seconds', () => {
    expect(retryAfterMinutes('60')).toBe(1);
    expect(retryAfterMinutes('1')).toBe(1);
    expect(retryAfterMinutes('abc')).toBeNull();
    expect(retryAfterMinutes('')).toBeNull();
    expect(retryAfterMinutes(null)).toBeNull();
  });
});
