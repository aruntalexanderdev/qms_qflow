import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router } from '@angular/router';
import { UserRole } from '../api/models/auth.models';
import { NotificationService } from '../notifications/notification.service';
import { mapHttpError } from '../errors/api-error';
import { SessionCoordinator } from './session-coordinator';
import { SessionStore } from './session-store';

/** Route of the role-neutral welcome page; the target of every redirect for a signed-in user. */
export const HOME_ROUTE = '/home';
export const LOGIN_ROUTE = '/login';

/**
 * Requires a session. On each navigation it also renews a token that expires within 30 s. A rejected refresh (401)
 * ends the session and goes to /login; a network error or 5xx keeps the session, shows the error, and stays on the
 * current page (AC-16).
 */
export const authGuard: CanActivateFn & CanActivateChildFn = async () => {
  const store = inject(SessionStore);
  const router = inject(Router);
  const coordinator = inject(SessionCoordinator);
  const notifications = inject(NotificationService);
  if (!store.isAuthenticated()) {
    return router.parseUrl(LOGIN_ROUTE);
  }
  try {
    await coordinator.ensureFresh();
  } catch (error) {
    notifications.error(mapHttpError(error));
    return false;
  }
  return store.isAuthenticated() ? true : router.parseUrl(LOGIN_ROUTE);
};

/** Only for the login page: a signed-in user goes to the home route. */
export const guestGuard: CanActivateFn = () => {
  const store = inject(SessionStore);
  return store.isAuthenticated() ? inject(Router).parseUrl(HOME_ROUTE) : true;
};

/** Allows the listed roles (spec 8 role table); anyone else signed in is redirected to /home. */
export function roleGuard(roles: readonly UserRole[]): CanActivateFn & CanActivateChildFn {
  return () => {
    const user = inject(SessionStore).user();
    const router = inject(Router);
    if (!user) {
      return router.parseUrl(LOGIN_ROUTE);
    }
    return roles.includes(user.role) ? true : router.parseUrl(HOME_ROUTE);
  };
}
