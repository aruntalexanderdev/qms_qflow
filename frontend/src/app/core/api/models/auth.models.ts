/** Contract of the STORY-003 auth endpoints (story STORY-004 section 7). JSON is camelCase, ids are uuid strings. */

export type UserRole = 'SuperAdmin' | 'CompanyAdmin' | 'BranchManager' | 'CounterStaff';

export interface CurrentUser {
  id: string;
  userName: string;
  /** Display name. */
  name: string;
  role: UserRole;
  /** Null for the Super Admin. */
  companyId: string | null;
  /** Preferred language code of the user; applied only when the offered list contains it (ADR-007 amendment 1). */
  language: string;
  branchIds: string[];
  counterIds: string[];
}

export interface LoginRequest {
  userName: string;
  password: string;
}

export interface AuthResponse {
  accessToken: string;
  tokenType: 'Bearer';
  /** Seconds until the access token expires, counted from the moment the response arrives. */
  expiresIn: number;
  /** Server clock; informational only, the app uses expiresIn (ADR-007 A.6). */
  expiresAt: string;
  user: CurrentUser;
}
