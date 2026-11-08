import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { HOME_ROUTE } from '../../../core/auth/auth.guards';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiError, mapHttpError } from '../../../core/errors/api-error';
import { LanguageService } from '../../../core/i18n/language.service';
import { LanguageSwitchComponent } from '../../../shared/language-switch.component';

const MAX_LENGTH = 256;

/** Sign-in page: username and password, language choice, translated problem-details errors by `code`. */
@Component({
  selector: 'qms-login',
  imports: [ReactiveFormsModule, MatButtonModule, TranslatePipe, LanguageSwitchComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly language = inject(LanguageService);

  protected readonly form = new FormGroup({
    userName: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_LENGTH)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_LENGTH)],
    }),
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  /** Why the previous session ended (`auth.refresh_reused`), shown once. Nothing is shown for a plain sign-out. */
  protected readonly sessionMessageKey = signal<string | null>(null);

  constructor() {
    const reason = this.auth.endedReason();
    if (reason === 'auth.refresh_reused') {
      this.sessionMessageKey.set(`errors.${reason}`);
      this.auth.clearEndedReason();
    }
    // A session adopted from another tab (same cookie) also leaves this page.
    effect(() => {
      if (this.auth.isAuthenticated()) {
        void this.router.navigateByUrl(HOME_ROUTE);
      }
    });
    inject(DestroyRef).onDestroy(() => this.auth.clearEndedReason());
  }

  protected fieldError(name: 'userName' | 'password'): string | null {
    const control = this.form.controls[name];
    if (!control.touched && !control.dirty) {
      return null;
    }
    if (control.hasError('required')) {
      return name === 'userName' ? 'auth.userNameRequired' : 'auth.passwordRequired';
    }
    if (control.hasError('maxlength')) {
      return 'auth.tooLong';
    }
    if (control.hasError('server')) {
      return 'errors.validation.field_invalid';
    }
    return null;
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    this.sessionMessageKey.set(null);
    try {
      await this.auth.login(this.form.getRawValue());
      await this.router.navigateByUrl(HOME_ROUTE);
    } catch (caught) {
      const failure = mapHttpError(caught);
      this.error.set(failure);
      this.form.controls.password.reset('');
      for (const field of Object.keys(failure.fieldErrors)) {
        const control = this.form.get(field);
        control?.setErrors({ server: true });
        control?.markAsTouched();
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
