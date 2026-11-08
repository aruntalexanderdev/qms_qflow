import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth/auth.service';
import { mapHttpError } from '../../core/errors/api-error';
import { NotificationService } from '../../core/notifications/notification.service';
import { LanguageSwitchComponent } from '../../shared/language-switch.component';
import { initialsOf } from '../../shared/utils/initials';

/** White 64px top bar: menu toggle (start), language switch, avatar, name, role label and Sign out (end). */
@Component({
  selector: 'qms-topbar',
  imports: [MatButtonModule, TranslatePipe, LanguageSwitchComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent {
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  readonly navExpanded = input(false);
  readonly menuToggle = output<void>();

  protected readonly user = this.auth.user;
  protected readonly initials = computed(() => initialsOf(this.user()?.name));
  protected readonly signingOut = signal(false);

  protected async signOut(): Promise<void> {
    this.signingOut.set(true);
    try {
      await this.auth.logout();
    } catch (error) {
      // The server could not revoke the session, so stay signed in and say why.
      this.notifications.error(mapHttpError(error));
    } finally {
      this.signingOut.set(false);
    }
  }
}
