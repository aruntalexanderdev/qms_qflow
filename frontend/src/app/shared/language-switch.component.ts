import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { LanguageService } from '../core/i18n/language.service';

/**
 * Language switch. It lists the languages offered by the API by their native name (never translated) and renders
 * nothing when only one language is offered (phase 1: English only).
 */
@Component({
  selector: 'qms-language-switch',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (language.canSwitch()) {
      <div class="switch" role="group" [attr.aria-label]="'language.label' | translate">
        @for (option of language.offered(); track option.code) {
          <button
            type="button"
            class="switch__button"
            [attr.lang]="option.code"
            [attr.aria-pressed]="language.current() === option.code"
            (click)="choose(option.code)"
          >
            {{ option.nativeName }}
          </button>
        }
      </div>
    }
  `,
  styleUrl: './language-switch.component.scss',
})
export class LanguageSwitchComponent {
  protected readonly language = inject(LanguageService);

  protected choose(code: string): void {
    void this.language.choose(code);
  }
}
