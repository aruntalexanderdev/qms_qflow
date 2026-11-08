import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { mapHttpError, retryAfterMinutes } from './api-error';

const fail = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new HttpErrorResponse({ status, error: body, headers: new HttpHeaders(headers) });

describe('mapHttpError', () => {
  it('maps auth.invalid_credentials and auth.locked_out to their own keys', () => {
    expect(mapHttpError(fail(401, { code: 'auth.invalid_credentials' })).messageKey).toBe('errors.auth.invalid_credentials');
    expect(mapHttpError(fail(401, { code: 'auth.locked_out' })).messageKey).toBe('errors.auth.locked_out');
  });

  it('429 shows the minutes from Retry-After, rounded up, with singular, dual and plural texts', () => {
    const key = (seconds: string) =>
      mapHttpError(fail(429, { code: 'request.rate_limited' }, { 'Retry-After': seconds }));
    expect(key('30').messageKey).toBe('errors.request.rate_limited_one');
    expect(key('60').messageKey).toBe('errors.request.rate_limited_one');
    expect(key('61').messageKey).toBe('errors.request.rate_limited_two');
    expect(key('61').params).toEqual({ minutes: 2 });
    expect(key('181').messageKey).toBe('errors.request.rate_limited');
    expect(key('181').params).toEqual({ minutes: 4 });
  });

  it('429 without Retry-After uses the generic text', () => {
    expect(mapHttpError(fail(429, { code: 'request.rate_limited' })).messageKey).toBe('errors.request.rate_limited_generic');
  });

  it.each([
    ['502 with a plain-text body', 502, 'Bad Gateway'],
    ['503 with an HTML body', 503, '<html><body>503</body></html>'],
    ['504 with an empty body', 504, null],
    ['502 with a JSON body that has no code', 502, { message: 'x' }],
  ])('%s (proxy, API down) is errors.network', (_name, status, body) => {
    expect(mapHttpError(fail(status, body)).messageKey).toBe('errors.network');
    expect(mapHttpError(fail(status, body)).code).toBe('network');
  });

  it('a 502 that carries a problem-details code keeps that code', () => {
    const mapped = mapHttpError(fail(502, { code: 'server.unexpected_error', correlationId: 'c1' }));
    expect(mapped.messageKey).toBe('errors.server.unexpected_error');
    expect(mapped.correlationId).toBe('c1');
  });

  it('a 500 with problem details keeps its code and correlation id; a 500 without a code is the generic text', () => {
    expect(mapHttpError(fail(500, { code: 'server.unexpected_error', correlationId: 'c2' })).correlationId).toBe('c2');
    expect(mapHttpError(fail(500, 'boom')).messageKey).toBe('errors.unknown');
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
    expect(mapHttpError(fail(418, '<html>')).messageKey).toBe('errors.unknown');
  });

  it('retryAfterMinutes parses seconds', () => {
    expect(retryAfterMinutes('60')).toBe(1);
    expect(retryAfterMinutes('1')).toBe(1);
    expect(retryAfterMinutes('abc')).toBeNull();
    expect(retryAfterMinutes('')).toBeNull();
    expect(retryAfterMinutes(null)).toBeNull();
  });
});
