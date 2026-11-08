/** Contract of GET /api/v1/languages (STORY-051, ADR-010 section 8). */
export type TextDirection = 'ltr' | 'rtl';

export interface OfferedLanguage {
  /** BCP-47 code, for example `en`. Never compared with a literal in the app: the list decides. */
  code: string;
  /** The language's own name ("English", "العربية"); shown as is, never translated. */
  nativeName: string;
  direction: TextDirection;
}

export interface LanguagesResponse {
  defaultLanguage: string;
  languages: OfferedLanguage[];
}
