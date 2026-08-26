import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { UnitStatementResponse } from '../../../core/models/ledger.models';
import { PropertyResponse, PropertyTypes, OccupancyStatuses } from '../../../core/models/property.models';
import { ToastService } from '../../../core/services/toast.service';
import { UnitStatement } from './unit-statement';

describe('UnitStatement', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

  const property: PropertyResponse = {
    id: 'prop-1',
    name: 'Riverside Apartments',
    propertyType: PropertyTypes.MultiFamily,
    streetAddress1: '100 Main St',
    streetAddress2: null,
    city: 'Provo',
    state: 'UT',
    postalCode: '84601',
    country: 'USA',
    unitIdentifier: 'Suite A',
    targetRent: 1450,
    occupancyStatus: OccupancyStatuses.Occupied,
    allowTenantDirectory: false,
  };

  const statement: UnitStatementResponse = {
    propertyId: 'prop-1',
    balance: 500,
    availableCredit: 0,
    accountStatus: 'Active',
    charges: [
      {
        charge: {
          id: 'charge-1',
          propertyId: 'prop-1',
          description: 'September Rent',
          amount: 1450,
          dueDate: '2026-09-01',
          accountingCode: null,
          category: 'BaseRent',
          status: 'Active',
          allocatedAmount: 950,
          outstandingAmount: 500,
          paymentStatus: 'Partial',
          isLocked: true,
        },
        adjustments: [
          { id: 'adj-1', adjustmentType: 'CreditAdjustment', amount: 25, reason: 'Goodwill discount for late maintenance', createdAt: '2026-09-05T00:00:00Z' },
        ],
      },
    ],
    payments: [
      {
        id: 'payment-1',
        propertyId: 'prop-1',
        residentProfileId: 'resident-1',
        residentName: 'Jamie Rivera',
        paymentDate: '2026-09-03',
        amountPaid: 950,
        tenderType: 'Check',
        referenceNumber: '1042',
        notes: null,
        unallocatedAmount: 0,
        status: 'Cleared',
        reversalReason: null,
        reallocatedToId: null,
        allocations: [{ chargeId: 'charge-1', chargeDescription: 'September Rent', allocatedAmount: 950 }],
      },
    ],
    credits: [],
    refunds: [],
    deposits: [],
  };

  function createComponent(id: string | null = 'prop-1'): UnitStatement {
    toastService = { show: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => id } } } },
        { provide: ToastService, useValue: toastService },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(UnitStatement);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  afterEach(() => httpMock.verify());

  it('loads the property and statement, then exposes them', () => {
    const component = createComponent();

    httpMock.expectOne('/api/properties/prop-1').flush({
      success: true, data: property, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<PropertyResponse>);
    httpMock.expectOne('/api/properties/prop-1/charges/statement').flush({
      success: true, data: statement, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<UnitStatementResponse>);

    expect(component['property']()).toEqual(property);
    expect(component['statement']()).toEqual(statement);
    expect(component['loading']()).toBe(false);
  });

  it('sets an error and stops loading when the statement request fails', () => {
    const component = createComponent();

    httpMock.expectOne('/api/properties/prop-1').flush({
      success: true, data: property, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<PropertyResponse>);
    httpMock.expectOne('/api/properties/prop-1/charges/statement').flush(
      { type: 'about:blank', title: 'Not Found', status: 404 },
      { status: 404, statusText: 'Not Found' },
    );

    expect(component['loading']()).toBe(false);
    expect(component['errorKey']()).toBe('ledger.statement.loadError');
  });

  it('sets an error immediately when there is no property id in the route', () => {
    const component = createComponent(null);

    httpMock.expectNone('/api/properties/prop-1');
    expect(component['errorKey']()).toBe('ledger.statement.loadError');
    expect(component['loading']()).toBe(false);
  });

  it('loadStatement() re-fetches the statement, e.g. after LogPaymentModal emits saved', () => {
    const component = createComponent();
    httpMock.expectOne('/api/properties/prop-1').flush({
      success: true, data: property, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<PropertyResponse>);
    httpMock.expectOne('/api/properties/prop-1/charges/statement').flush({
      success: true, data: statement, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<UnitStatementResponse>);

    component['loadStatement']('prop-1');

    const reload = httpMock.expectOne('/api/properties/prop-1/charges/statement');
    reload.flush({ success: true, data: { ...statement, balance: 0 }, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<UnitStatementResponse>);

    expect(component['statement']()!.balance).toBe(0);
  });

  it('applyCredits() posts to the apply endpoint, toasts, and reloads the statement', () => {
    const component = createComponent();
    httpMock.expectOne('/api/properties/prop-1').flush({
      success: true, data: property, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<PropertyResponse>);
    httpMock.expectOne('/api/properties/prop-1/charges/statement').flush({
      success: true, data: statement, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<UnitStatementResponse>);

    component['applyCredits']();

    const req = httpMock.expectOne('/api/properties/prop-1/credits/apply');
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, data: { totalApplied: 150, allocations: [] }, message: null, statusCode: 200, traceId: 't1' });

    expect(toastService.show).toHaveBeenCalledWith('ledger.statement.creditsAppliedToast');
    expect(component['applyingCredits']()).toBe(false);

    const reload = httpMock.expectOne('/api/properties/prop-1/charges/statement');
    reload.flush({ success: true, data: statement, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<UnitStatementResponse>);
  });

  it('applyCredits() shows a no-op toast when nothing was applied', () => {
    const component = createComponent();
    httpMock.expectOne('/api/properties/prop-1').flush({
      success: true, data: property, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<PropertyResponse>);
    httpMock.expectOne('/api/properties/prop-1/charges/statement').flush({
      success: true, data: statement, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<UnitStatementResponse>);

    component['applyCredits']();

    const req = httpMock.expectOne('/api/properties/prop-1/credits/apply');
    req.flush({ success: true, data: { totalApplied: 0, allocations: [] }, message: null, statusCode: 200, traceId: 't1' });

    expect(toastService.show).toHaveBeenCalledWith('ledger.statement.noCreditsAppliedToast');

    const reload = httpMock.expectOne('/api/properties/prop-1/charges/statement');
    reload.flush({ success: true, data: statement, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<UnitStatementResponse>);
  });
});
