import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { LanguagesResponse } from './models/language.models';

/**
 * The languages offered to the caller: anonymous or Super Admin = the languages the release makes available, a company
 * user = the languages the company enabled. Anonymous calls are allowed; once signed in the interceptor adds the token.
 */
@Injectable({ providedIn: 'root' })
export class LanguagesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<LanguagesResponse> {
    return this.http.get<LanguagesResponse>('/api/v1/languages');
  }
}
