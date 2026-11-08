import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { FakeLockManager, makeAuthResponse, makeUser } from '../../../testing/auth-fakes';
import { authInterceptor, isAuthEndpoint } from './auth.interceptor';
import { AUTH_CHANNEL, CLOCK, LOCK_MANAGER } from './session-environment';
import { SessionStore } from './session-store';

const tick = () => new Promise((resolve) => setTimeout(resolve, 10));

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let store: SessionStore;
  let now: number;
  const router = { navigateByUrl: vi.fn().mockResolvedValue(true) };

  beforeEach(() => {
    now = 1_000_000;
    router.navigateByUrl.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: LOCK_MANAGER, useValue: new FakeLockManager() },
        { provide: AUTH_CHANNEL, useValue: null },
        { provide: CLOCK, useValue: () => now },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    store = TestBed.inject(SessionStore);
    store.set({ accessToken: 'old', validUntil: now + 600_000, user: makeUser() });
  });

  afterEach(() => backend.verify());

  it('adds the bearer token to API calls', async () => {
    http.get('/api/v1/things').subscribe();
    await tick();
    const request = backend.expectOne('/api/v1/things');
    expect(request.request.headers.get('Authorization')).toBe('Bearer old');
    request.flush({});
  });

  it('401 -> exactly one refresh -> the original request is retried once with the new token', async () => {
    const results: unknown[] = [];
    http.get('/api/v1/things').subscribe((value) => results.push(value));
    await tick();
    backend.expectOne('/api/v1/things').flush({ code: 'auth.unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await tick();
    const refresh = backend.expectOne('/api/v1/auth/refresh');
    expect(refresh.request.method).toBe('POST');
    expect(refresh.request.body).toBeNull();
    expect(refresh.request.headers.has('Authorization')).toBe(false);
    refresh.flush(makeAuthResponse('new'));
    await tick();
    const retry = backend.expectOne('/api/v1/things');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush({ ok: true });
    expect(results).toEqual([{ ok: true }]);
  });

  it('retries only once: a second 401 reaches the caller', async () => {
    let status = 0;
    http.get('/api/v1/things').subscribe({ error: (e) => (status = e.status) });
    await tick();
    backend.expectOne('/api/v1/things').flush({}, { status: 401, statusText: 'x' });
    await tick();
    backend.expectOne('/api/v1/auth/refresh').flush(makeAuthResponse('new'));
    await tick();
    backend.expectOne('/api/v1/things').flush({}, { status: 401, statusText: 'x' });
    expect(status).toBe(401);
  });

  it('refreshes before the call when the token expires within 30 s', async () => {
    now += 580_000;
    http.get('/api/v1/things').subscribe();
    await tick();
    backend.expectOne('/api/v1/auth/refresh').flush(makeAuthResponse('fresh'));
    await tick();
    const call = backend.expectOne('/api/v1/things');
    expect(call.request.headers.get('Authorization')).toBe('Bearer fresh');
    call.flush({});
  });

  it('keeps the session and fails the call when the refresh hits a network error on an expired token', async () => {
    now += 700_000;
    let status = -1;
    http.get('/api/v1/things').subscribe({ error: (e) => (status = e.status) });
    await tick();
    backend.expectOne('/api/v1/auth/refresh').error(new ProgressEvent('error'), { status: 0 });
    await tick();
    expect(status).toBe(0);
    expect(store.isAuthenticated()).toBe(true);
  });

  it.each(['/api/v1/auth/login', '/api/v1/auth/refresh', '/api/v1/auth/logout'])(
    'never adds a bearer or refreshes for %s, not even after its own 401',
    async (url) => {
      let status = 0;
      http.post(url, null).subscribe({ error: (e) => (status = e.status) });
      await tick();
      const request = backend.expectOne(url);
      expect(request.request.headers.has('Authorization')).toBe(false);
      request.flush({ code: 'auth.invalid_credentials' }, { status: 401, statusText: 'x' });
      await tick();
      expect(status).toBe(401);
      backend.expectNone('/api/v1/auth/refresh');
    },
  );

  it('leaves non-API requests alone', () => {
    http.get('/i18n/en.json').subscribe();
    const request = backend.expectOne('/i18n/en.json');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('recognises the excluded endpoints', () => {
    expect(isAuthEndpoint('/api/v1/auth/login')).toBe(true);
    expect(isAuthEndpoint('/api/v1/auth/me')).toBe(false);
  });
});
