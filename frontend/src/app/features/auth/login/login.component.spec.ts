import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { ARABIC, ENGLISH, fakeLanguageService } from '../../../../testing/language-fakes';
import { provideTestTranslations } from '../../../../testing/translations';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  const endedReason = signal<string | null>(null);
  const isAuthenticated = signal(false);
  const auth = {
    login: vi.fn(),
    endedReason,
    isAuthenticated,
    clearEndedReason: vi.fn(() => endedReason.set(null)),
  };
  const router = { navigateByUrl: vi.fn().mockResolvedValue(true) };
  let language = fakeLanguageService([ENGLISH, ARABIC]);

  const el = () => fixture.nativeElement as HTMLElement;
  const input = (id: string) => el().querySelector<HTMLInputElement>(`#${id}`)!;

  async function setup(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideTestTranslations(),
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router },
        { provide: LanguageService, useFactory: () => language },
      ],
    }).compileComponents();
    await firstValueFrom(TestBed.inject(TranslateService).use('en'));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
  }

  async function submit(userName = 'admin', password = 'wrong'): Promise<void> {
    input('login-user-name').value = userName;
    input('login-user-name').dispatchEvent(new Event('input'));
    input('login-password').value = password;
    input('login-password').dispatchEvent(new Event('input'));
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    vi.clearAllMocks();
    language = fakeLanguageService([ENGLISH, ARABIC]);
    endedReason.set(null);
    isAuthenticated.set(false);
  });

  it('labels both inputs and sends { userName, password } then opens /home', async () => {
    auth.login.mockResolvedValue({});
    await setup();
    expect(el().querySelector('label[for="login-user-name"]')?.textContent).toContain('Username');
    expect(el().querySelector('label[for="login-password"]')?.textContent).toContain('Password');
    await submit('admin', 'Secret1!');
    expect(auth.login).toHaveBeenCalledWith({ userName: 'admin', password: 'Secret1!' });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/home');
  });

  it('does not call the API when the form is empty and shows field messages', async () => {
    await setup();
    await submit('', '');
    expect(auth.login).not.toHaveBeenCalled();
    expect(el().textContent).toContain('Enter your username');
    expect(el().textContent).toContain('Enter your password');
  });

  it('wrong credentials: translated message, password cleared', async () => {
    auth.login.mockRejectedValue(
      new HttpErrorResponse({ status: 401, error: { code: 'auth.invalid_credentials' } }),
    );
    await setup();
    await submit();
    expect(el().querySelector('.qms-alert')?.textContent).toContain('Incorrect username or password');
    expect(input('login-password').value).toBe('');
    expect(input('login-user-name').value).toBe('admin');
  });

  it('locked out shows the locked text and not the invalid-credentials text', async () => {
    auth.login.mockRejectedValue(new HttpErrorResponse({ status: 401, error: { code: 'auth.locked_out' } }));
    await setup();
    await submit();
    const text = el().querySelector('.qms-alert')?.textContent ?? '';
    expect(text).toContain('locked');
    expect(text).not.toContain('Incorrect username');
  });

  it('429 shows the minutes from Retry-After', async () => {
    auth.login.mockRejectedValue(
      new HttpErrorResponse({
        status: 429,
        error: { code: 'request.rate_limited' },
        headers: new HttpHeaders({ 'Retry-After': '120' }),
      }),
    );
    await setup();
    await submit();
    expect(el().querySelector('.qms-alert')?.textContent).toContain('Try again in 2 minutes');
  });

  it('network error shows errors.network', async () => {
    auth.login.mockRejectedValue(new HttpErrorResponse({ status: 0 }));
    await setup();
    await submit();
    expect(el().querySelector('.qms-alert')?.textContent).toContain('Cannot reach the server');
  });

  it('server validation errors mark the matching field', async () => {
    auth.login.mockRejectedValue(
      new HttpErrorResponse({ status: 400, error: { code: 'validation.failed', errors: { userName: ['too long'] } } }),
    );
    await setup();
    await submit();
    expect(input('login-user-name').getAttribute('aria-invalid')).toBe('true');
    expect(el().textContent).toContain('Check this field');
  });

  it('shows the refresh_reused message once, and nothing for a plain sign-out', async () => {
    endedReason.set('auth.refresh_reused');
    await setup();
    expect(el().querySelector('.qms-alert')?.textContent).toContain('Your session ended');
    expect(auth.clearEndedReason).toHaveBeenCalled();
  });

  it('shows no message when opened signed out normally', async () => {
    await setup();
    expect(el().querySelector('.qms-alert')).toBeNull();
  });

  it('lists the offered languages by native name when there are two', async () => {
    await setup();
    const labels = Array.from(el().querySelectorAll('qms-language-switch button')).map((b) => b.textContent?.trim());
    expect(labels).toEqual(['English', 'العربية']);
  });

  it('renders no switch element at all when only English is offered (phase 1)', async () => {
    language = fakeLanguageService([ENGLISH]);
    await setup();
    expect(el().querySelector('qms-language-switch')).toBeNull();
    expect(el().querySelector('.login__language')).toBeNull();
  });
});
