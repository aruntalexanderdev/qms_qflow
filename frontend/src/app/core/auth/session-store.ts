import { Injectable, computed, signal } from '@angular/core';
import { CurrentUser } from '../api/models/auth.models';

/** Everything a tab knows about the session. Memory only: nothing here is ever written to storage (ADR-004, ADR-007). */
export interface Session {
  accessToken: string;
  /** Local epoch milliseconds when the token expires: arrival time + expiresIn (ADR-007 A.6), not the server clock. */
  validUntil: number;
  user: CurrentUser;
}

/** Refresh when the token expires within this many milliseconds. */
export const REFRESH_MARGIN_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly session = signal<Session | null>(null);

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);

  /** Set when a session was ended by the server; the login page shows it once (for `auth.refresh_reused`). */
  readonly endedReason = signal<string | null>(null);

  accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  snapshot(): Session | null {
    return this.session();
  }

  set(session: Session): void {
    this.session.set(session);
  }

  clear(): void {
    this.session.set(null);
  }

  /** True when a token exists and does not expire within `marginMs`. */
  isFresh(now: number, marginMs = REFRESH_MARGIN_MS): boolean {
    const current = this.session();
    return current !== null && current.validUntil - now > marginMs;
  }

  /** True when the token exists and has not expired yet. */
  isUsable(now: number): boolean {
    return this.isFresh(now, 0);
  }
}
