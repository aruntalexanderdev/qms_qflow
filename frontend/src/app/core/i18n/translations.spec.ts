import { arabicTexts, englishTexts } from '../../../testing/translations';
import { ARABIC, ENGLISH } from '../../../testing/language-fakes';
import { OfferedLanguage } from '../api/models/language.models';
import { KNOWN_ERROR_CODES } from '../errors/api-error';
import { NAVIGATION } from '../navigation/navigation';

type Texts = Record<string, unknown>;

function flatten(obj: Texts, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null ? flatten(value as Texts, `${prefix}${key}.`) : [`${prefix}${key}`],
  );
}

function lookup(obj: Texts, path: string): unknown {
  return path.split('.').reduce<unknown>((node, part) => (node as Texts | undefined)?.[part], obj);
}

/** The files in the registry fixture that must be complete: English always, others only when they are available. */
const FILES: Record<string, Texts> = { en: englishTexts, ar: arabicTexts };

function missingKeys(available: readonly OfferedLanguage[], files: Record<string, Texts> = FILES): string[] {
  const required = flatten(files['en']);
  return available
    .filter((language) => language.code !== 'en')
    .flatMap((language) => {
      const have = new Set(flatten(files[language.code] ?? {}));
      return required.filter((key) => !have.has(key)).map((key) => `${language.code}:${key}`);
    });
}

describe('translations (ADR-010: English required, others only when available)', () => {
  it('en.json contains every key the app uses: error codes, roles, navigation, rate-limit texts', () => {
    const keys = [
      ...KNOWN_ERROR_CODES.map((code) => `errors.${code}`),
      'errors.network',
      'errors.unknown',
      'errors.request.rate_limited_generic',
      'errors.request.rate_limited_one',
      'errors.request.rate_limited_two',
      ...['SuperAdmin', 'CompanyAdmin', 'BranchManager', 'CounterStaff'].map((role) => `roles.${role}`),
      ...NAVIGATION.flatMap((section) => [section.labelKey, ...section.items.map((item) => item.labelKey)]),
    ];
    for (const key of keys) {
      expect(lookup(englishTexts, key), `en ${key}`).toBeTypeOf('string');
    }
  });

  it('with only en available, ar.json is not checked', () => {
    const incomplete = { ...FILES, ar: { app: { name: 'x' } } };
    expect(missingKeys([ENGLISH], incomplete)).toEqual([]);
  });

  it('with ar available, the shipped ar.json has every key of en.json', () => {
    expect(missingKeys([ENGLISH, ARABIC])).toEqual([]);
  });

  it('with ar available, a missing key fails the check', () => {
    const broken = { ...FILES, ar: { app: { name: 'x' } } };
    expect(missingKeys([ENGLISH, ARABIC], broken).length).toBeGreaterThan(0);
    expect(missingKeys([ENGLISH, ARABIC], broken)[0]).toMatch(/^ar:/);
  });

  it('ar.json is kept and still has translated texts for when Arabic is offered', () => {
    expect(lookup(arabicTexts, 'roles.CompanyAdmin')).toBeTypeOf('string');
  });
});
