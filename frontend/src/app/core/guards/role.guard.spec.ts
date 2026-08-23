import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { denyRolesGuard } from './role.guard';
import { AuthService } from '../services/auth.service';
import { RoleNames } from '../constants/roles';

describe('denyRolesGuard', () => {
  function runGuard(isAuthenticated: boolean, role: string | null) {
    const authServiceStub = { isAuthenticated: () => isAuthenticated, role: () => role };
    const routerStub = { createUrlTree: (commands: unknown[]) => ({ commands }) };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceStub },
        { provide: Router, useValue: routerStub },
      ],
    });

    const guard = denyRolesGuard([RoleNames.Tenant]);
    return TestBed.runInInjectionContext(() => guard(null as never, null as never));
  }

  it('blocks a non-owner Tenant from the route, redirecting to /dashboard', () => {
    const result = runGuard(true, RoleNames.Tenant) as unknown as { commands: unknown[] };
    expect(result.commands).toEqual(['/dashboard']);
  });

  it('allows a PropertyManager through', () => {
    expect(runGuard(true, RoleNames.PropertyManager)).toBe(true);
  });

  it('allows a PropertyOwner through', () => {
    expect(runGuard(true, RoleNames.PropertyOwner)).toBe(true);
  });

  it('redirects an unauthenticated caller to /login rather than /dashboard', () => {
    const result = runGuard(false, null) as unknown as { commands: unknown[] };
    expect(result.commands).toEqual(['/login']);
  });
});
