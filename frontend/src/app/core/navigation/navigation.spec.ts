import { NAVIGATION, navigationFor } from './navigation';

describe('navigationFor', () => {
  it('shows only the live Home entry to every role while later screens are not built', () => {
    for (const role of ['SuperAdmin', 'CompanyAdmin', 'BranchManager', 'CounterStaff'] as const) {
      const items = navigationFor(role).flatMap((section) => section.items.map((item) => item.id));
      expect(items).toEqual(['home']);
    }
  });

  it('follows the spec 8 role table once entries are live', () => {
    const allLive = NAVIGATION.map((section) => ({
      ...section,
      items: section.items.map((item) => ({ ...item, live: true })),
    }));
    const ids = (role: Parameters<typeof navigationFor>[0]) =>
      navigationFor(role, allLive).flatMap((section) => section.items.map((item) => item.id));
    expect(ids('SuperAdmin')).toEqual(['home', 'companies']);
    expect(ids('CounterStaff')).toEqual(['home', 'counter-console']);
    expect(ids('BranchManager')).toEqual(['home', 'dashboard', 'branches', 'reports']);
    expect(ids('CompanyAdmin')).toEqual([
      'home', 'service-types', 'priority-types', 'service-mapping', 'branches', 'users', 'templates', 'reports',
    ]);
  });

  it('returns nothing without a role', () => {
    expect(navigationFor(null)).toEqual([]);
  });
});
