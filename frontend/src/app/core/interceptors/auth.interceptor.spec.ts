import { HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  function run(req: HttpRequest<unknown>, storedAccessToken: string | null) {
    const authServiceStub = {
      accessToken: () => storedAccessToken,
      refresh: () => of({ accessToken: 'refreshed-token' }),
      clearSession: vi.fn(),
    };
    const routerStub = { navigate: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceStub },
        { provide: Router, useValue: routerStub },
      ],
    });

    let capturedRequest!: HttpRequest<unknown>;
    const next = (r: HttpRequest<unknown>) => {
      capturedRequest = r;
      return of(new HttpResponse({ status: 200 }));
    };

    TestBed.runInInjectionContext(() => authInterceptor(req, next));
    return capturedRequest;
  }

  it('preserves a request-supplied Authorization header even when a stored session token exists', () => {
    // Regression test: verifyTwoFactor()/completeProfile() (AuthService) deliberately set
    // their own Authorization header carrying a short-lived interim/challenge token. The
    // interceptor used to unconditionally overwrite it with the CURRENT stored session's
    // access token via req.clone({ setHeaders }) -- which clobbers headers of the same
    // name -- so any browser that had ever completed a login before (a stale token sitting
    // in localStorage, accessToken() doesn't check expiry) would have its genuinely valid,
    // unexpired 2FA code rejected because the server validated the wrong token entirely.
    const req = new HttpRequest('POST', '/api/auth/login/verify-2fa', { code: '123456' });
    const reqWithOwnAuth = req.clone({ setHeaders: { Authorization: 'Bearer challenge-token' } });

    const captured = run(reqWithOwnAuth, 'stale-stored-session-token');

    expect(captured.headers.get('Authorization')).toBe('Bearer challenge-token');
  });

  it('attaches the stored session token when the request has no Authorization header of its own', () => {
    const req = new HttpRequest('GET', '/api/properties', null);

    const captured = run(req, 'current-session-token');

    expect(captured.headers.get('Authorization')).toBe('Bearer current-session-token');
  });

  it('sends no Authorization header when there is no stored session and the request set none', () => {
    const req = new HttpRequest('GET', '/api/properties', null);

    const captured = run(req, null);

    expect(captured.headers.has('Authorization')).toBe(false);
  });
});
