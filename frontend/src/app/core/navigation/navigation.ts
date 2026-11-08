import { UserRole } from '../api/models/auth.models';

export interface NavItem {
  id: string;
  /** Translation key. */
  labelKey: string;
  route: string;
  /** Roles that see the entry: the spec 8 role table. */
  roles: readonly UserRole[];
  /** False until the screen ships; entries that are not live are hidden (STORY-004). Set to true in the story that builds it. */
  live: boolean;
}

export interface NavSection {
  id: string;
  labelKey: string;
  items: readonly NavItem[];
}

const ALL_ROLES: readonly UserRole[] = ['SuperAdmin', 'CompanyAdmin', 'BranchManager', 'CounterStaff'];

/**
 * Sidebar structure from spec section 8:
 * Super Admin = companies only; Company Admin = setup and all reports; Branch Manager / Supervisor = branch setup,
 * live dashboard and branch reports; Counter Staff = counter console only.
 */
export const NAVIGATION: readonly NavSection[] = [
  {
    id: 'overview',
    labelKey: 'nav.sections.overview',
    items: [
      { id: 'home', labelKey: 'nav.home', route: '/home', roles: ALL_ROLES, live: true },
      {
        id: 'dashboard',
        labelKey: 'nav.dashboard',
        route: '/dashboard',
        roles: ['BranchManager'],
        live: false,
      },
    ],
  },
  {
    id: 'setup',
    labelKey: 'nav.sections.setup',
    items: [
      {
        id: 'companies',
        labelKey: 'nav.companies',
        route: '/admin/companies',
        roles: ['SuperAdmin'],
        live: false,
      },
      {
        id: 'service-types',
        labelKey: 'nav.serviceTypes',
        route: '/admin/service-types',
        roles: ['CompanyAdmin'],
        live: false,
      },
      {
        id: 'priority-types',
        labelKey: 'nav.priorityTypes',
        route: '/admin/priority-types',
        roles: ['CompanyAdmin'],
        live: false,
      },
      {
        id: 'service-mapping',
        labelKey: 'nav.serviceMapping',
        route: '/admin/service-mapping',
        roles: ['CompanyAdmin'],
        live: false,
      },
      {
        id: 'branches',
        labelKey: 'nav.branches',
        route: '/admin/branches',
        roles: ['CompanyAdmin', 'BranchManager'],
        live: false,
      },
      {
        id: 'users',
        labelKey: 'nav.users',
        route: '/admin/users',
        roles: ['CompanyAdmin'],
        live: false,
      },
      {
        id: 'templates',
        labelKey: 'nav.templates',
        route: '/admin/templates',
        roles: ['CompanyAdmin'],
        live: false,
      },
    ],
  },
  {
    id: 'operations',
    labelKey: 'nav.sections.operations',
    items: [
      {
        id: 'counter-console',
        labelKey: 'nav.counterConsole',
        route: '/counter',
        roles: ['CounterStaff'],
        live: false,
      },
      {
        id: 'reports',
        labelKey: 'nav.reports',
        route: '/reports',
        roles: ['CompanyAdmin', 'BranchManager'],
        live: false,
      },
    ],
  },
];

/** The sections a role sees: only live entries allowed for the role; sections left empty are dropped. */
export function navigationFor(
  role: UserRole | null | undefined,
  sections: readonly NavSection[] = NAVIGATION,
): NavSection[] {
  if (!role) {
    return [];
  }
  return sections
    .map((section) => ({
      ...section,
      items: section.items.filter((item) => item.live && item.roles.includes(role)),
    }))
    .filter((section) => section.items.length > 0);
}
