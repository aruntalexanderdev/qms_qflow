import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Page title (28px bold) with a one-line muted description (DESIGN_SYSTEM.md section 5). */
@Component({
  selector: 'qms-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="page-header">
      <h1 class="page-header__title">{{ title() }}</h1>
      @if (description()) {
        <p class="page-header__description">{{ description() }}</p>
      }
    </header>
  `,
  styleUrl: './page-header.component.scss',
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly description = input<string>('');
}
