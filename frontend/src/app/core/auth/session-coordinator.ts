import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from '../api/auth.api';
import { AuthResponse, CurrentUser, LoginRequest } from '../api/models/auth.models';
import { ProblemDetails } from '../api/models/problem-details';
import { AUTH_CHANNEL, AUTH_LOCK_NAME, CLOCK, LOCK_MANAGER } from './session-environment';
import { SessionStore } from './session-store';

/** Messages on BroadcastChannel('qms-auth'); in memory only, same origin (ADR-007 A.4). */
export type AuthMessage =
  | {
      type: 'session';
      accessToken: string;
      /** Local epoch ms; all tabs share the PC clock, so this is valid in the receiver. */
      validUntil: number;
      expiresAt: string;
      user: CurrentUser;
    }
  | { type: 'signed-out'; reason?: string };

/** What a refresh request ended up doing. */
export type RefreshOutcome = 'refreshed' | 'adopted' | 'signed-out';

export const REFRESH_REUSED_CODE = 'auth.refresh_reused';

/** Waiting longer than this for the lock means another tab held it. */
const WAITED_THRESHOLD_MS = 3;
/** How long a tab that waited listens for the other tab's broadcast before refreshing itself. */
const BROADCAST_GRACE_MS = 150;

/**
 * The only code that calls login, refresh and logout (ADR-007 A). Each runs inside the Web Lock `qms-auth-session`,
 * so the browser runs at most one of them at a time across all tabs; the tab that did the work broadcasts the result.
 * Inside one tab concurrent refresh callers share one promise (single flight).
 */
@Injectable({ providedIn: 'root' })
export class SessionCoordinator {
  private readonly api = inject(AuthApi);
  private readonly store = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly locks = inject(LOCK_MANAGER);
  private readonly channel = inject(AUTH_CHANNEL);
  private readonly now = inject(CLOCK);

  private inFlight: Promise<RefreshOutcome> | null = null;

  constructor() {
    if (this.channel) {
      this.channel.onmessage = (event) => this.onMessage(event.data as AuthMessage);
      inject(DestroyRef).onDestroy(() => this.channel?.close());
    }
  }

  /** Signs in under the lock, so no other tab refreshes with the old cookie while this one replaces it. */
  login(request: LoginRequest): Promise<CurrentUser> {
    return this.withLock(async () => {
      const response = await firstValueFrom(this.api.login(request));
      this.adoptAndBroadcast(response);
      return response.user;
    });
  }

  /**
   * Makes sure the tab holds a token that does not expire within the refresh margin. Returns 'adopted' when no request
   * was needed. Throws the HttpErrorResponse for a network error or 5xx (the session is kept).
   */
  async ensureFresh(): Promise<RefreshOutcome | 'fresh'> {
    if (this.store.isFresh(this.now())) {
      return 'fresh';
    }
    return this.refresh(this.store.snapshot()?.validUntil ?? null);
  }

  /**
   * Refreshes the session. `staleValidUntil` identifies the session that needed refreshing by its expiry (null at
   * start-up); if a newer one reached this tab meanwhile, it is used and no request is sent. (Tokens are not
   * compared: two tokens issued in the same second are identical.) A refresh 401 ends the session in all tabs.
   * `navigate: false` is for the app initialiser, where the router has not started yet.
   */
  refresh(staleValidUntil: number | null, options: { navigate?: boolean } = {}): Promise<RefreshOutcome> {
    if (this.inFlight) {
      return this.inFlight;
    }
    if (this.hasNewerSession(staleValidUntil)) {
      return Promise.resolve('adopted');
    }
    const navigate = options.navigate ?? true;
    const requestedAt = performance.now();
    const flight: Promise<RefreshOutcome> = this.withLock(() =>
      this.refreshInsideLock(staleValidUntil, navigate, performance.now() - requestedAt),
    ).finally(() => {
      if (this.inFlight === flight) {
        this.inFlight = null;
      }
    });
    this.inFlight = flight;
    return flight;
  }

  /** Revokes the refresh token under the lock, then signs this and every other tab out. A failed request keeps the session. */
  async logout(): Promise<void> {
    await this.withLock(() => firstValueFrom(this.api.logout()));
    this.endSession(null, true);
  }

  private async refreshInsideLock(
    staleValidUntil: number | null,
    navigate: boolean,
    waitedMs: number,
  ): Promise<RefreshOutcome> {
    // A tab that had to wait for the lock gives the previous holder's broadcast time to arrive (it travels between
    // browser processes and can be a few milliseconds behind the lock release), so one refresh serves both tabs.
    const deadline = performance.now() + (waitedMs > WAITED_THRESHOLD_MS ? BROADCAST_GRACE_MS : 0);
    do {
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
      if (this.hasNewerSession(staleValidUntil)) {
        return 'adopted';
      }
    } while (performance.now() < deadline);
    try {
      const response = await firstValueFrom(this.api.refresh());
      this.adoptAndBroadcast(response);
      return 'refreshed';
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        const code = (error.error as ProblemDetails | null)?.code;
        this.endSession(code === REFRESH_REUSED_CODE ? code : null, navigate);
        return 'signed-out';
      }
      // Network error or 5xx: the session is kept, only the current request fails.
      throw error;
    }
  }

  /** A session newer than the one that needed refreshing reached this tab (broadcast), and it is still fresh. */
  private hasNewerSession(staleValidUntil: number | null): boolean {
    const current = this.store.snapshot();
    return (
      current !== null &&
      (staleValidUntil === null || current.validUntil > staleValidUntil) &&
      this.store.isFresh(this.now())
    );
  }

  private adoptAndBroadcast(response: AuthResponse): void {
    const validUntil = this.now() + response.expiresIn * 1000;
    this.store.set({ accessToken: response.accessToken, validUntil, user: response.user });
    this.store.endedReason.set(null);
    this.post({
      type: 'session',
      accessToken: response.accessToken,
      validUntil,
      expiresAt: response.expiresAt,
      user: response.user,
    });
  }

  private endSession(reason: string | null, navigate: boolean): void {
    this.store.clear();
    this.store.endedReason.set(reason);
    this.post({ type: 'signed-out', reason: reason ?? undefined });
    if (navigate) {
      void this.router.navigateByUrl('/login');
    }
  }

  private onMessage(message: AuthMessage): void {
    if (message.type === 'session') {
      const current = this.store.snapshot();
      if (current && current.user.id !== message.user.id) {
        // The shared cookie now belongs to someone else: drop this tab's state and start over.
        this.store.clear();
        void this.router.navigateByUrl('/');
        return;
      }
      if (!current || message.validUntil > current.validUntil) {
        this.store.set({
          accessToken: message.accessToken,
          validUntil: message.validUntil,
          user: message.user,
        });
        this.store.endedReason.set(null);
      }
      return;
    }
    if (message.type === 'signed-out') {
      const wasSignedIn = this.store.isAuthenticated();
      this.store.clear();
      this.store.endedReason.set(message.reason ?? null);
      if (wasSignedIn || message.reason) {
        void this.router.navigateByUrl('/login');
      }
    }
  }

  private post(message: AuthMessage): void {
    this.channel?.postMessage(message);
  }

  private withLock<T>(callback: () => Promise<T>): Promise<T> {
    if (!this.locks) {
      return callback();
    }
    return this.locks.request(AUTH_LOCK_NAME, { mode: 'exclusive' }, callback);
  }
}
