import { EnvironmentInjector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { FakeLockManager, makeAuthResponse, makeUser } from '../../../testing/auth-fakes';
import { AuthApi } from '../api/auth.api';
import { UserRole } from '../api/models/auth.models';
import { NotificationService } from '../notifications/notification.service';
import { authGuard, guestGuard, roleGuard } from './auth.guards';
import { AUTH_CHANNEL, CLOCK, LOCK_MANAGER } from './session-environment';
import { SessionStore } from './session-store';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

describe('guards', () => {
  let store: SessionStore;
  let now: number;
  const api = { refresh: vi.fn(), login: vi.fn(), logout: vi.fn(), me: vi.fn() };
  const notifications = { error: vi.fn() };
  const route = {} as ActivatedRouteSnapshot;
  const state = {} as RouterStateSnapshot;
  const router = { parseUrl: (url: string) => ({ url }) as unknown as UrlTree, navigateByUrl: vi.fn() };

  const run = <T>(fn: () => T): T => runInInjectionContext(TestBed.inject(EnvironmentInjector), fn);
  const urlOf = (result: unknown) => (result as { url: string }).url;

  beforeEach(() => {
    now = 1_000_000;
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthApi, useValue: api },
        { provide: Router, useValue: router },
        { provide: NotificationService, useValue: notifications },
        { provide: LOCK_MANAGER, useValue: new FakeLockManager() },
        { provide: AUTH_CHANNEL, useValue: null },
        { provide: CLOCK, useValue: () => now },
      ],
    });
    store = TestBed.inject(SessionStore);
  });

  const signIn = (role: UserRole = 'CompanyAdmin') =>
    store.set({ accessToken: 't', validUntil: now + 600_000, user: makeUser(role) });

  describe('authGuard', () => {
    it('sends a signed-out visitor to /login', async () => {
      expect(urlOf(await run(() => authGuard(route, state)))).toBe('/login');
    });

    it('lets a signed-in user through without a request', async () => {
      signIn();
      expect(await run(() => authGuard(route, state))).toBe(true);
      expect(api.refresh).not.toHaveBeenCalled();
    });

    it('renews a token that expires within 30 s on navigation', async () => {
      signIn();
      now += 580_000;
      api.refresh.mockReturnValue(of(makeAuthResponse('renewed')));
      expect(await run(() => authGuard(route, state))).toBe(true);
      expect(api.refresh).toHaveBeenCalledTimes(1);
      expect(store.accessToken()).toBe('renewed');
    });

    it('goes to /login when the refresh is rejected', async () => {
      signIn();
      now += 700_000;
      api.refresh.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status: 401, error: { code: 'auth.refresh_invalid' } })),
      );
      expect(urlOf(await run(() => authGuard(route, state)))).toBe('/login');
      expect(store.isAuthenticated()).toBe(false);
    });

    it('stays on the page, keeps the session and shows errors.network on a network failure', async () => {
      signIn();
      now += 700_000;
      api.refresh.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
      expect(await run(() => authGuard(route, state))).toBe(false);
      expect(store.isAuthenticated()).toBe(true);
      expect(notifications.error).toHaveBeenCalledWith(expect.objectContaining({ messageKey: 'errors.network' }));
    });
  });

  describe('guestGuard', () => {
    it('lets a signed-out visitor see the login page', () => {
      expect(run(() => guestGuard(route, state))).toBe(true);
    });
    it('redirects a signed-in user to /home', () => {
      signIn();
      expect(urlOf(run(() => guestGuard(route, state)))).toBe('/home');
    });
  });

  describe('roleGuard', () => {
    it('allows a listed role', () => {
      signIn('BranchManager');
      expect(run(() => roleGuard(['BranchManager', 'CompanyAdmin'])(route, state))).toBe(true);
    });
    it('redirects any other signed-in role to /home', () => {
      signIn('CounterStaff');
      expect(urlOf(run(() => roleGuard(['BranchManager'])(route, state)))).toBe('/home');
    });
    it('redirects a signed-out visitor to /login', () => {
      expect(urlOf(run(() => roleGuard(['BranchManager'])(route, state)))).toBe('/login');
    });
  });
});
