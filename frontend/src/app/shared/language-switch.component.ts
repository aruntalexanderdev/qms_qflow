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
  styles: `
    .switch {
      display: inline-flex;
      border: 1px solid var(--qms-neutral-border);
      border-radius: var(--qms-radius-md);
      background: var(--qms-neutral-surface);
      overflow: hidden;
    }

    .switch__button {
      min-block-size: var(--qms-size-control-height-sm);
      padding-inline: var(--qms-space-3);
      border: 0;
      background: transparent;
      color: var(--qms-neutral-text-label);
      font: inherit;
      font-size: var(--qms-font-size-sm);
      font-weight: var(--qms-font-weight-semibold);
      cursor: pointer;

      &:hover {
        background: var(--qms-neutral-surface-muted);
      }

      &[aria-pressed='true'] {
        background: var(--qms-brand-primary-subtle);
        color: var(--qms-brand-on-primary-subtle);
      }
    }
  `,
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
