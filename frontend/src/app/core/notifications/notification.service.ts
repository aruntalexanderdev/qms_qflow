import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { ApiError } from '../errors/api-error';

/** Toasts (DESIGN_SYSTEM.md section 6): top end corner, 5 seconds. Text is always translated. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  error(error: ApiError): void {
    let message = this.translate.instant(error.messageKey, error.params) as string;
    if (error.correlationId) {
      message += ` (${this.translate.instant('errors.reference') as string}: ${error.correlationId})`;
    }
    this.show(message);
  }

  show(message: string): void {
    this.snackBar.open(message, this.translate.instant('shell.close') as string, {
      duration: 5000,
      horizontalPosition: 'end',
      verticalPosition: 'top',
      panelClass: 'qms-toast',
    });
  }
}
