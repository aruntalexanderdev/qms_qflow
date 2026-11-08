import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { makeUser } from '../../../testing/auth-fakes';
import { provideTestTranslations } from '../../../testing/translations';
import { AuthService } from '../../core/auth/auth.service';
import { HomeComponent } from './home.component';

describe('HomeComponent', () => {
  it.each([
    ['en', 'Welcome, Arun Alexander'],
    ['ar', 'مرحبًا، Arun Alexander'],
  ])('shows the welcome title in %s', async (lang, expected) => {
    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [provideTestTranslations(), { provide: AuthService, useValue: { user: signal(makeUser()) } }],
    }).compileComponents();
    await firstValueFrom(TestBed.inject(TranslateService).use(lang));
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain(expected);
  });
});
