import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { initializeApp } from '../../app.config';
import { makeUser } from '../../../testing/auth-fakes';
import { AuthService } from './auth.service';
import { SessionCoordinator } from './session-coordinator';
import { SessionStore } from './session-store';

describe('AuthService', () => {
  const coordinator = { refresh: vi.fn(), login: vi.fn(), logout: vi.fn() };
  let service: AuthService;

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ providers: [{ provide: SessionCoordinator, useValue: coordinator }] });
    service = TestBed.inject(AuthService);
  });

  it('exposes the user from the store and never touches web storage', () => {
    TestBed.inject(SessionStore).set({ accessToken: 't', validUntil: 1, user: makeUser() });
    expect(service.user()?.name).toBe('Arun Alexander');
    expect(service.isAuthenticated()).toBe(true);
    expect(localStorage.getItem('accessToken')).toBeNull();
  });

  it('restore() refreshes once without navigating', async () => {
    coordinator.refresh.mockResolvedValue('refreshed');
    await service.restore();
    expect(coordinator.refresh).toHaveBeenCalledWith(null, { navigate: false });
  });

  it('restore() swallows a network error (stays signed out)', async () => {
    coordinator.refresh.mockRejectedValue(new HttpErrorResponse({ status: 0 }));
    await expect(service.restore()).resolves.toBeUndefined();
  });

  it('login and logout delegate to the coordinator', async () => {
    coordinator.login.mockResolvedValue(makeUser());
    await service.login({ userName: 'a', password: 'b' });
    await service.logout();
    expect(coordinator.login).toHaveBeenCalledWith({ userName: 'a', password: 'b' });
    expect(coordinator.logout).toHaveBeenCalled();
  });
});

describe('initializeApp', () => {
  it('loads the language, restores the session, then syncs the user language, in that order', async () => {
    const calls: string[] = [];
    await initializeApp(
      { init: async () => void calls.push('init'), syncWithUser: async () => void calls.push('sync') },
      { restore: async () => void calls.push('restore') },
    );
    expect(calls).toEqual(['init', 'restore', 'sync']);
  });
});
