import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { ARABIC, ENGLISH, fakeLanguageService } from '../../testing/language-fakes';
import { provideTestTranslations } from '../../testing/translations';
import { LanguageService } from '../core/i18n/language.service';
import { LanguageSwitchComponent } from './language-switch.component';

async function render(fake: ReturnType<typeof fakeLanguageService>) {
  await TestBed.configureTestingModule({
    imports: [LanguageSwitchComponent],
    providers: [provideTestTranslations(), { provide: LanguageService, useValue: fake }],
  }).compileComponents();
  await firstValueFrom(TestBed.inject(TranslateService).use('en'));
  const fixture = TestBed.createComponent(LanguageSwitchComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('LanguageSwitchComponent', () => {
  it('does not clip the focus ring, has 44px targets and switches language', async () => {
    const fake = fakeLanguageService([ENGLISH, ARABIC]);
    const root = await render(fake);
    const group = root.querySelector('.switch') as HTMLElement;
    expect(getComputedStyle(group).overflow).not.toBe('hidden');
    const buttons = root.querySelectorAll<HTMLButtonElement>('.switch__button');
    expect(getComputedStyle(buttons[0]).minBlockSize).toBe('var(--qms-size-control-height)');
    buttons[1].click();
    expect(fake.choose).toHaveBeenCalledWith('ar');
  });

  it('labels the buttons by native name, with lang and aria-pressed', async () => {
    const root = await render(fakeLanguageService([ENGLISH, ARABIC], 'ar'));
    const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('.switch__button'));
    expect(buttons.map((b) => b.textContent?.trim())).toEqual(['English', 'العربية']);
    expect(buttons.map((b) => b.getAttribute('lang'))).toEqual(['en', 'ar']);
    expect(buttons.map((b) => b.getAttribute('aria-pressed'))).toEqual(['false', 'true']);
  });

  it('renders nothing with one offered language (phase 1)', async () => {
    const root = await render(fakeLanguageService([ENGLISH]));
    expect(root.querySelector('.switch')).toBeNull();
    expect(root.querySelector('button')).toBeNull();
  });
});
