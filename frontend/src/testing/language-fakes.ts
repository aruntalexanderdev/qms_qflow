import { computed, signal } from '@angular/core';
import { OfferedLanguage } from '../app/core/api/models/language.models';

export const ENGLISH: OfferedLanguage = { code: 'en', nativeName: 'English', direction: 'ltr' };
export const ARABIC: OfferedLanguage = { code: 'ar', nativeName: 'العربية', direction: 'rtl' };

/** A stand-in for LanguageService in component tests: the offered list decides whether the switch shows. */
export function fakeLanguageService(offered: readonly OfferedLanguage[] = [ENGLISH], current = 'en') {
  const offeredSignal = signal(offered);
  return {
    offered: offeredSignal.asReadonly(),
    current: signal(current).asReadonly(),
    canSwitch: computed(() => offeredSignal().length > 1),
    choose: vi.fn().mockResolvedValue(undefined),
  };
}
