import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth/auth.service';
import { PageHeaderComponent } from '../../layout/page-header/page-header.component';

/** Welcome placeholder, open to every signed-in role (STORY-004). It carries no data. */
@Component({
  selector: 'qms-home',
  imports: [PageHeaderComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <qms-page-header
      [title]="'home.welcome' | translate: { name: auth.user()?.name }"
      [description]="'home.description' | translate"
    />
  `,
})
export class HomeComponent {
  protected readonly auth = inject(AuthService);
}
