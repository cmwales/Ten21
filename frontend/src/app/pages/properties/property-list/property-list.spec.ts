import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { PropertyListResponse } from '../../../core/models/property.models';
import { PropertyList } from './property-list';

describe('PropertyList', () => {
  let httpMock: HttpTestingController;

  const response: PropertyListResponse = {
    items: [
      {
        id: 'prop-1',
        name: 'Riverside Apartments',
        propertyType: 'MultiFamily',
        streetAddress1: '100 Main St',
        city: 'Provo',
        state: 'UT',
        postalCode: '84601',
        units: [
          { id: 'unit-1', unitIdentifier: '101', occupancyStatus: 'Vacant', targetRent: 1200 },
          { id: 'unit-2', unitIdentifier: '102', occupancyStatus: 'Occupied', targetRent: 1300 },
        ],
      },
      {
        id: 'prop-2',
        name: 'Downtown Lofts',
        propertyType: 'Commercial',
        streetAddress1: '5 Center St',
        city: 'Ogden',
        state: 'UT',
        postalCode: '84401',
        units: [],
      },
    ],
    totalCount: 2,
    pageNumber: 1,
    pageSize: 2,
  };

  function createComponent(): PropertyList {
    // vi.useFakeTimers() (needed for the debounced-search test) interferes with the test
    // runner's automatic TestBed reset between `it()` blocks, so this resets explicitly.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PropertyList);
    const component = fixture.componentInstance;
    component.ngOnInit();
    httpMock.expectOne('/api/properties').flush({
      success: true,
      data: response,
      message: null,
      statusCode: 200,
      traceId: 't1',
    } satisfies ApiResponse<PropertyListResponse>);
    return component;
  }

  afterEach(() => httpMock.verify());

  it('loads and shows every property with no filters applied', () => {
    const component = createComponent();
    expect(component['filteredProperties']().length).toBe(2);
    expect(component['isEmptyWorkspace']()).toBe(false);
  });

  it('search filters by unit identifier across the full loaded set', () => {
    const component = createComponent();

    vi.useFakeTimers();
    try {
      component['onSearchInput']('102');
      vi.advanceTimersByTime(350);
    } finally {
      vi.useRealTimers();
    }

    expect(component['filteredProperties']().length).toBe(1);
    expect(component['filteredProperties']()[0].id).toBe('prop-1');
  });

  it('property type filter badges narrow the list', () => {
    const component = createComponent();
    component['toggleTypeFilter']('Commercial');

    expect(component['filteredProperties']().length).toBe(1);
    expect(component['filteredProperties']()[0].id).toBe('prop-2');

    component['toggleTypeFilter']('Commercial');
    expect(component['filteredProperties']().length).toBe(2);
  });

  it('paginates client-side by the selected page size', () => {
    const component = createComponent();
    component['setPageSize'](1 as never);

    expect(component['pagedProperties']().length).toBe(1);
    expect(component['totalPages']()).toBe(2);

    component['nextPage']();
    expect(component['pagedProperties']()[0].id).toBe('prop-2');

    component['nextPage']();
    expect(component['pageNumber']()).toBe(2);
  });
});
