import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { LanguageState } from '../i18n/language-state';
import { SessionCoordinator } from './session-coordinator';
import { CLOCK } from './session-environment';
import { SessionStore } from './session-store';

const API_PREFIX = '/api/';
const EXCLUDED_AUTH_PATH = /\/auth\/(login|refresh|logout)\/?$/i;

/** Calls that carry no bearer and never trigger a refresh, not even on their own 401 (ADR-007 A.6). */
export function isAuthEndpoint(url: string): boolean {
  return EXCLUDED_AUTH_PATH.test(pathOf(url));
}

function pathOf(url: string): string {
  try {
    return new URL(url, 'http://localhost').pathname;
  } catch {
    return url;
  }
}

/** Only same-origin URLs (relative, or absolute on this origin) under /api/ get the bearer token. */
function isApiCall(url: string): boolean {
  try {
    const parsed = new URL(url, location.origin);
    return parsed.origin === location.origin && parsed.pathname.startsWith(API_PREFIX);
  } catch {
    return false;
  }
}

function withBearer(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}

/**
 * Adds the bearer token to API calls. Refreshes first when the token expires within 30 s, and once after a 401, then
 * retries the original request once. All refreshing goes through SessionCoordinator.
 */
export const authInterceptor: HttpInterceptorFn = (
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> => {
  if (!isApiCall(request.url)) {
    return next(request);
  }
  // The active UI language goes to the API only (same origin, under /api/), also on the auth calls. Never elsewhere.
  request = request.clone({ setHeaders: { 'Accept-Language': inject(LanguageState).code() } });
  if (isAuthEndpoint(request.url)) {
    return next(request);
  }

  const coordinator = inject(SessionCoordinator);
  const store = inject(SessionStore);
  const now = inject(CLOCK);

  const prepare = async () => {
    if (store.accessToken() && !store.isFresh(now())) {
      try {
        await coordinator.ensureFresh();
      } catch (error) {
        // Refresh failed (network or 5xx). A token that has not expired yet can still serve this request.
        if (!store.isUsable(now())) {
          throw error;
        }
      }
    }
    return store.snapshot();
  };

  return from(prepare()).pipe(
    switchMap((session) =>
      next(withBearer(request, session?.accessToken ?? null)).pipe(
        catchError((error: unknown) => {
          if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
            return throwError(() => error);
          }
          return from(coordinator.refresh(session?.validUntil ?? null)).pipe(
            switchMap((outcome) => {
              // Never replay the request under another identity (a user switch in another tab, ADR-007 A.4).
              const sameUser = (store.user()?.id ?? null) === (session?.user.id ?? null);
              return outcome === 'signed-out' || outcome === 'user-changed' || !sameUser
                ? throwError(() => error)
                : next(withBearer(request, store.accessToken()));
            }),
          );
        }),
      ),
    ),
  );
};
