import { InjectionToken } from '@angular/core';

/** The part of `navigator.locks` the app uses. Replaced by a fake in tests. */
export interface LockManagerLike {
  request<T>(
    name: string,
    options: { mode: 'exclusive'; ifAvailable?: boolean },
    callback: (lock: unknown) => Promise<T>,
  ): Promise<T>;
}

/** The part of `BroadcastChannel` the app uses. A tab does not receive its own messages. */
export interface AuthChannelLike {
  postMessage(message: unknown): void;
  onmessage: ((event: MessageEvent) => void) | null;
  close(): void;
}

export const AUTH_LOCK_NAME = 'qms-auth-session';
export const AUTH_CHANNEL_NAME = 'qms-auth';

/** `navigator.locks`, or null where Web Locks are missing (non-secure contexts); then only the in-tab single flight applies. */
export const LOCK_MANAGER = new InjectionToken<LockManagerLike | null>('qms.lockManager', {
  providedIn: 'root',
  factory: () =>
    typeof navigator !== 'undefined' && 'locks' in navigator
      ? (navigator.locks as unknown as LockManagerLike)
      : null,
});

export const AUTH_CHANNEL = new InjectionToken<AuthChannelLike | null>('qms.authChannel', {
  providedIn: 'root',
  factory: () =>
    typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel(AUTH_CHANNEL_NAME) : null,
});

/** Epoch milliseconds. Shared by all tabs of one browser, so a `validUntil` sent over the channel stays meaningful. */
export const CLOCK = new InjectionToken<() => number>('qms.clock', {
  providedIn: 'root',
  factory: () => () => Date.now(),
});
