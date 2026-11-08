import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { provideTestTranslations } from '../../testing/translations';
import { LanguageService } from '../core/i18n/language.service';
import { LanguageSwitchComponent } from './language-switch.component';

describe('LanguageSwitchComponent', () => {
  it('does not clip the focus ring, has 44px targets and switches language', async () => {
    const choose = vi.fn();
    await TestBed.configureTestingModule({
      imports: [LanguageSwitchComponent],
      providers: [provideTestTranslations(), { provide: LanguageService, useValue: { current: () => 'en', choose } }],
    }).compileComponents();
    await firstValueFrom(TestBed.inject(TranslateService).use('en'));
    const fixture = TestBed.createComponent(LanguageSwitchComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const group = root.querySelector('.switch') as HTMLElement;
    expect(getComputedStyle(group).overflow).not.toBe('hidden');
    const buttons = root.querySelectorAll<HTMLButtonElement>('.switch__button');
    expect(getComputedStyle(buttons[0]).minBlockSize).toBe('var(--qms-size-control-height)');
    buttons[1].click();
    expect(choose).toHaveBeenCalledWith('ar');
  });
});
