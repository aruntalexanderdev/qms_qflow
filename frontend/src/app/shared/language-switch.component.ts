import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { Language } from '../core/api/models/auth.models';
import { LanguageService } from '../core/i18n/language.service';

/** English / Arabic switch. The labels are fixed: each language names itself. */
@Component({
  selector: 'qms-language-switch',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="switch" role="group" [attr.aria-label]="'language.label' | translate">
      @for (option of options; track option.code) {
        <button
          type="button"
          class="switch__button"
          [attr.lang]="option.code"
          [attr.aria-pressed]="language.current() === option.code"
          (click)="choose(option.code)"
        >
          {{ option.label }}
        </button>
      }
    </div>
  `,
  styleUrl: './language-switch.component.scss',
})
export class LanguageSwitchComponent {
  protected readonly language = inject(LanguageService);
  protected readonly options: readonly { code: Language; label: string }[] = [
    { code: 'en', label: 'English' },
    { code: 'ar', label: 'العربية' },
  ];

  protected choose(code: Language): void {
    void this.language.choose(code);
  }
}
