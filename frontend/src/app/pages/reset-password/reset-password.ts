import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { LanguageSelector } from '../../shared/language-selector/language-selector';

/** US-16: the landing page for a password-reset link's URL (?userId=&token=). */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, LanguageSelector],
  templateUrl: './reset-password.html',
})
export class ResetPassword {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);

  private readonly userId = this.route.snapshot.queryParamMap.get('userId');
  private readonly token = this.route.snapshot.queryParamMap.get('token');
  protected readonly linkIsValid = !!this.userId && !!this.token;

  protected readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(6)]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly succeeded = signal(false);

  protected submit(): void {
    if (this.submitting() || this.form.invalid || !this.userId || !this.token) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorKey.set(null);

    this.authService.resetPassword(this.userId, this.token, this.form.getRawValue().newPassword).subscribe({
      next: () => {
        this.submitting.set(false);
        this.succeeded.set(true);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorKey.set(this.resolveErrorKey(error));
      },
    });
  }

  protected goToLogin(): void {
    void this.router.navigateByUrl('/login');
  }

  private resolveErrorKey(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 400) {
      return 'auth.resetPassword.invalidLinkError';
    }
    return 'auth.resetPassword.networkError';
  }
}
