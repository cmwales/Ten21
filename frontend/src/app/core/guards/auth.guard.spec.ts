import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

describe('authGuard', () => {
  function runGuard(isAuthenticated: boolean) {
    const authServiceStub = { isAuthenticated: () => isAuthenticated };
    const routerStub = { createUrlTree: (commands: unknown[]) => ({ commands }) };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceStub },
        { provide: Router, useValue: routerStub },
      ],
    });

    return TestBed.runInInjectionContext(() =>
      authGuard(null as never, null as never),
    );
  }

  it('allows navigation when a valid session exists', () => {
    expect(runGuard(true)).toBe(true);
  });

  it('redirects to /login when there is no valid session', () => {
    const result = runGuard(false) as unknown as { commands: unknown[] };
    expect(result.commands).toEqual(['/login']);
  });
});
