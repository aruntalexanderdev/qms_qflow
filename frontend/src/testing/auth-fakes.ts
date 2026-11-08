import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { delay } from 'rxjs/operators';
import { AuthResponse, CurrentUser, UserRole } from '../app/core/api/models/auth.models';
import { AuthChannelLike, LockManagerLike } from '../app/core/auth/session-environment';

export function makeUser(role: UserRole = 'CompanyAdmin', overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'user-1',
    userName: 'admin',
    name: 'Arun Alexander',
    role,
    companyId: role === 'SuperAdmin' ? null : 'company-1',
    language: 'en',
    branchIds: [],
    counterIds: [],
    ...overrides,
  };
}

export function makeAuthResponse(token = 'token-1', user = makeUser(), expiresIn = 900): AuthResponse {
  return { accessToken: token, tokenType: 'Bearer', expiresIn, expiresAt: '2026-11-08T10:00:00Z', user };
}

export function problem(status: number, code: string, headers: Record<string, string> = {}): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: { status, code },
    headers: new HttpHeaders(headers),
  });
}

/** One lock shared by every fake "tab": requests run one at a time in arrival order, like navigator.locks. */
export class FakeLockManager implements LockManagerLike {
  private tail: Promise<unknown> = Promise.resolve();
  private pending = 0;
  held = 0;
  maxHeld = 0;

  request<T>(
    _name: string,
    options: { mode: 'exclusive'; ifAvailable?: boolean },
    callback: (lock: unknown) => Promise<T>,
  ): Promise<T> {
    if (options.ifAvailable && this.pending > 0) {
      return callback(null);
    }
    this.pending++;
    const run = this.tail.then(async () => {
      this.held++;
      this.maxHeld = Math.max(this.maxHeld, this.held);
      try {
        return await callback({});
      } finally {
        this.held--;
        this.pending--;
      }
    });
    this.tail = run.catch(() => undefined);
    return run;
  }
}

/** BroadcastChannel hub: a message reaches every other endpoint, asynchronously, never the sender. */
export class FakeChannelHub {
  private readonly endpoints = new Set<FakeChannel>();

  create(): FakeChannel {
    const channel = new FakeChannel(this);
    this.endpoints.add(channel);
    return channel;
  }

  deliver(from: FakeChannel, message: unknown): void {
    for (const endpoint of this.endpoints) {
      if (endpoint !== from) {
        queueMicrotask(() => endpoint.onmessage?.({ data: message } as MessageEvent));
      }
    }
  }

  remove(channel: FakeChannel): void {
    this.endpoints.delete(channel);
  }
}

export class FakeChannel implements AuthChannelLike {
  onmessage: ((event: MessageEvent) => void) | null = null;
  readonly sent: unknown[] = [];

  constructor(private readonly hub: FakeChannelHub) {}

  postMessage(message: unknown): void {
    this.sent.push(message);
    this.hub.deliver(this, message);
  }

  close(): void {
    this.hub.remove(this);
  }
}

/** A server shared by the fake tabs: counts refresh requests and rotates a token per success. */
export class FakeAuthServer {
  refreshCalls = 0;
  logoutCalls = 0;
  loginCalls = 0;
  concurrentRefreshes = 0;
  maxConcurrentRefreshes = 0;
  nextRefresh: (() => Observable<AuthResponse>) | null = null;
  private counter = 0;

  readonly api = {
    login: () => {
      this.loginCalls++;
      return of(makeAuthResponse(`login-${++this.counter}`));
    },
    refresh: (): Observable<AuthResponse> => {
      this.refreshCalls++;
      this.concurrentRefreshes++;
      this.maxConcurrentRefreshes = Math.max(this.maxConcurrentRefreshes, this.concurrentRefreshes);
      const outcome = this.nextRefresh
        ? this.nextRefresh()
        : of(makeAuthResponse(`refresh-${++this.counter}`));
      return new Observable<AuthResponse>((subscriber) => {
        const sub = outcome.pipe(delay(5)).subscribe({
          next: (value) => {
            this.concurrentRefreshes--;
            subscriber.next(value);
          },
          error: (error) => {
            this.concurrentRefreshes--;
            subscriber.error(error);
          },
          complete: () => subscriber.complete(),
        });
        return () => sub.unsubscribe();
      });
    },
    logout: (): Observable<void> => {
      this.logoutCalls++;
      return of(undefined);
    },
    me: () => throwError(() => problem(500, 'server.unexpected_error')),
  };
}
