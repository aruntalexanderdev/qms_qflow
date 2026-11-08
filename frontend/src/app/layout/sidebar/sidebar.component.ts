import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { UserRole } from '../../core/api/models/auth.models';
import { navigationFor } from '../../core/navigation/navigation';

/** Dark teal sidebar: brand block and the navigation sections of the signed-in role (spec section 8). */
@Component({
  selector: 'qms-sidebar',
  imports: [RouterLink, RouterLinkActive, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  readonly role = input<UserRole | null>(null);
  readonly navigated = output<void>();

  protected readonly sections = computed(() => navigationFor(this.role()));
}
