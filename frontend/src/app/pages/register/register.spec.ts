import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { Register } from './register';

describe('Register', () => {
  function createComponent(): Register {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: Router, useValue: { navigateByUrl: vi.fn() } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
      ],
    });
    return TestBed.createComponent(Register).componentInstance;
  }

  it('flags a form-level passwordMismatch error when the two password fields differ', () => {
    const component = createComponent();
    component['form'].patchValue({ password: 'Correct-Passw0rd!', confirmPassword: 'Different-Passw0rd!' });

    expect(component['form'].errors?.['passwordMismatch']).toBe(true);
  });

  it('clears the passwordMismatch error once both fields match', () => {
    const component = createComponent();
    component['form'].patchValue({ password: 'Correct-Passw0rd!', confirmPassword: 'Different-Passw0rd!' });
    expect(component['form'].errors?.['passwordMismatch']).toBe(true);

    component['form'].patchValue({ confirmPassword: 'Correct-Passw0rd!' });
    expect(component['form'].errors?.['passwordMismatch']).toBeUndefined();
  });

  it('formats a phone number as (XXX) XXX-XXXX while typing', () => {
    const component = createComponent();
    const input = { value: '8015551212' } as HTMLInputElement;

    component['onPhoneInput']({ target: input } as unknown as Event);

    expect(component['form'].controls.phoneNumber.value).toBe('(801) 555-1212');
  });

  it('formats a partially-typed phone number progressively', () => {
    const component = createComponent();

    component['onPhoneInput']({ target: { value: '80' } } as unknown as Event);
    expect(component['form'].controls.phoneNumber.value).toBe('(80');

    component['onPhoneInput']({ target: { value: '801555' } } as unknown as Event);
    expect(component['form'].controls.phoneNumber.value).toBe('(801) 555');
  });

  it('strips non-digit characters and caps at 10 digits', () => {
    const component = createComponent();
    const input = { value: '1-801-555-1212 ext 99' } as HTMLInputElement;

    component['onPhoneInput']({ target: input } as unknown as Event);

    expect(component['form'].controls.phoneNumber.value).toBe('(180) 155-5121');
  });
});
