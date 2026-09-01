import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { LeaseResponse } from '../../../core/models/lease.models';
import { PropertyResponse, PropertyTypes, OccupancyStatuses } from '../../../core/models/property.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ToastService } from '../../../core/services/toast.service';
import { LeaseDrawer } from './lease-drawer';

describe('LeaseDrawer', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<LeaseDrawer>;

  const resident: ResidentResponse = {
    id: 'resident-1',
    propertyId: 'prop-1',
    userId: null,
    occupantType: 'Primary',
    firstName: 'Dana',
    lastName: 'Demo',
    email: 'dana@example.com',
    phoneNumber: null,
    forwardingAddress: null,
    noticeGivenDate: null,
    showInDirectory: false,
    emergencyContacts: [],
  };

  const baseRentCharge = {
    id: 'charge-base-rent',
    chargeName: 'Base Rent',
    category: 'BaseRent' as const,
    amount: 1450,
    recurrencePattern: 'Monthly' as const,
    recurrenceInterval: 1,
    dueDayOfMonth: 1,
    targetDayOfWeek: null,
    secondaryDueDay: null,
    endStrategy: 'LeaseAligned' as const,
    effectiveStartDate: '2026-09-01',
    effectiveEndDate: null,
    prorationStrategy: 'FullAmount' as const,
    isPaused: false,
    accountingCode: null,
    description: null,
  };

  const petRentCharge = {
    id: 'charge-1',
    chargeName: 'Pet Rent',
    category: 'AddOn' as const,
    amount: 50,
    recurrencePattern: 'Monthly' as const,
    recurrenceInterval: 1,
    dueDayOfMonth: 1,
    targetDayOfWeek: null,
    secondaryDueDay: null,
    endStrategy: 'LeaseAligned' as const,
    effectiveStartDate: '2026-09-01',
    effectiveEndDate: null,
    prorationStrategy: 'FullAmount' as const,
    isPaused: false,
    accountingCode: 'GL-4030',
    description: null,
  };

  const lease: LeaseResponse = {
    id: 'lease-1',
    propertyId: 'prop-1',
    residentId: 'resident-1',
    startDate: '2026-09-01',
    endDate: '2027-08-31',
    status: 'FixedTerm',
    totalMonthlyDues: 1500,
    recurringCharges: [baseRentCharge, petRentCharge],
    effectiveStatus: 'FixedTerm',
    isExpiringSoon: false,
  };

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
    moveOutNoticeDate: null,
  };

  function createComponent(): LeaseDrawer {
    toastService = { show: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: ToastService, useValue: toastService },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LeaseDrawer);
    const component = fixture.componentInstance;
    fixture.componentRef.setInput('propertyId', 'prop-1');
    return component;
  }

  function open(_component: LeaseDrawer): void {
    fixture.componentRef.setInput('open', true);
    TestBed.flushEffects();
    httpMock.expectOne('/api/properties/prop-1/leases').flush({
      success: true, data: [lease], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<LeaseResponse[]>);
    httpMock.expectOne('/api/properties/prop-1/residents').flush({
      success: true, data: [resident], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ResidentResponse[]>);
    httpMock.expectOne('/api/properties/prop-1').flush({
      success: true, data: property, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<PropertyResponse>);
  }

  afterEach(() => httpMock.verify());

  it('loads leases and residents for the property when opened', () => {
    const component = createComponent();
    open(component);

    expect(component['leases']()).toEqual([lease]);
    expect(component['residents']()).toEqual([resident]);
  });

  it('does not reload when open is false', () => {
    createComponent();
    fixture.componentRef.setInput('open', false);
    TestBed.flushEffects();

    httpMock.expectNone('/api/properties/prop-1/leases');
    httpMock.expectNone('/api/properties/prop-1/residents');
    httpMock.expectNone('/api/properties/prop-1');
  });

  it('residentName() resolves a loaded resident to a display name', () => {
    const component = createComponent();
    open(component);

    expect(component['residentName']('resident-1')).toBe('Dana Demo');
  });

  it('startAdd() shows an empty form defaulted to FixedTerm, and save() posts a create request', () => {
    const component = createComponent();
    open(component);

    component['startAdd'](); // auto-adds the pinned Base Rent row (US-44)
    component['form'].patchValue({
      residentId: 'resident-1',
      startDate: '2026-09-01',
      endDate: '2027-08-31',
    });
    component['form'].controls.recurringCharges.at(0).patchValue({ amount: 1450 });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/leases');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.residentId).toBe('resident-1');
    expect(req.request.body.status).toBe('FixedTerm');
    expect(req.request.body.recurringCharges).toHaveLength(1);
    expect(req.request.body.recurringCharges[0]).toMatchObject({ chargeName: 'Base Rent', category: 'BaseRent', amount: 1450 });
    req.flush({ success: true, data: lease, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<LeaseResponse>);

    const reload = httpMock.expectOne('/api/properties/prop-1/leases');
    reload.flush({ success: true, data: [lease], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<LeaseResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('leases.drawer.addedToast');
    expect(component['showForm']()).toBe(false);
  });

  it('startEdit() populates the form including recurring charges, and save() puts an update', () => {
    const component = createComponent();
    open(component);

    component['startEdit'](lease);

    expect(component['form'].controls.residentId.value).toBe('resident-1');
    // Base Rent is always sorted to index 0, regardless of the server's own row order.
    expect(component['form'].controls.recurringCharges.length).toBe(2);
    expect(component['form'].controls.recurringCharges.at(0).controls.chargeName.value).toBe('Base Rent');
    expect(component['form'].controls.recurringCharges.at(1).controls.chargeName.value).toBe('Pet Rent');

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/leases/lease-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.recurringCharges).toHaveLength(2);
    expect(req.request.body.recurringCharges[1]).toMatchObject({ chargeName: 'Pet Rent', amount: 50, accountingCode: 'GL-4030' });
    req.flush({ success: true, data: lease, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<LeaseResponse>);

    const reload = httpMock.expectOne('/api/properties/prop-1/leases');
    reload.flush({ success: true, data: [lease], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<LeaseResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('leases.drawer.savedToast');
  });

  it('recomputeTotal() sums Base Rent and every add-on recurring charge amount live', () => {
    const component = createComponent();
    open(component);
    component['startAdd'](); // pinned Base Rent row at index 0

    component['form'].controls.recurringCharges.at(0).patchValue({ amount: 1450 });
    component['recomputeTotal']();
    expect(component['liveTotalMonthlyDues']()).toBe(1450);

    component['addRecurringChargeRow']();
    component['form'].controls.recurringCharges.at(1).patchValue({ amount: 50 });
    component['recomputeTotal']();
    expect(component['liveTotalMonthlyDues']()).toBe(1500);

    component['removeRecurringChargeRow'](1);
    expect(component['liveTotalMonthlyDues']()).toBe(1450);
  });

  it('removeRecurringChargeRow() refuses to remove the pinned Base Rent row at index 0', () => {
    const component = createComponent();
    open(component);
    component['startAdd']();

    component['removeRecurringChargeRow'](0);

    expect(component['form'].controls.recurringCharges.length).toBe(1);
  });

  it('does not submit an invalid form', () => {
    const component = createComponent();
    open(component);
    component['startAdd'](); // residentId/startDate/endDate required, left blank

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/leases');
  });

  it('deleteLease() calls DELETE and reloads the list', () => {
    const component = createComponent();
    open(component);

    component['deleteLease'](lease);

    const req = httpMock.expectOne('/api/properties/prop-1/leases/lease-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    const reload = httpMock.expectOne('/api/properties/prop-1/leases');
    reload.flush({ success: true, data: [], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<LeaseResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('leases.drawer.removedToast');
  });

  it('startMoveInCharge()/cancelMoveInCharge() toggle the inline move-in form, defaulted to the lease start date', () => {
    const component = createComponent();
    open(component);

    component['startMoveInCharge'](lease);
    expect(component['moveInChargeLeaseId']()).toBe('lease-1');
    expect(component['moveInDate']()).toBe('2026-09-01');

    component['cancelMoveInCharge']();
    expect(component['moveInChargeLeaseId']()).toBeNull();
  });

  it('createMoveInCharge() posts the move-in date and shows a confirmation toast', () => {
    const component = createComponent();
    open(component);
    component['startMoveInCharge'](lease);
    component['moveInDate'].set('2026-09-05');

    component['createMoveInCharge'](lease);

    const req = httpMock.expectOne('/api/properties/prop-1/leases/lease-1/move-in-charge');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ moveInDate: '2026-09-05' });
    req.flush({
      success: true,
      data: { id: 'charge-1', propertyId: 'prop-1', description: 'Pro-Rated Rent', amount: 200, dueDate: '2026-09-05', accountingCode: null, paidDate: null },
      message: null,
      statusCode: 200,
      traceId: 't1',
    });

    expect(toastService.show).toHaveBeenCalledWith('leases.drawer.moveInChargeCreatedToast');
    expect(component['moveInChargeLeaseId']()).toBeNull();
  });

  it('loads the property-level move-out notice, not a per-lease one', () => {
    const component = createComponent();
    open(component);

    expect(component['propertyMoveOutNoticeDate']()).toBeNull();
  });

  it('onMoveOutNoticeChange() PATCHes the property and shows a confirmation toast', () => {
    const component = createComponent();
    open(component);

    component['onMoveOutNoticeChange']('2026-11-01');

    const req = httpMock.expectOne('/api/properties/prop-1/move-out-notice');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ moveOutNoticeDate: '2026-11-01' });
    req.flush({
      success: true, data: { ...property, moveOutNoticeDate: '2026-11-01' }, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<PropertyResponse>);

    expect(component['propertyMoveOutNoticeDate']()).toBe('2026-11-01');
    expect(toastService.show).toHaveBeenCalledWith('leases.drawer.moveOutNoticeSavedToast');
    expect(component['savingMoveOutNotice']()).toBe(false);
  });

  it('onMoveOutNoticeChange() with an empty value clears the notice', () => {
    const component = createComponent();
    open(component);

    component['onMoveOutNoticeChange']('');

    const req = httpMock.expectOne('/api/properties/prop-1/move-out-notice');
    expect(req.request.body).toEqual({ moveOutNoticeDate: null });
    req.flush({ success: true, data: property, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<PropertyResponse>);
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });
});
