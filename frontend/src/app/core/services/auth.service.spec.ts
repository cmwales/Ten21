import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { ApiResponse, AuthResponse } from '../models/auth.models';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const session: AuthResponse = {
    accessToken: 'token-123',
    expiresAtUtc: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
    tenantId: 'tenant-1',
    organizationId: null,
    role: 'PropertyManager',
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts unauthenticated with no stored session', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.role()).toBeNull();
  });

  it('login() stores the session and flips isAuthenticated to true', () => {
    service.login({ email: 'dev@ten21.io', password: 'secret' }).subscribe();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.withCredentials).toBe(true);
    req.flush({
      success: true,
      data: session,
      message: null,
      statusCode: 200,
      traceId: 't1',
    } satisfies ApiResponse<AuthResponse>);

    expect(service.isAuthenticated()).toBe(true);
    expect(service.role()).toBe('PropertyManager');
    expect(service.tenantId()).toBe('tenant-1');
    expect(localStorage.getItem('ten21_auth_session')).toContain('token-123');
  });

  it('treats an expired access token as unauthenticated', () => {
    service.setSession({ ...session, expiresAtUtc: new Date(Date.now() - 1000).toISOString() });
    expect(service.isAuthenticated()).toBe(false);
  });

  it('logout() clears the session even if the revoke call fails', () => {
    service.setSession(session);

    service.logout().subscribe();
    const req = httpMock.expectOne('/api/auth/revoke-token');
    req.flush('server error', { status: 500, statusText: 'Server Error' });

    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('ten21_auth_session')).toBeNull();
  });
});
