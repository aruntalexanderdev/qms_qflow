import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Language } from '../api/models/auth.models';
import { SessionStore } from '../auth/session-store';

export const LANGUAGE_STORAGE_KEY = 'qms.lang';
export const DEFAULT_LANGUAGE: Language = 'en';
export const SUPPORTED_LANGUAGES: readonly Language[] = ['en', 'ar'];

function isLanguage(value: unknown): value is Language {
  return value === 'en' || value === 'ar';
}

/**
 * UI language and text direction (ADR-007 B). `localStorage['qms.lang']` wins when set; otherwise the language of the
 * signed-in user (`user.language`, never the `lang` claim) is applied and stored; before sign-in the default is `en`.
 * Switching language in the app changes local storage only: no server call (no endpoint yet).
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly document = inject(DOCUMENT);
  private readonly store = inject(SessionStore);

  private readonly active = signal<Language>(DEFAULT_LANGUAGE);
  readonly current = this.active.asReadonly();
  readonly direction = computed<'ltr' | 'rtl'>(() => (this.active() === 'ar' ? 'rtl' : 'ltr'));

  constructor() {
    this.translate.addLangs([...SUPPORTED_LANGUAGES]);
    this.translate.setFallbackLang(DEFAULT_LANGUAGE);
    // A user appearing (sign-in, restore, session adopted from another tab) applies the precedence rule.
    effect(() => {
      const user = this.store.user();
      if (user) {
        void this.apply(this.resolveFor(user.language));
      }
    });
  }

  /** Start-up: the stored choice, else `en`. Resolves when the translations are loaded, so no keys flash. */
  init(): Promise<void> {
    return this.apply(this.readStored() ?? DEFAULT_LANGUAGE);
  }

  /** After the session is restored: applies `user.language` when nothing is stored. Resolves when loaded. */
  syncWithUser(): Promise<void> {
    const user = this.store.user();
    return user ? this.apply(this.resolveFor(user.language)) : Promise.resolve();
  }

  /** The person at this PC chose a language (login page or shell switch). Local storage only. */
  choose(language: Language): Promise<void> {
    this.writeStored(language);
    return this.apply(language);
  }

  /** Stored choice wins; otherwise the user's language, which is then stored. */
  private resolveFor(userLanguage: Language): Language {
    const stored = this.readStored();
    if (stored) {
      return stored;
    }
    this.writeStored(userLanguage);
    return userLanguage;
  }

  private async apply(language: Language): Promise<void> {
    this.active.set(language);
    const root = this.document.documentElement;
    root.lang = language;
    root.dir = language === 'ar' ? 'rtl' : 'ltr';
    await firstValueFrom(this.translate.use(language));
  }

  private readStored(): Language | null {
    try {
      const value = localStorage.getItem(LANGUAGE_STORAGE_KEY);
      return isLanguage(value) ? value : null;
    } catch {
      return null;
    }
  }

  private writeStored(language: Language): void {
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    } catch {
      // Storage blocked: the choice lasts until the page is closed.
    }
  }
}
