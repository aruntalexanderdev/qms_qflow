import { arabicTexts, englishTexts } from '../../../testing/translations';
import { KNOWN_ERROR_CODES } from '../errors/api-error';
import { NAVIGATION } from '../navigation/navigation';

function flatten(obj: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null
      ? flatten(value as Record<string, unknown>, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  );
}

function lookup(obj: Record<string, unknown>, path: string): unknown {
  return path.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], obj);
}

describe('translations', () => {
  it('en.json and ar.json have exactly the same keys', () => {
    expect(flatten(arabicTexts).sort()).toEqual(flatten(englishTexts).sort());
  });

  it('every known error code, extra error key, role and navigation label is translated', () => {
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
      expect(lookup(arabicTexts, key), `ar ${key}`).toBeTypeOf('string');
    }
  });
});
