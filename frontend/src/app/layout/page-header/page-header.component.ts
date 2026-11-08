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
  styles: `
    .page-header {
      margin-block-end: var(--qms-space-6);
    }

    .page-header__title {
      margin: 0 0 var(--qms-space-2);
      font-size: var(--qms-font-size-3xl);
      font-weight: var(--qms-font-weight-bold);
      line-height: var(--qms-font-line-height-tight);
      letter-spacing: var(--qms-font-letter-spacing-heading);
    }

    .page-header__description {
      margin: 0;
      color: var(--qms-neutral-text-muted);
    }
  `,
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly description = input<string>('');
}
