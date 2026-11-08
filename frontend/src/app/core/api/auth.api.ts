import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AuthResponse, CurrentUser, LoginRequest } from './models/auth.models';

/** Path prefix shared with the interceptor: relative, so the dev proxy and Nginx keep one origin and the cookie works. */
export const AUTH_API_PATH = '/api/v1/auth';

/**
 * Typed client for /api/v1/auth. Do not call it from components: login, refresh and logout go through
 * `SessionCoordinator` (ADR-007 A), which is the only caller of refresh and logout.
 * No `withCredentials` is needed: the HttpOnly refresh cookie is same-origin.
 */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${AUTH_API_PATH}/login`, request);
  }

  /** No body: the refresh token travels only in the `qms_refresh` cookie. */
  refresh(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${AUTH_API_PATH}/refresh`, null);
  }

  logout(): Observable<void> {
    return this.http.post<void>(`${AUTH_API_PATH}/logout`, null);
  }

  /** Typed for later stories; start-up restore uses refresh, which already returns `user`. */
  me(): Observable<CurrentUser> {
    return this.http.get<CurrentUser>(`${AUTH_API_PATH}/me`);
  }
}
