import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { TenantMembershipSummary } from '../../core/models/organization.models';
import { AuthService } from '../../core/services/auth.service';
import { AppHeader } from './app-header';

describe('AppHeader', () => {
  let router: { navigateByUrl: ReturnType<typeof vi.fn> };
  let authService: {
    tenantId: ReturnType<typeof signal<string | null>>;
    role: ReturnType<typeof signal<string | null>>;
    listWorkspaces: ReturnType<typeof vi.fn>;
    switchWorkspace: ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
  };

  const workspaces: TenantMembershipSummary[] = [
    { tenantId: 'tenant-1', tenantName: 'Riverside HQ', isPrimary: true, role: 'PropertyManager' },
    { tenantId: 'tenant-2', tenantName: 'Second Property', isPrimary: false, role: 'PropertyManager' },
  ];

  function createComponent(): AppHeader {
    router = { navigateByUrl: vi.fn().mockReturnValue(Promise.resolve(true)) };
    authService = {
      tenantId: signal<string | null>('tenant-1'),
      role: signal<string | null>('PropertyManager'),
      listWorkspaces: vi.fn().mockReturnValue(of(workspaces)),
      switchWorkspace: vi.fn().mockReturnValue(of({})),
      logout: vi.fn().mockReturnValue(of(undefined)),
    };

    TestBed.configureTestingModule({
      providers: [
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: authService },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
      ],
    });

    const fixture = TestBed.createComponent(AppHeader);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  it('loads workspaces on init and identifies the active one by tenantId', () => {
    const component = createComponent();

    expect(authService.listWorkspaces).toHaveBeenCalled();
    expect(component['activeWorkspaceName']()).toBe('Riverside HQ');
    expect(component['otherWorkspaces']()).toEqual([workspaces[1]]);
  });

  it('switchWorkspace() calls AuthService, reloads the list, and refreshes the current route', () => {
    const component = createComponent();

    component['switchWorkspace']('tenant-2');

    expect(authService.switchWorkspace).toHaveBeenCalledWith('tenant-2');
    expect(authService.listWorkspaces).toHaveBeenCalledTimes(2); // once on init, once after switching
    expect(router.navigateByUrl).toHaveBeenCalledWith('/', { skipLocationChange: true });
    expect(component['workspaceMenuOpen']()).toBe(false);
    expect(component['switching']()).toBe(false);
  });

  it('canManageProperties()/isTenant() reflect the current role', () => {
    const component = createComponent();
    expect(component['isTenant']()).toBe(false);
    expect(component['canManageProperties']()).toBe(true);

    authService.role.set('Tenant');
    expect(component['isTenant']()).toBe(true);
    expect(component['canManageProperties']()).toBe(false);
  });

  it('logout() calls AuthService and navigates to /login', () => {
    const component = createComponent();

    component['logout']();

    expect(authService.logout).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('toggleWorkspaceMenu()/closeWorkspaceMenu() control the dropdown open state', () => {
    const component = createComponent();
    expect(component['workspaceMenuOpen']()).toBe(false);

    component['toggleWorkspaceMenu']();
    expect(component['workspaceMenuOpen']()).toBe(true);

    component['closeWorkspaceMenu']();
    expect(component['workspaceMenuOpen']()).toBe(false);
  });
});
