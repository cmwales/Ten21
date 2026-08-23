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
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { ProblemDetails } from '../../core/models/auth.models';
import { LanguageSelector } from '../../shared/language-selector/language-selector';
import { TURNSTILE_SITE_KEY, turnstileReady } from '../../core/turnstile/turnstile';

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
  protected readonly turnstileToken = signal<string | null>(null);

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
