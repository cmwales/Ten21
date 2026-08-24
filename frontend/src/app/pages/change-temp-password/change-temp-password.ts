import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { LanguageSelector } from '../../shared/language-selector/language-selector';

/** Same group-level match validator as Register, so the mismatch error doesn't fight with
 * confirmPassword's own Validators.required for precedence. */
function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const newPassword = control.get('newPassword')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  return newPassword === confirmPassword ? null : { passwordMismatch: true };
}

/** US-24: the second half of a MustChangePassword-gated login -- reached only after login()
 * returns PasswordChangeRequiredResponse (e.g. a resident's first sign-in with the temporary
 * password from their welcome email). Only reachable with a live challenge; a direct/
 * refreshed navigation here with no pending challenge bounces back to /login, same pattern
 * as VerifyTwoFactor. */
@Component({
  selector: 'app-change-temp-password',
  imports: [ReactiveFormsModule, TranslatePipe, LanguageSelector],
  templateUrl: './change-temp-password.html',
})
export class ChangeTempPassword {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = this.fb.nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(6)]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: [passwordsMatchValidator] },
  );

  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  constructor() {
    if (!this.authService.passwordChangeChallenge()) {
      void this.router.navigateByUrl('/login');
    }
  }

  protected submit(): void {
    if (this.submitting() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorKey.set(null);

    this.authService.changeTempPassword(this.form.getRawValue().newPassword).subscribe({
      next: () => {
        this.submitting.set(false);
        void this.router.navigateByUrl('/dashboard');
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorKey.set(this.resolveErrorKey(error));
      },
    });
  }

  private resolveErrorKey(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 400) {
      return 'auth.changeTempPassword.validationError';
    }
    if (error instanceof HttpErrorResponse && error.status === 403) {
      return 'auth.changeTempPassword.expiredChallengeError';
    }
    return 'auth.changeTempPassword.networkError';
  }
}
