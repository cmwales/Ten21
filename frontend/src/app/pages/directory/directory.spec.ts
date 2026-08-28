import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../core/models/auth.models';
import { DirectoryEntryResponse } from '../../core/models/directory.models';
import { Directory } from './directory';

describe('Directory', () => {
  let httpMock: HttpTestingController;

  const entries: DirectoryEntryResponse[] = [
    { firstName: 'Sam', lastName: 'Ortiz', unitIdentifier: 'Suite B' },
  ];

  function createComponent(): Directory {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Directory);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  afterEach(() => httpMock.verify());

  it('loads the directory and exposes the entries', () => {
    const component = createComponent();

    httpMock.expectOne('/api/directory').flush({
      success: true, data: entries, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<DirectoryEntryResponse[]>);

    expect(component['entries']()).toEqual(entries);
    expect(component['loading']()).toBe(false);
    expect(component['disabled']()).toBe(false);
  });

  it('shows the disabled state (not a generic error) when the workspace has turned the directory off', () => {
    const component = createComponent();

    httpMock.expectOne('/api/directory').flush(
      { type: 'about:blank', title: 'Forbidden', status: 403 },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(component['loading']()).toBe(false);
    expect(component['disabled']()).toBe(true);
    expect(component['errorKey']()).toBeNull();
  });

  it('sets a generic error for any other failure', () => {
    const component = createComponent();

    httpMock.expectOne('/api/directory').flush(
      { type: 'about:blank', title: 'Server Error', status: 500 },
      { status: 500, statusText: 'Server Error' },
    );

    expect(component['loading']()).toBe(false);
    expect(component['disabled']()).toBe(false);
    expect(component['errorKey']()).toBe('directory.loadError');
  });
});
