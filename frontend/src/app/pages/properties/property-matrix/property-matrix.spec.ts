import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { PropertyListResponse } from '../../../core/models/property.models';
import { UnitGroupResponse, UnitTierResponse } from '../../../core/models/unit-matrix.models';
import { PropertyMatrix } from './property-matrix';

describe('PropertyMatrix', () => {
  let httpMock: HttpTestingController;

  const properties: PropertyListResponse = {
    items: [
      {
        id: 'prop-1',
        name: 'Riverside Apartments - Suite A',
        propertyType: 'MultiFamily',
        streetAddress1: '100 Main St',
        city: 'Provo',
        state: 'UT',
        postalCode: '84601',
        unitIdentifier: 'Suite A',
        targetRent: 1200,
        occupancyStatus: 'Vacant',
        unitGroupId: null,
        unitTierId: null,
      },
      {
        id: 'prop-2',
        name: 'Riverside Apartments - Suite B',
        propertyType: 'MultiFamily',
        streetAddress1: '100 Main St',
        city: 'Provo',
        state: 'UT',
        postalCode: '84601',
        unitIdentifier: 'Suite B',
        targetRent: 1300,
        occupancyStatus: 'Occupied',
        unitGroupId: null,
        unitTierId: null,
      },
    ],
    totalCount: 2,
    pageNumber: 1,
    pageSize: 2,
  };

  const tiers: UnitTierResponse[] = [
    { id: 'tier-1', tierName: 'Ocean View 2BR', defaultRent: 2200, accountingCode: 'GL-4010', description: null },
  ];

  const groups: UnitGroupResponse[] = [{ id: 'group-1', groupName: 'North Wing', description: null }];

  function ok<T>(data: T): ApiResponse<T> {
    return { success: true, data, message: null, statusCode: 200, traceId: 't1' };
  }

  function createComponent(): PropertyMatrix {
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
    const fixture = TestBed.createComponent(PropertyMatrix);
    const component = fixture.componentInstance;
    component.ngOnInit();

    httpMock.expectOne('/api/properties').flush(ok(properties));
    httpMock.expectOne('/api/unit-tiers').flush(ok(tiers));
    httpMock.expectOne('/api/unit-groups').flush(ok(groups));

    return component;
  }

  afterEach(() => httpMock.verify());

  it('loads properties, tiers, and groups into the matrix', () => {
    const component = createComponent();

    expect(component['rows']().length).toBe(2);
    expect(component['tiers']().length).toBe(1);
    expect(component['groups']().length).toBe(1);
  });

  it('selecting a tier prefills TargetRent and debounce-saves the row', () => {
    const component = createComponent();
    const row = component['rows']()[0];

    vi.useFakeTimers();
    try {
      component['onTierChange'](row, 'tier-1');
      expect(component['rows']()[0].targetRent).toBe(2200);

      vi.advanceTimersByTime(600);
    } finally {
      vi.useRealTimers();
    }

    const req = httpMock.expectOne('/api/properties/prop-1/matrix');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ unitGroupId: null, unitTierId: 'tier-1', targetRent: 2200 });
    req.flush(ok({ id: 'prop-1', unitIdentifier: 'Suite A', unitGroupId: null, unitTierId: 'tier-1', targetRent: 2200 }));

    expect(component['saveStateByRow']().get('prop-1')).toBe('saved');
  });

  it('manually overriding TargetRent after a tier is picked is not reset', () => {
    const component = createComponent();
    const row = component['rows']()[0];

    vi.useFakeTimers();
    try {
      component['onTierChange'](row, 'tier-1');
      component['onTargetRentChange'](component['rows']()[0], '2500');
      vi.advanceTimersByTime(600);
    } finally {
      vi.useRealTimers();
    }

    // Only the final debounced save fires -- the tier-change save was superseded.
    const req = httpMock.expectOne('/api/properties/prop-1/matrix');
    expect(req.request.body).toEqual({ unitGroupId: null, unitTierId: 'tier-1', targetRent: 2500 });
    req.flush(ok({ id: 'prop-1', unitIdentifier: 'Suite A', unitGroupId: null, unitTierId: 'tier-1', targetRent: 2500 }));
  });

  it('batch-assigns a group to every selected row without touching TargetRent', () => {
    const component = createComponent();
    component['toggleSelectRow']('prop-1');
    component['toggleSelectRow']('prop-2');
    component['batchField'].set('UnitGroup');
    component['batchValueId'].set('group-1');

    component['applyBatch']();

    const req = httpMock.expectOne('/api/properties/matrix/batch');
    expect(req.request.body).toEqual({ propertyIds: ['prop-1', 'prop-2'], field: 'UnitGroup', valueId: 'group-1' });
    req.flush(
      ok([
        { id: 'prop-1', unitIdentifier: 'Suite A', unitGroupId: 'group-1', unitTierId: null, targetRent: 1200 },
        { id: 'prop-2', unitIdentifier: 'Suite B', unitGroupId: 'group-1', unitTierId: null, targetRent: 1300 },
      ]),
    );

    expect(component['rows']().every((r) => r.unitGroupId === 'group-1')).toBe(true);
    expect(component['selectedIds']().size).toBe(0);
  });

  it('toggleSelectAll selects and clears every row', () => {
    const component = createComponent();

    component['toggleSelectAll']();
    expect(component['selectedIds']().size).toBe(2);
    expect(component['allSelected']()).toBe(true);

    component['toggleSelectAll']();
    expect(component['selectedIds']().size).toBe(0);
  });

  it('deleteTier removes the tier from the local list on success', () => {
    const component = createComponent();

    component['deleteTier'](tiers[0]);
    httpMock.expectOne('/api/unit-tiers/tier-1').flush(null);

    expect(component['tiers']().length).toBe(0);
  });
});
