import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { ProblemDetails } from '../../core/models/auth.models';
import { LanguageSelector } from '../../shared/language-selector/language-selector';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, LanguageSelector],
  templateUrl: './register.html',
})
export class Register {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = this.fb.nonNullable.group({
    firstName: ['', [Validators.required]],
    lastName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(6)]],
    phoneNumber: [''],
    address: [''],
    workspaceName: ['', [Validators.required]],
    portfolioSize: [1, [Validators.required, Validators.min(1)]],
    agreedToTerms: [false, [Validators.requiredTrue]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

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
      .register({
        firstName: raw.firstName,
        lastName: raw.lastName,
        email: raw.email,
        password: raw.password,
        phoneNumber: raw.phoneNumber || null,
        address: raw.address || null,
        workspaceName: raw.workspaceName,
        portfolioSize: raw.portfolioSize,
        agreedToTerms: raw.agreedToTerms,
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
      return 'auth.register.networkError';
    }

    if (error.status === 429) {
      return 'auth.register.rateLimitedError';
    }

    if (error.status === 400) {
      const problem = error.error as ProblemDetails | undefined;
      const fields = problem?.errors ? Object.keys(problem.errors) : [];
      if (fields.some((f) => f.toLowerCase() === 'email')) {
        return 'auth.register.emailTakenError';
      }
      return 'auth.register.validationError';
    }

    return 'auth.register.networkError';
  }
}
