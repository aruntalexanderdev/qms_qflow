import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { Observable, of, throwError } from 'rxjs';
import { makeUser } from '../../../testing/auth-fakes';
import { ARABIC, ENGLISH } from '../../../testing/language-fakes';
import { LanguagesApi } from '../api/languages.api';
import { LanguagesResponse, OfferedLanguage } from '../api/models/language.models';
import { SessionStore } from '../auth/session-store';
import { LanguageState } from './language-state';
import { LANGUAGE_STORAGE_KEY, LanguageService } from './language.service';

const PHASE_1: LanguagesResponse = { defaultLanguage: 'en', languages: [ENGLISH] };
const TWO: LanguagesResponse = { defaultLanguage: 'en', languages: [ENGLISH, ARABIC] };
const flush = async () => {
  TestBed.tick(); // run the effect that watches the signed-in user
  await new Promise((resolve) => setTimeout(resolve, 0));
};

describe('LanguageService', () => {
  let service: LanguageService;
  let store: SessionStore;
  let html: HTMLElement;
  let responses: (() => Observable<LanguagesResponse>)[];
  const api = { list: vi.fn(() => (responses.shift() ?? (() => of(PHASE_1)))()) };

  const signIn = (language: string, id = 'user-1') =>
    store.set({ accessToken: 't', validUntil: Date.now() + 900_000, user: makeUser('CompanyAdmin', { id, language }) });

  function setup(...lists: (LanguagesResponse | Error)[]) {
    responses = lists.map((list) => () => (list instanceof Error ? throwError(() => list) : of(list)));
  }

  beforeEach(() => {
    localStorage.clear();
    api.list.mockClear();
    TestBed.configureTestingModule({
      providers: [provideTranslateService({ fallbackLang: 'en' }), { provide: LanguagesApi, useValue: api }],
    });
    html = TestBed.inject(DOCUMENT).documentElement;
    store = TestBed.inject(SessionStore);
    service = TestBed.inject(LanguageService);
    TestBed.tick(); // first effect run (records the anonymous state, like the first render after start-up)
  });

  describe('AC-32 phase 1: English only', () => {
    it('reads the list once anonymously, shows English ltr and no switch', async () => {
      setup(PHASE_1);
      await service.init();
      expect(api.list).toHaveBeenCalledTimes(1);
      expect(html.lang).toBe('en');
      expect(html.dir).toBe('ltr');
      expect(service.canSwitch()).toBe(false);
      expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
    });
  });

  describe('AC-33 two offered languages', () => {
    it('lists both, choosing ar sets lang, dir=rtl and qms.lang; the active code is shared for Accept-Language', async () => {
      setup(TWO);
      await service.init();
      expect(service.canSwitch()).toBe(true);
      expect(service.offered().map((l) => l.nativeName)).toEqual(['English', 'العربية']);
      await service.choose('ar');
      expect(html.lang).toBe('ar');
      expect(html.dir).toBe('rtl');
      expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
      expect(TestBed.inject(LanguageState).code()).toBe('ar');
      expect(api.list).toHaveBeenCalledTimes(1); // the switch itself makes no request
      await service.choose('en');
      expect(html.dir).toBe('ltr');
    });

    it.each<[OfferedLanguage['direction']]>([['rtl'], ['ltr']])(
      'takes dir from the language direction (%s), not from the code',
      async (direction) => {
        setup({ defaultLanguage: 'en', languages: [ENGLISH, { code: 'xx', nativeName: 'Xx', direction }] });
        await service.init();
        await service.choose('xx');
        expect(html.lang).toBe('xx');
        expect(html.dir).toBe(direction);
      },
    );

    it('ignores a code that is not offered', async () => {
      setup(PHASE_1);
      await service.init();
      await service.choose('ar');
      expect(service.current()).toBe('en');
      expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
    });
  });

  describe('AC-34 precedence', () => {
    interface Case { offered: LanguagesResponse; stored: string | null; user: string; expected: string; storedAfter: string }
    const cases: Case[] = [
      { offered: PHASE_1, stored: null, user: 'ar', expected: 'en', storedAfter: 'en' },
      { offered: PHASE_1, stored: 'ar', user: 'ar', expected: 'en', storedAfter: 'en' },
      { offered: PHASE_1, stored: 'en', user: 'ar', expected: 'en', storedAfter: 'en' },
      { offered: TWO, stored: null, user: 'ar', expected: 'ar', storedAfter: 'ar' },
      { offered: TWO, stored: 'ar', user: 'en', expected: 'ar', storedAfter: 'ar' },
      { offered: TWO, stored: 'en', user: 'ar', expected: 'en', storedAfter: 'en' },
      { offered: TWO, stored: 'fr', user: 'ar', expected: 'ar', storedAfter: 'ar' },
    ];
    it.each(cases)('offered %# stored=$stored user=$user -> $expected', async ({ offered, stored, user, expected, storedAfter }) => {
      if (stored) localStorage.setItem(LANGUAGE_STORAGE_KEY, stored);
      setup(offered, offered);
      await service.init();
      signIn(user);
      await service.syncWithUser();
      expect(service.current()).toBe(expected);
      expect(html.lang).toBe(expected);
      expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe(storedAfter);
    });

    it('before sign-in a stored language that is not offered is not shown', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
      setup(PHASE_1);
      await service.init();
      expect(service.current()).toBe('en');
      expect(html.dir).toBe('ltr');
    });
  });

  describe('AC-35 the list is read again for the signed-in user', () => {
    it('re-reads after sign-in, and an active language the new list lacks is replaced', async () => {
      setup(TWO, TWO, PHASE_1);
      await service.init();
      await service.choose('ar');
      signIn('ar', 'ca-c1');
      await flush();
      expect(api.list).toHaveBeenCalledTimes(2);
      expect(service.canSwitch()).toBe(true);
      expect(service.current()).toBe('ar');
      // sign-out and another company's admin signs in: {en} only
      store.clear();
      await flush();
      signIn('en', 'ca-c2');
      await flush();
      expect(service.canSwitch()).toBe(false);
      expect(service.current()).toBe('en');
      expect(html.dir).toBe('ltr');
      expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
    });

    it('start-up restore: syncWithUser and the effect make one request for the same user', async () => {
      setup(PHASE_1, TWO);
      await service.init();
      signIn('en', 'ca-c1');
      await service.syncWithUser();
      await flush();
      expect(api.list).toHaveBeenCalledTimes(2);
    });

    it('a session adopted from another tab (a different user) reads the list again', async () => {
      setup(PHASE_1, TWO, PHASE_1);
      await service.init();
      signIn('en', 'u1');
      await flush();
      signIn('en', 'u2');
      await flush();
      expect(api.list).toHaveBeenCalledTimes(3);
      expect(service.canSwitch()).toBe(false);
    });
  });

  describe('AC-39 the list cannot be loaded', () => {
    it('falls back to English with no switch and no error, and a later success updates the list', async () => {
      setup(new Error('boom'), TWO);
      await service.init();
      expect(service.canSwitch()).toBe(false);
      expect(service.current()).toBe('en');
      expect(html.lang).toBe('en');
      signIn('en', 'u1');
      await flush();
      expect(service.canSwitch()).toBe(true);
    });
  });
});
