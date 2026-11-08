import { Injectable, signal } from '@angular/core';

/**
 * The active UI language code, readable without any HTTP dependency, so the interceptor can send it as
 * `Accept-Language` without a circular injection (LanguageService -> LanguagesApi -> HttpClient -> interceptor).
 */
@Injectable({ providedIn: 'root' })
export class LanguageState {
  readonly code = signal('en');
}
