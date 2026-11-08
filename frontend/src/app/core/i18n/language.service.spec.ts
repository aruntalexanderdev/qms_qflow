import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { makeUser } from '../../../testing/auth-fakes';
import { SessionStore } from '../auth/session-store';
import { LANGUAGE_STORAGE_KEY, LanguageService } from './language.service';

describe('LanguageService', () => {
  let service: LanguageService;
  let store: SessionStore;
  let html: HTMLElement;

  const signIn = (language: 'en' | 'ar') =>
    store.set({ accessToken: 't', validUntil: Date.now() + 900_000, user: makeUser('CompanyAdmin', { language }) });

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
    html = TestBed.inject(DOCUMENT).documentElement;
    store = TestBed.inject(SessionStore);
    service = TestBed.inject(LanguageService);
  });

  it('defaults to English, ltr, before sign-in', async () => {
    await service.init();
    expect(html.lang).toBe('en');
    expect(html.dir).toBe('ltr');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
  });

  it('switching to Arabic sets lang=ar dir=rtl and stores the choice', async () => {
    await service.choose('ar');
    expect(html.lang).toBe('ar');
    expect(html.dir).toBe('rtl');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
    await service.choose('en');
    expect(html.dir).toBe('ltr');
  });

  it('without a stored choice, user.language is applied and stored', async () => {
    signIn('ar');
    await service.syncWithUser();
    expect(html.dir).toBe('rtl');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
  });

  it('a stored choice wins over user.language', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar');
    signIn('en');
    await service.syncWithUser();
    expect(service.current()).toBe('ar');
    expect(html.dir).toBe('rtl');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
  });

  it('ignores an invalid stored value', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'fr');
    await service.init();
    expect(service.current()).toBe('en');
  });
});
