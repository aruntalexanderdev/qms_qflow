import { Injectable, inject } from '@angular/core';
import { CurrentUser, LoginRequest } from '../api/models/auth.models';
import { SessionCoordinator } from './session-coordinator';
import { SessionStore } from './session-store';

/**
 * Facade over the session for components and guards. State is in memory only (ADR-004): the access token is never
 * decoded and never written to storage; everything shown comes from `user`.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly store = inject(SessionStore);
  private readonly coordinator = inject(SessionCoordinator);

  readonly user = this.store.user;
  readonly isAuthenticated = this.store.isAuthenticated;
  /** Why the last session ended, when the login page must say so (`auth.refresh_reused`). */
  readonly endedReason = this.store.endedReason;

  login(request: LoginRequest): Promise<CurrentUser> {
    return this.coordinator.login(request);
  }

  /**
   * Start-up restore (ADR-007 A.6): one refresh with the cookie. A 401 means "signed out" and is not an error; a
   * network error or 5xx leaves the tab signed out, and the guard sends the user to /login.
   */
  async restore(): Promise<void> {
    try {
      await this.coordinator.refresh(null, { navigate: false });
    } catch {
      // Server unreachable at start-up: stay signed out. Nothing to show before the first route.
    }
  }

  logout(): Promise<void> {
    return this.coordinator.logout();
  }

  clearEndedReason(): void {
    this.store.endedReason.set(null);
  }
}
