import { HttpErrorResponse } from '@angular/common/http';
import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { ProblemDetails } from '../../core/models/auth.models';
import { LanguageSelector } from '../../shared/language-selector/language-selector';
import { TURNSTILE_SITE_KEY, turnstileReady } from '../../core/turnstile/turnstile';

/** Group-level (not confirmPassword-level) so it doesn't fight with confirmPassword's own
 * Validators.required for error precedence -- the mismatch error is read off the form via
 * form.errors, not confirmPassword.errors, in the template. */
function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  return password === confirmPassword ? null : { passwordMismatch: true };
}

/** Formats as the user types: (555) 123-4567. Non-digit characters are stripped and the
 * result is capped at 10 digits -- deliberately plain TypeScript rather than pulling in a
 * masking library for one field. */
function formatPhoneNumber(value: string): string {
  const digits = value.replace(/\D/g, '').slice(0, 10);

  if (digits.length === 0) {
    return '';
  }
  if (digits.length < 4) {
    return `(${digits}`;
  }
  if (digits.length < 7) {
    return `(${digits.slice(0, 3)}) ${digits.slice(3)}`;
  }
  return `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6)}`;
}

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, LanguageSelector],
  templateUrl: './register.html',
})
export class Register implements AfterViewInit, OnDestroy {
  @ViewChild('turnstileContainer') private turnstileContainer?: ElementRef<HTMLDivElement>;

  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private turnstileWidgetId: string | null = null;

  protected readonly form = this.fb.nonNullable.group(
    {
      firstName: ['', [Validators.required]],
      lastName: ['', [Validators.required]],
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      confirmPassword: ['', [Validators.required]],
      phoneNumber: [''],
      address: [''],
      workspaceName: ['', [Validators.required]],
      portfolioSize: [1, [Validators.required, Validators.min(1)]],
      agreedToTerms: [false, [Validators.requiredTrue]],
    },
    { validators: [passwordsMatchValidator] },
  );

  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly turnstileToken = signal<string | null>(null);

  protected onPhoneInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.form.controls.phoneNumber.setValue(formatPhoneNumber(input.value));
  }

  async ngAfterViewInit(): Promise<void> {
    await turnstileReady();
    if (!this.turnstileContainer || !window.turnstile) {
      return;
    }
    this.turnstileWidgetId = window.turnstile.render(this.turnstileContainer.nativeElement, {
      sitekey: TURNSTILE_SITE_KEY,
      action: 'register',
      callback: (token) => this.turnstileToken.set(token),
      'expired-callback': () => this.turnstileToken.set(null),
      'error-callback': () => this.turnstileToken.set(null),
    });
  }

  ngOnDestroy(): void {
    if (this.turnstileWidgetId) {
      window.turnstile?.remove(this.turnstileWidgetId);
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

    const turnstileToken = this.turnstileToken();
    if (!turnstileToken) {
      this.errorKey.set('auth.register.turnstileRequired');
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
        turnstileToken,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          void this.router.navigateByUrl('/dashboard');
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          this.errorKey.set(this.resolveErrorKey(error));
          // Turnstile tokens are single-use -- force a fresh solve before retrying.
          this.turnstileToken.set(null);
          if (this.turnstileWidgetId) {
            window.turnstile?.reset(this.turnstileWidgetId);
          }
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
      if (fields.some((f) => f.toLowerCase() === 'turnstiletoken')) {
        return 'auth.register.turnstileFailedError';
      }
      return 'auth.register.validationError';
    }

    return 'auth.register.networkError';
  }
}
