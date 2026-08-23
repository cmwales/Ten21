import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { TotpSetupResponse } from '../../core/models/auth.models';

/**
 * US-17: TOTP authenticator-app enrollment, available to any role. There's no
 * GET-current-status endpoint yet (US-17's own scope stopped short of one), so this page
 * doesn't claim to know whether TOTP is already enabled -- it just offers both actions
 * (set up / disable) with their own clear success feedback, rather than showing a status
 * indicator it can't honestly back up.
 */
@Component({
  selector: 'app-security',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe],
  templateUrl: './security.html',
})
export class Security {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);

  protected readonly setupData = signal<TotpSetupResponse | null>(null);
  protected readonly settingUp = signal(false);
  protected readonly setupErrorKey = signal<string | null>(null);
  protected readonly enableSucceeded = signal(false);

  protected readonly disabling = signal(false);
  protected readonly disableErrorKey = signal<string | null>(null);
  protected readonly disableSucceeded = signal(false);

  protected readonly enableForm = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
  });

  protected beginSetup(): void {
    this.settingUp.set(true);
    this.setupErrorKey.set(null);
    this.enableSucceeded.set(false);

    this.authService.setupTotp().subscribe({
      next: (data) => {
        this.settingUp.set(false);
        this.setupData.set(data);
      },
      error: () => {
        this.settingUp.set(false);
        this.setupErrorKey.set('auth.security.setupError');
      },
    });
  }

  protected confirmEnable(): void {
    if (this.enableForm.invalid) {
      this.enableForm.markAllAsTouched();
      return;
    }

    this.setupErrorKey.set(null);
    this.authService.enableTotp(this.enableForm.getRawValue().code).subscribe({
      next: () => {
        this.enableSucceeded.set(true);
        this.setupData.set(null);
        this.enableForm.reset();
      },
      error: (error: unknown) => {
        this.setupErrorKey.set(
          error instanceof HttpErrorResponse && error.status === 400
            ? 'auth.security.wrongCodeError'
            : 'auth.security.setupError',
        );
      },
    });
  }

  protected disable(): void {
    this.disabling.set(true);
    this.disableErrorKey.set(null);
    this.disableSucceeded.set(false);

    this.authService.disableTotp().subscribe({
      next: () => {
        this.disabling.set(false);
        this.disableSucceeded.set(true);
      },
      error: () => {
        this.disabling.set(false);
        this.disableErrorKey.set('auth.security.disableError');
      },
    });
  }
}
