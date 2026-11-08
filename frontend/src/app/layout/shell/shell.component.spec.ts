import { BreakpointObserver } from '@angular/cdk/layout';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { makeUser } from '../../../testing/auth-fakes';
import { ARABIC, ENGLISH, fakeLanguageService } from '../../../testing/language-fakes';
import { provideTestTranslations } from '../../../testing/translations';
import { UserRole } from '../../core/api/models/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import { NotificationService } from '../../core/notifications/notification.service';
import { ShellComponent } from './shell.component';

describe('ShellComponent', () => {
  let fixture: ComponentFixture<ShellComponent>;
  const user = signal(makeUser());
  const auth = { user, logout: vi.fn().mockResolvedValue(undefined) };
  const notifications = { error: vi.fn() };

  async function open(role: UserRole, language: 'en' | 'ar' = 'en', drawer = false): Promise<HTMLElement> {
    user.set(makeUser(role, { name: role === 'SuperAdmin' ? 'Sam Super' : 'Arun Alexander' }));
    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideTestTranslations(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: NotificationService, useValue: notifications },
        { provide: BreakpointObserver, useValue: { observe: () => of({ matches: drawer, breakpoints: {} }) } },
        { provide: LanguageService, useValue: fakeLanguageService([ENGLISH, ARABIC], language) },
      ],
    }).compileComponents();
    await firstValueFrom(TestBed.inject(TranslateService).use(language));
    fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => vi.clearAllMocks());

  it('Company Admin: initials, name and the translated role label in the top bar', async () => {
    const el = await open('CompanyAdmin');
    expect(el.querySelector('.user__avatar')?.textContent?.trim()).toBe('AA');
    expect(el.querySelector('.user__name')?.textContent).toContain('Arun Alexander');
    expect(el.querySelector('.user__role')?.textContent).toContain('Company Admin');
  });

  it('Super Admin shell: role label, only Home in the nav, no company or Company Admin entries', async () => {
    const el = await open('SuperAdmin');
    expect(el.querySelector('.user__role')?.textContent).toContain('Super Admin');
    const links = Array.from(el.querySelectorAll('qms-sidebar a.item')).map((a) => a.textContent?.trim());
    expect(links).toEqual(['Home']);
    expect(el.textContent).not.toContain('Service types');
    expect(el.textContent).not.toContain('Counter console');
  });

  it('shows the Arabic role label in Arabic', async () => {
    const el = await open('CompanyAdmin', 'ar');
    expect(el.querySelector('.user__role')?.textContent).toContain('مدير الشركة');
  });

  it('Sign out calls logout once', async () => {
    const el = await open('CompanyAdmin');
    el.querySelector<HTMLButtonElement>('button.sign-out')!.click();
    await fixture.whenStable();
    expect(auth.logout).toHaveBeenCalledTimes(1);
  });

  it('a failed sign-out stays signed in and shows the error', async () => {
    auth.logout.mockRejectedValueOnce(new Error('x'));
    const el = await open('CompanyAdmin');
    el.querySelector<HTMLButtonElement>('button.sign-out')!.click();
    await fixture.whenStable();
    expect(notifications.error).toHaveBeenCalled();
  });

  it('the menu button toggles the sidebar state', async () => {
    const el = await open('CompanyAdmin');
    const button = el.querySelector<HTMLButtonElement>('.menu-button')!;
    expect(button.getAttribute('aria-expanded')).toBe('true');
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(el.querySelector('.shell--collapsed')).not.toBeNull();
  });

  it('drawer: focus moves into the nav, background is inert, Escape closes and returns focus to the menu button', async () => {
    const el = await open('CompanyAdmin', 'en', true);
    document.body.appendChild(el);
    const button = el.querySelector<HTMLButtonElement>('.menu-button')!;
    button.focus();
    button.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(el.querySelector('.shell--drawer-open')).not.toBeNull();
    expect(el.querySelector('.shell__column')?.hasAttribute('inert')).toBe(true);
    expect(document.activeElement?.classList.contains('item')).toBe(true);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('.shell__column')?.hasAttribute('inert')).toBe(false);
    expect(document.activeElement).toBe(button);
    el.remove();
  });

  it('uses header, aside and main landmarks', async () => {
    const el = await open('CompanyAdmin');
    expect(el.querySelector('header qms-topbar')).not.toBeNull();
    expect(el.querySelector('aside qms-sidebar')).not.toBeNull();
    expect(el.querySelector('main')).not.toBeNull();
  });
});
