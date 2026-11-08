import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
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

function isApiCall(url: string): boolean {
  return pathOf(url).startsWith(API_PREFIX);
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
  if (!isApiCall(request.url) || isAuthEndpoint(request.url)) {
    return next(request);
  }

  const coordinator = inject(SessionCoordinator);
  const store = inject(SessionStore);
  const now = inject(CLOCK);

  const prepare = async (): Promise<string | null> => {
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
    return store.accessToken();
  };

  return from(prepare()).pipe(
    switchMap((token) =>
      next(withBearer(request, token)).pipe(
        catchError((error: unknown) => {
          if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
            return throwError(() => error);
          }
          return from(coordinator.refresh(token)).pipe(
            switchMap((outcome) =>
              outcome === 'signed-out'
                ? throwError(() => error)
                : next(withBearer(request, store.accessToken())),
            ),
          );
        }),
      ),
    ),
  );
};
