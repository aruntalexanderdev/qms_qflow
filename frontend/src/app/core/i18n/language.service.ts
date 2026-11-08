import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { LanguagesApi } from '../api/languages.api';
import { OfferedLanguage, TextDirection } from '../api/models/language.models';
import { SessionStore } from '../auth/session-store';
import { LanguageState } from './language-state';

export const LANGUAGE_STORAGE_KEY = 'qms.lang';

/**
 * Ultimate fallback when the list cannot be loaded (AC-39): English, always shipped. It is not a list of supported
 * languages: the offered list comes from the API.
 */
export const DEFAULT_LANGUAGE = 'en';
const FALLBACK_OFFER: readonly OfferedLanguage[] = [{ code: DEFAULT_LANGUAGE, nativeName: 'English', direction: 'ltr' }];

/**
 * UI language and text direction (ADR-007 amendment 1, ADR-010). The offered languages, the default and each language's
 * direction come from `GET /api/v1/languages`, read anonymously at start-up and again for every signed-in user.
 * Precedence: `localStorage['qms.lang']` if offered, else `user.language` if offered, else the default. A stored
 * choice that is no longer offered is replaced. Switching changes local storage only: no server call yet.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly document = inject(DOCUMENT);
  private readonly store = inject(SessionStore);
  private readonly api = inject(LanguagesApi);
  private readonly state = inject(LanguageState);

  private readonly offeredList = signal<readonly OfferedLanguage[]>(FALLBACK_OFFER);
  private readonly defaultCode = signal(DEFAULT_LANGUAGE);

  readonly offered = this.offeredList.asReadonly();
  readonly defaultLanguage = this.defaultCode.asReadonly();
  readonly current = this.state.code.asReadonly();
  /** The switch is shown only when there is a choice (hidden in phase 1: English only). */
  readonly canSwitch = computed(() => this.offeredList().length > 1);
  readonly direction = computed<TextDirection>(
    () => this.offeredList().find((language) => language.code === this.state.code())?.direction ?? 'ltr',
  );

  /** The list load of the current identity (user id, or null when anonymous); one request per identity. */
  private load: { key: string | null; promise: Promise<void> } | null = null;
  private seenUser = false;

  constructor() {
    // A user appearing or changing (sign-in, restore, user switch, session adopted from another tab) or signing out:
    // read the list again for that identity and apply the precedence rule. The first run only records the state:
    // start-up is handled by init() and syncWithUser().
    effect(() => {
      const id = this.store.user()?.id ?? null;
      untracked(() => {
        if (!this.seenUser) {
          this.seenUser = true;
          return;
        }
        void this.refresh(id);
      });
    });
  }

  /** Start-up, anonymous: read the deployment's list, then apply stored-if-offered, else the default. */
  init(): Promise<void> {
    return this.refresh(null);
  }

  /** After the session is restored: re-read the list for the user and apply the precedence rule. */
  syncWithUser(): Promise<void> {
    return this.refresh(this.store.user()?.id ?? null);
  }

  /** The person at this PC chose a language (login page or shell switch). A code that is not offered is ignored. */
  async choose(code: string): Promise<void> {
    if (!this.offeredList().some((language) => language.code === code)) {
      return;
    }
    this.writeStored(code);
    await this.apply(code);
  }

  private refresh(userId: string | null): Promise<void> {
    if (!this.load || this.load.key !== userId) {
      const promise = this.loadAndApply(userId);
      this.load = { key: userId, promise };
    }
    return this.load.promise;
  }

  private async loadAndApply(userId: string | null): Promise<void> {
    try {
      const response = await firstValueFrom(this.api.list());
      if (this.load?.key !== userId) {
        return; // A newer identity (sign-in, sign-out) started its own load: do not apply this older answer.
      }
      this.offeredList.set(response.languages.length > 0 ? response.languages : FALLBACK_OFFER);
      this.defaultCode.set(response.defaultLanguage || DEFAULT_LANGUAGE);
    } catch {
      if (this.load?.key !== userId) {
        return;
      }
      // No error is shown (AC-39): English only, no switch, until a later call succeeds.
      this.offeredList.set(FALLBACK_OFFER);
      this.defaultCode.set(DEFAULT_LANGUAGE);
      this.load = null;
    }
    this.translate.addLangs(this.offeredList().map((language) => language.code));
    const user = this.store.user();
    await this.apply(this.resolve(user?.language ?? null, user !== null));
  }

  /** ADR-007 amendment 1 B.3/B.4. Stored value is written only for a signed-in user. */
  private resolve(userLanguage: string | null, signedIn: boolean): string {
    const isOffered = (code: string | null): code is string =>
      code !== null && this.offeredList().some((language) => language.code === code);
    const stored = this.readStored();
    let chosen: string;
    if (isOffered(stored)) {
      chosen = stored;
    } else if (signedIn && isOffered(userLanguage)) {
      chosen = userLanguage;
    } else {
      const fallback = this.defaultCode();
      chosen = isOffered(fallback) ? fallback : this.offeredList()[0].code;
    }
    if (signedIn && stored !== chosen) {
      this.writeStored(chosen);
    }
    return chosen;
  }

  private async apply(code: string): Promise<void> {
    this.state.code.set(code);
    const root = this.document.documentElement;
    root.lang = code;
    root.dir = this.direction();
    await firstValueFrom(this.translate.use(code));
  }

  private readStored(): string | null {
    try {
      return localStorage.getItem(LANGUAGE_STORAGE_KEY);
    } catch {
      return null;
    }
  }

  private writeStored(code: string): void {
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, code);
    } catch {
      // Storage blocked: the choice lasts until the page is closed.
    }
  }
}
