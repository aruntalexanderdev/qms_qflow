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
/** 'user-changed': the refresh returned another user than this tab had; the tab was reset (ADR-007 A.4). */
export type RefreshOutcome = 'refreshed' | 'adopted' | 'signed-out' | 'user-changed';

export const REFRESH_REUSED_CODE = 'auth.refresh_reused';

/** How long a tab that had to queue for the lock waits for the previous holder's broadcast before refreshing itself. */
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
  /** Callbacks waiting for a newer session to arrive over the channel (see waitForBroadcast). */
  private readonly sessionWaiters = new Set<() => void>();

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
    const flight: Promise<RefreshOutcome> = this.withLock((contended) =>
      this.refreshInsideLock(staleValidUntil, navigate, contended),
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
    contended: boolean,
  ): Promise<RefreshOutcome> {
    if (this.hasNewerSession(staleValidUntil)) {
      return 'adopted';
    }
    if (contended) {
      // This tab had to queue behind another tab's lock. That tab's new session travels over the channel, between
      // browser processes, and can trail the lock release by a few ms: wait for it (an event, or one timeout) so a
      // single refresh serves both tabs. An uncontended refresh never waits.
      await this.waitForBroadcast(BROADCAST_GRACE_MS, () => this.hasNewerSession(staleValidUntil));
      if (this.hasNewerSession(staleValidUntil)) {
        return 'adopted';
      }
    }
    try {
      const response = await firstValueFrom(this.api.refresh());
      const current = this.store.snapshot();
      if (current && current.user.id !== response.user.id) {
        // The cookie now belongs to another user than this tab (and the request that triggered the refresh) had:
        // the same rule as for a broadcast. Reset the tab; others learn it from the broadcast.
        this.store.clear();
        this.broadcastSession(response, this.now() + response.expiresIn * 1000);
        void this.router.navigateByUrl('/');
        return 'user-changed';
      }
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
    this.broadcastSession(response, validUntil);
  }

  private broadcastSession(response: AuthResponse, validUntil: number): void {
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
        for (const wake of [...this.sessionWaiters]) {
          wake();
        }
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

  /** Resolves when `isDone()` holds after a session message, or after `timeoutMs`, whichever comes first. */
  private waitForBroadcast(timeoutMs: number, isDone: () => boolean): Promise<void> {
    return new Promise<void>((resolve) => {
      const finish = () => {
        clearTimeout(timer);
        this.sessionWaiters.delete(check);
        resolve();
      };
      const check = () => {
        if (isDone()) {
          finish();
        }
      };
      const timer = setTimeout(finish, timeoutMs);
      this.sessionWaiters.add(check);
    });
  }

  /**
   * Runs `callback` under the exclusive lock. `contended` is true when another holder made this call queue (found with
   * an `ifAvailable` probe, not a timing guess). Without Web Locks the callback just runs.
   */
  private async withLock<T>(callback: (contended: boolean) => Promise<T>): Promise<T> {
    if (!this.locks) {
      return callback(false);
    }
    const probe = await this.locks.request(AUTH_LOCK_NAME, { mode: 'exclusive', ifAvailable: true }, async (lock) =>
      lock ? { ran: true as const, value: await callback(false) } : { ran: false as const },
    );
    if (probe.ran) {
      return probe.value;
    }
    return this.locks.request(AUTH_LOCK_NAME, { mode: 'exclusive' }, () => callback(true));
  }
}
