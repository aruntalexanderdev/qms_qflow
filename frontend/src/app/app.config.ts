import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { LanguageService } from './core/i18n/language.service';

/**
 * Before the first route resolves: load the stored language (so no translation keys flash), restore the session by
 * one refresh (ADR-007 A.6), then apply the user's language when nothing is stored (ADR-007 B).
 */
export async function initializeApp(
  language: Pick<LanguageService, 'init' | 'syncWithUser'>,
  auth: Pick<AuthService, 'restore'>,
): Promise<void> {
  await language.init();
  await auth.restore();
  await language.syncWithUser();
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideTranslateService({ fallbackLang: 'en' }),
    // The loader bypasses the interceptors (static files, no bearer).
    provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json', useHttpBackend: true }),
    provideAppInitializer(() => initializeApp(inject(LanguageService), inject(AuthService))),
  ],
};
