import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { map } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { TopbarComponent } from '../topbar/topbar.component';

/** Below this width the sidebar is a drawer (DESIGN_SYSTEM.md section 5, breakpoint lg). */
const DRAWER_QUERY = '(max-width: 1023.98px)';

/** Authenticated layout: sidebar, top bar and the routed page. Mirrors in RTL through logical properties only. */
@Component({
  selector: 'qms-shell',
  imports: [RouterOutlet, SidebarComponent, TopbarComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  host: { '(document:keydown.escape)': 'closeDrawer()' },
})
export class ShellComponent {
  private readonly auth = inject(AuthService);

  protected readonly role = computed(() => this.auth.user()?.role ?? null);
  protected readonly isDrawer = toSignal(
    inject(BreakpointObserver)
      .observe(DRAWER_QUERY)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  private readonly drawerOpen = signal(false);
  protected readonly collapsed = signal(false);

  protected readonly drawerVisible = computed(() => this.isDrawer() && this.drawerOpen());
  protected readonly navExpanded = computed(() =>
    this.isDrawer() ? this.drawerOpen() : !this.collapsed(),
  );

  protected toggleNav(): void {
    if (this.isDrawer()) {
      this.drawerOpen.update((open) => !open);
    } else {
      this.collapsed.update((value) => !value);
    }
  }

  protected closeDrawer(): void {
    this.drawerOpen.set(false);
  }
}
