import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { ProblemDetails } from '../../core/models/auth.models';
import { LanguageSelector } from '../../shared/language-selector/language-selector';
import { GOOGLE_CLIENT_ID } from '../../core/google-auth/google-auth';
import { GoogleSignInButton } from '../../shared/google-sign-in-button/google-sign-in-button';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, LanguageSelector, GoogleSignInButton],
  templateUrl: './login.html',
})
export class Login {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly showPassword = signal(false);
  protected readonly showGoogleButton = !!GOOGLE_CLIENT_ID;

  protected togglePasswordVisibility(): void {
    this.showPassword.update((visible) => !visible);
  }

  protected onGoogleCredential(idToken: string): void {
    this.errorKey.set(null);
    this.authService.loginWithGoogle(idToken).subscribe({
      next: (result) => {
        if ('requiresProfileCompletion' in result) {
          void this.router.navigateByUrl('/complete-profile');
        } else {
          void this.router.navigateByUrl('/dashboard');
        }
      },
      error: () => {
        this.errorKey.set('auth.login.googleError');
      },
    });
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorKey.set(null);

    this.authService.login(this.form.getRawValue()).subscribe({
      next: (result) => {
        this.submitting.set(false);
        if ('requiresTwoFactor' in result) {
          void this.router.navigateByUrl('/verify-2fa');
        } else {
          void this.router.navigateByUrl('/dashboard');
        }
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorKey.set(this.resolveErrorKey(error));
      },
    });
  }

  private resolveErrorKey(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'auth.login.networkError';
    }

    if (error.status === 429) {
      return 'auth.login.rateLimitedError';
    }

    if (error.status === 401) {
      const problem = error.error as ProblemDetails | undefined;
      const detail = problem?.detail?.toLowerCase() ?? '';
      return detail.includes('locked') ? 'auth.login.lockedOutError' : 'auth.login.genericError';
    }

    return 'auth.login.networkError';
  }
}
