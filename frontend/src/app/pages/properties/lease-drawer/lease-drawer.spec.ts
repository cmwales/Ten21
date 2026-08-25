import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { LeaseResponse } from '../../../core/models/lease.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ToastService } from '../../../core/services/toast.service';
import { LeaseDrawer } from './lease-drawer';

describe('LeaseDrawer', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

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

  const lease: LeaseResponse = {
    id: 'lease-1',
    propertyId: 'prop-1',
    residentId: 'resident-1',
    startDate: '2026-09-01',
    endDate: '2027-08-31',
    monthlyBaseRent: 1450,
    dueDayOfMonth: 1,
    status: 'FixedTerm',
    moveOutNoticeDate: null,
    totalMonthlyDues: 1500,
    recurringCharges: [{ id: 'charge-1', chargeName: 'Pet Rent', amount: 50, accountingCode: 'GL-4030' }],
    effectiveStatus: 'FixedTerm',
    isExpiringSoon: false,
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
    const fixture = TestBed.createComponent(LeaseDrawer);
    const component = fixture.componentInstance;
    component.propertyId = 'prop-1';
    return component;
  }

  function open(component: LeaseDrawer): void {
    component.open = true;
    component.ngOnChanges({ open: new SimpleChange(false, true, false) });
    httpMock.expectOne('/api/properties/prop-1/leases').flush({
      success: true, data: [lease], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<LeaseResponse[]>);
    httpMock.expectOne('/api/properties/prop-1/residents').flush({
      success: true, data: [resident], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ResidentResponse[]>);
  }

  afterEach(() => httpMock.verify());

  it('loads leases and residents for the property when opened', () => {
    const component = createComponent();
    open(component);

    expect(component['leases']()).toEqual([lease]);
    expect(component['residents']()).toEqual([resident]);
  });

  it('does not reload when open changes to false', () => {
    const component = createComponent();
    component.open = false;
    component.ngOnChanges({ open: new SimpleChange(true, false, false) });

    httpMock.expectNone('/api/properties/prop-1/leases');
    httpMock.expectNone('/api/properties/prop-1/residents');
  });

  it('residentName() resolves a loaded resident to a display name', () => {
    const component = createComponent();
    open(component);

    expect(component['residentName']('resident-1')).toBe('Dana Demo');
  });

  it('startAdd() shows an empty form defaulted to FixedTerm, and save() posts a create request', () => {
    const component = createComponent();
    open(component);

    component['startAdd']();
    component['form'].patchValue({
      residentId: 'resident-1',
      startDate: '2026-09-01',
      endDate: '2027-08-31',
      monthlyBaseRent: 1450,
    });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/leases');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.residentId).toBe('resident-1');
    expect(req.request.body.status).toBe('FixedTerm');
    expect(req.request.body.recurringCharges).toEqual([]);
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
    expect(component['form'].controls.recurringCharges.length).toBe(1);
    expect(component['form'].controls.recurringCharges.at(0).controls.chargeName.value).toBe('Pet Rent');

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/leases/lease-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.recurringCharges).toEqual([{ chargeName: 'Pet Rent', amount: 50, accountingCode: 'GL-4030' }]);
    req.flush({ success: true, data: lease, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<LeaseResponse>);

    const reload = httpMock.expectOne('/api/properties/prop-1/leases');
    reload.flush({ success: true, data: [lease], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<LeaseResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('leases.drawer.savedToast');
  });

  it('recomputeTotal() sums MonthlyBaseRent and every recurring charge amount live', () => {
    const component = createComponent();
    open(component);
    component['startAdd']();

    component['form'].patchValue({ monthlyBaseRent: 1450 });
    component['recomputeTotal']();
    expect(component['liveTotalMonthlyDues']()).toBe(1450);

    component['addRecurringChargeRow']();
    component['form'].controls.recurringCharges.at(0).patchValue({ amount: 50 });
    component['recomputeTotal']();
    expect(component['liveTotalMonthlyDues']()).toBe(1500);

    component['removeRecurringChargeRow'](0);
    expect(component['liveTotalMonthlyDues']()).toBe(1450);
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
      data: { id: 'charge-1', propertyId: 'prop-1', residentId: 'resident-1', description: 'Pro-Rated Rent', amount: 200, dueDate: '2026-09-05', accountingCode: null },
      message: null,
      statusCode: 200,
      traceId: 't1',
    });

    expect(toastService.show).toHaveBeenCalledWith('leases.drawer.moveInChargeCreatedToast');
    expect(component['moveInChargeLeaseId']()).toBeNull();
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });
});
