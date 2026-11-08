import { Provider } from '@angular/core';
import { TranslateLoader, TranslationObject, provideTranslateLoader, provideTranslateService } from '@ngx-translate/core';
import { Observable, of } from 'rxjs';
import ar from '../../public/i18n/ar.json';
import en from '../../public/i18n/en.json';

/** The real en.json / ar.json, so tests see the shipped texts. */
class JsonLoader implements TranslateLoader {
  getTranslation(lang: string): Observable<TranslationObject> {
    return of((lang === 'ar' ? ar : en) as TranslationObject);
  }
}

export function provideTestTranslations(): Provider[] {
  return [provideTranslateService({ fallbackLang: 'en', lang: 'en' }), provideTranslateLoader(JsonLoader)];
}

export { ar as arabicTexts, en as englishTexts };
