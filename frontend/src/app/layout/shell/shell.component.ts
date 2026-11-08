import { CdkTrapFocus } from '@angular/cdk/a11y';
import { BreakpointObserver } from '@angular/cdk/layout';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
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
  imports: [CdkTrapFocus, RouterOutlet, SidebarComponent, TopbarComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  host: { '(document:keydown.escape)': 'closeDrawer()' },
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);
  private readonly topbar = viewChild.required(TopbarComponent);
  private readonly sidebar = viewChild.required('sidebar', { read: ElementRef });

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

  constructor() {
    // Drawer focus: into the drawer when it opens, back to the menu button when it closes (Escape, backdrop, link).
    let wasOpen = false;
    effect(() => {
      const open = this.drawerVisible();
      untracked(() => {
        if (open) {
          afterNextRender(
            () => (this.sidebar().nativeElement as HTMLElement).querySelector<HTMLElement>('a.item')?.focus(),
            { injector: this.injector },
          );
        } else if (wasOpen) {
          // After the render that removes `inert`: an inert element cannot take focus.
          afterNextRender(() => this.topbar().focusMenuButton(), { injector: this.injector });
        }
        wasOpen = open;
      });
    });
  }

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
