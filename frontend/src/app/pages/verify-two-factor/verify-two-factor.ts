import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { LanguageSelector } from '../../shared/language-selector/language-selector';

/** US-17: the second half of a 2FA-gated login. Only reachable with a live challenge from
 * AuthService.login(); a direct/refreshed navigation here with no pending challenge
 * bounces back to /login, since there's nothing to verify. */
@Component({
  selector: 'app-verify-two-factor',
  imports: [ReactiveFormsModule, TranslatePipe, LanguageSelector],
  templateUrl: './verify-two-factor.html',
})
export class VerifyTwoFactor {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  constructor() {
    if (!this.authService.twoFactorChallenge()) {
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

    this.authService.verifyTwoFactor(this.form.getRawValue().code).subscribe({
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
    if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
      return 'auth.verifyTwoFactor.invalidCodeError';
    }
    return 'auth.verifyTwoFactor.networkError';
  }
}
