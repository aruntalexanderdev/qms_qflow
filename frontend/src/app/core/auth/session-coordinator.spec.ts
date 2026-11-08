import { EnvironmentInjector, createEnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import {
  FakeAuthServer,
  FakeChannelHub,
  FakeLockManager,
  makeAuthResponse,
  makeUser,
  problem,
} from '../../../testing/auth-fakes';
import { AuthApi } from '../api/auth.api';
import { SessionCoordinator } from './session-coordinator';
import { AUTH_CHANNEL, CLOCK, LOCK_MANAGER } from './session-environment';
import { SessionStore } from './session-store';

interface Tab {
  coordinator: SessionCoordinator;
  store: SessionStore;
  router: { navigateByUrl: ReturnType<typeof vi.fn> };
}

describe('SessionCoordinator', () => {
  let server: FakeAuthServer;
  let locks: FakeLockManager;
  let hub: FakeChannelHub;
  let now: number;

  function openTab(): Tab {
    const router = { navigateByUrl: vi.fn().mockResolvedValue(true) };
    const injector = createEnvironmentInjector(
      [
        { provide: AuthApi, useValue: server.api },
        { provide: Router, useValue: router },
        { provide: LOCK_MANAGER, useValue: locks },
        { provide: AUTH_CHANNEL, useValue: hub.create() },
        { provide: CLOCK, useValue: () => now },
        SessionStore,
        SessionCoordinator,
      ],
      TestBed.inject(EnvironmentInjector),
    );
    return { coordinator: injector.get(SessionCoordinator), store: injector.get(SessionStore), router };
  }

  beforeEach(() => {
    server = new FakeAuthServer();
    locks = new FakeLockManager();
    hub = new FakeChannelHub();
    now = 1_000_000;
  });

  it('login stores the session in memory and expiry comes from expiresIn', async () => {
    const tab = openTab();
    const user = await tab.coordinator.login({ userName: 'admin', password: 'x' });
    expect(user.role).toBe('CompanyAdmin');
    expect(tab.store.accessToken()).toBe('login-1');
    expect(tab.store.snapshot()?.validUntil).toBe(now + 900_000);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('two tabs refreshing at once send exactly one request; the second adopts the broadcast session', async () => {
    const a = openTab();
    const b = openTab();
    const [first, second] = await Promise.all([a.coordinator.refresh(null), b.coordinator.refresh(null)]);
    expect([first, second].sort()).toEqual(['adopted', 'refreshed']);
    expect(server.refreshCalls).toBe(1);
    expect(a.store.accessToken()).toBe('refresh-1');
    expect(b.store.accessToken()).toBe('refresh-1');
    expect(locks.maxHeld).toBe(1);
  });

  it('a tab that heard no broadcast refreshes only after the first finished (never overlapping)', async () => {
    const a = openTab();
    const b = openTab();
    // Break the channel: b hears nothing.
    b.store.clear();
    hub.deliver = () => undefined;
    await Promise.all([a.coordinator.refresh(null), b.coordinator.refresh(null)]);
    expect(server.refreshCalls).toBe(2);
    expect(server.maxConcurrentRefreshes).toBe(1);
  });

  it('single flight inside one tab', async () => {
    const tab = openTab();
    await Promise.all([tab.coordinator.refresh(null), tab.coordinator.refresh(null), tab.coordinator.refresh(null)]);
    expect(server.refreshCalls).toBe(1);
  });

  it('uses a newer session instead of refreshing when the stale token was already replaced', async () => {
    const tab = openTab();
    await tab.coordinator.login({ userName: 'a', password: 'b' });
    now += 1000;
    // Pretend a broadcast delivered a newer token while the old one was rejected.
    tab.store.set({ accessToken: 'newer', validUntil: now + 900_000, user: makeUser() });
    const outcome = await tab.coordinator.refresh(now - 1000 + 900_000); // the older session's expiry
    expect(outcome).toBe('adopted');
    expect(server.refreshCalls).toBe(0);
  });

  it('ensureFresh does nothing while the token has more than 30 s left, refreshes within 30 s', async () => {
    const tab = openTab();
    await tab.coordinator.login({ userName: 'a', password: 'b' });
    expect(await tab.coordinator.ensureFresh()).toBe('fresh');
    now += 880_000;
    expect(await tab.coordinator.ensureFresh()).toBe('refreshed');
    expect(server.refreshCalls).toBe(1);
  });

  it('logout under the lock signs out the other tab through the broadcast', async () => {
    const a = openTab();
    const b = openTab();
    await a.coordinator.login({ userName: 'a', password: 'b' });
    await Promise.resolve();
    expect(b.store.isAuthenticated()).toBe(true);
    await a.coordinator.logout();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(server.logoutCalls).toBe(1);
    expect(a.store.isAuthenticated()).toBe(false);
    expect(b.store.isAuthenticated()).toBe(false);
    expect(a.router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(b.router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('refresh 401 clears the session, broadcasts signed-out and goes to /login', async () => {
    const a = openTab();
    const b = openTab();
    await a.coordinator.login({ userName: 'a', password: 'b' });
    server.nextRefresh = () => throwError(() => problem(401, 'auth.refresh_invalid'));
    const outcome = await a.coordinator.refresh(now + 900_000);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(outcome).toBe('signed-out');
    expect(a.store.isAuthenticated()).toBe(false);
    expect(a.store.endedReason()).toBeNull();
    expect(b.store.isAuthenticated()).toBe(false);
    expect(b.router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('auth.refresh_reused ends the session everywhere and keeps the reason for the login page', async () => {
    const a = openTab();
    const b = openTab();
    await a.coordinator.login({ userName: 'a', password: 'b' });
    server.nextRefresh = () => throwError(() => problem(401, 'auth.refresh_reused'));
    await a.coordinator.refresh(now + 900_000);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(a.store.endedReason()).toBe('auth.refresh_reused');
    expect(b.store.endedReason()).toBe('auth.refresh_reused');
  });

  it('does not navigate on a start-up 401 (navigate: false)', async () => {
    const tab = openTab();
    server.nextRefresh = () => throwError(() => problem(401, 'auth.refresh_invalid'));
    expect(await tab.coordinator.refresh(null, { navigate: false })).toBe('signed-out');
    expect(tab.router.navigateByUrl).not.toHaveBeenCalled();
  });

  it.each([
    ['network error', 0, 'x'],
    ['server error', 500, 'server.unexpected_error'],
  ])('a %s during refresh keeps the session and fails only the call', async (_name, status, code) => {
    const tab = openTab();
    await tab.coordinator.login({ userName: 'a', password: 'b' });
    server.nextRefresh = () => throwError(() => problem(status, code));
    await expect(tab.coordinator.refresh(now + 900_000)).rejects.toMatchObject({ status });
    expect(tab.store.isAuthenticated()).toBe(true);
    // The next attempt works once the server is back.
    server.nextRefresh = null;
    expect(await tab.coordinator.refresh(now + 900_000)).toBe('refreshed');
  });

  it('a session for a different user discards this tab and goes to /', async () => {
    const a = openTab();
    const b = openTab();
    await a.coordinator.login({ userName: 'a', password: 'b' });
    await Promise.resolve();
    expect(b.store.user()?.id).toBe('user-1');
    server.nextRefresh = () => of(makeAuthResponse('other', makeUser('CounterStaff', { id: 'user-2' })));
    await a.store.clear();
    // Tab b still has user-1; a (signed out locally) refreshes and gets user-2.
    await a.coordinator.refresh(null);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(b.store.isAuthenticated()).toBe(false);
    expect(b.router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('works without Web Locks (single flight only)', async () => {
    const injector = createEnvironmentInjector(
      [
        { provide: AuthApi, useValue: server.api },
        { provide: Router, useValue: { navigateByUrl: vi.fn() } },
        { provide: LOCK_MANAGER, useValue: null },
        { provide: AUTH_CHANNEL, useValue: null },
        { provide: CLOCK, useValue: () => now },
        SessionStore,
        SessionCoordinator,
      ],
      TestBed.inject(EnvironmentInjector),
    );
    const coordinator = injector.get(SessionCoordinator);
    await Promise.all([coordinator.refresh(null), coordinator.refresh(null)]);
    expect(server.refreshCalls).toBe(1);
  });

  it('a queued tab adopts a broadcast that arrives after the lock was released (event-driven, one refresh)', async () => {
    const a = openTab();
    const b = openTab();
    // Hold back every broadcast until we release it by hand, like a slow cross-process delivery.
    const held: (() => void)[] = [];
    hub.deliver = (from, message) => {
      for (const endpoint of hub['endpoints'] as Set<{ onmessage: ((e: MessageEvent) => void) | null }>) {
        if (endpoint !== (from as unknown)) {
          held.push(() => endpoint.onmessage?.({ data: message } as MessageEvent));
        }
      }
    };
    const both = Promise.all([a.coordinator.refresh(null), b.coordinator.refresh(null)]);
    await new Promise((resolve) => setTimeout(resolve, 40));
    expect(server.refreshCalls).toBe(1);
    held.forEach((release) => release());
    const outcomes = await both;
    expect(outcomes.sort()).toEqual(['adopted', 'refreshed']);
    expect(server.refreshCalls).toBe(1);
  });

  it('does not wait for a broadcast when the lock was free', async () => {
    const tab = openTab();
    const started = performance.now();
    await tab.coordinator.refresh(null);
    expect(performance.now() - started).toBeLessThan(100);
  });

  it('a refresh that returns another user resets the tab, broadcasts, and reports user-changed', async () => {
    const a = openTab();
    await a.coordinator.login({ userName: 'a', password: 'b' });
    server.nextRefresh = () => of(makeAuthResponse('other', makeUser('CounterStaff', { id: 'user-2' })));
    const outcome = await a.coordinator.refresh(now + 900_000);
    expect(outcome).toBe('user-changed');
    expect(a.store.isAuthenticated()).toBe(false);
    expect(a.router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('sign-out in one tab while another tab is queued for a refresh ends the queued tab on /login', async () => {
    const a = openTab();
    const b = openTab();
    await a.coordinator.login({ userName: 'a', password: 'b' });
    await new Promise((resolve) => setTimeout(resolve, 0));
    server.nextRefresh = () => throwError(() => problem(401, 'auth.refresh_invalid'));
    const bRefresh = b.coordinator.refresh(null);
    const aLogout = a.coordinator.logout();
    await Promise.all([aLogout, bRefresh.catch(() => undefined)]);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(b.store.isAuthenticated()).toBe(false);
    expect(b.store.endedReason()).toBeNull();
  });
});
