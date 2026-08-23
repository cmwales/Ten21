import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { LanguageSelector } from '../../shared/language-selector/language-selector';

/**
 * US-15: the second half of a first-time Google signup -- collects the workspace fields
 * Google Sign-In doesn't (phone, address, workspace name, portfolio size). Only reachable
 * with a live interim token from AuthService.loginWithGoogle(); a direct/refreshed
 * navigation here with no interim token bounces back to /login, since there's nothing to
 * complete.
 */
@Component({
  selector: 'app-complete-profile',
  imports: [ReactiveFormsModule, TranslatePipe, LanguageSelector],
  templateUrl: './complete-profile.html',
})
export class CompleteProfile {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly hasInterimToken = this.authService.interimToken;

  protected readonly form = this.fb.nonNullable.group({
    phoneNumber: [''],
    address: [''],
    workspaceName: ['', [Validators.required]],
    portfolioSize: [1, [Validators.required, Validators.min(1)]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  constructor() {
    if (!this.authService.interimToken()) {
      void this.router.navigateByUrl('/login');
    }
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

    const raw = this.form.getRawValue();
    this.authService
      .completeProfile({
        phoneNumber: raw.phoneNumber || null,
        address: raw.address || null,
        workspaceName: raw.workspaceName,
        portfolioSize: raw.portfolioSize,
      })
      .subscribe({
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
    if (!(error instanceof HttpErrorResponse)) {
      return 'auth.completeProfile.networkError';
    }
    if (error.status === 401 || error.status === 403) {
      return 'auth.completeProfile.expiredError';
    }
    return 'auth.completeProfile.networkError';
  }
}
