import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../core/models/auth.models';
import { DirectoryAdminResponse } from '../../core/models/directory.models';
import { DirectoryAdmin } from './directory-admin';

describe('DirectoryAdmin', () => {
  let httpMock: HttpTestingController;

  const response: DirectoryAdminResponse = {
    workspaceDirectoryEnabled: true,
    entries: [
      {
        firstName: 'Sam',
        lastName: 'Ortiz',
        email: 'sam@example.com',
        phoneNumber: '555-0100',
        propertyAddress: '100 Main St, Provo, UT 84601',
        unitIdentifier: 'Suite B',
      },
    ],
  };

  function createComponent(): DirectoryAdmin {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(DirectoryAdmin);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  afterEach(() => httpMock.verify());

  it('loads the directory entries and workspace toggle state', () => {
    const component = createComponent();

    httpMock.expectOne('/api/directory/admin').flush({
      success: true, data: response, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<DirectoryAdminResponse>);

    expect(component['entries']()).toEqual(response.entries);
    expect(component['workspaceDirectoryEnabled']()).toBe(true);
    expect(component['loading']()).toBe(false);
  });

  it('surfaces workspaceDirectoryEnabled = false so the disabled banner can render', () => {
    const component = createComponent();

    httpMock.expectOne('/api/directory/admin').flush({
      success: true,
      data: { ...response, workspaceDirectoryEnabled: false },
      message: null,
      statusCode: 200,
      traceId: 't1',
    } satisfies ApiResponse<DirectoryAdminResponse>);

    expect(component['workspaceDirectoryEnabled']()).toBe(false);
    expect(component['entries']().length).toBe(1);
  });

  it('sets an error on failure', () => {
    const component = createComponent();

    httpMock.expectOne('/api/directory/admin').flush(
      { type: 'about:blank', title: 'Server Error', status: 500 },
      { status: 500, statusText: 'Server Error' },
    );

    expect(component['loading']()).toBe(false);
    expect(component['errorKey']()).toBe('directoryAdmin.loadError');
  });
});
