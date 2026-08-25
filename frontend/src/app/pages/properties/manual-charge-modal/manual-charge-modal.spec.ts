import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { ManualChargeResponse } from '../../../core/models/manual-charge.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ToastService } from '../../../core/services/toast.service';
import { ManualChargeModal } from './manual-charge-modal';

describe('ManualChargeModal', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

  const resident: ResidentResponse = {
    id: 'resident-1',
    propertyId: 'prop-1',
    userId: null,
    occupantType: 'Primary',
    firstName: 'Dana',
    lastName: 'Demo',
    email: null,
    phoneNumber: null,
    forwardingAddress: null,
    noticeGivenDate: null,
    showInDirectory: false,
    emergencyContacts: [],
  };

  const charge: ManualChargeResponse = {
    id: 'charge-1',
    propertyId: 'prop-1',
    residentId: null,
    description: 'Trash Violation Fine',
    amount: 75,
    dueDate: '2026-09-15',
    accountingCode: 'GL-4100',
  };

  function createComponent(): ManualChargeModal {
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
    const fixture = TestBed.createComponent(ManualChargeModal);
    const component = fixture.componentInstance;
    component.propertyId = 'prop-1';
    return component;
  }

  function open(component: ManualChargeModal): void {
    component.open = true;
    component.ngOnChanges({ open: new SimpleChange(false, true, false) });
    httpMock.expectOne('/api/properties/prop-1/manual-charges').flush({
      success: true, data: [charge], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ManualChargeResponse[]>);
    httpMock.expectOne('/api/properties/prop-1/residents').flush({
      success: true, data: [resident], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ResidentResponse[]>);
  }

  afterEach(() => httpMock.verify());

  it('loads charges and residents for the property when opened', () => {
    const component = createComponent();
    open(component);

    expect(component['charges']()).toEqual([charge]);
    expect(component['residents']()).toEqual([resident]);
  });

  it('does not reload when open changes to false', () => {
    const component = createComponent();
    component.open = false;
    component.ngOnChanges({ open: new SimpleChange(true, false, false) });

    httpMock.expectNone('/api/properties/prop-1/manual-charges');
  });

  it('residentName() resolves a loaded resident, and returns null for a unit-level charge', () => {
    const component = createComponent();
    open(component);

    expect(component['residentName']('resident-1')).toBe('Dana Demo');
    expect(component['residentName'](null)).toBeNull();
  });

  it('startAdd() shows an empty form, and save() posts a create request scoped to the unit generally', () => {
    const component = createComponent();
    open(component);

    component['startAdd']();
    component['form'].patchValue({ description: 'Trash Violation Fine', amount: 75, dueDate: '2026-09-15' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/manual-charges');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.residentId).toBeNull();
    expect(req.request.body.description).toBe('Trash Violation Fine');
    req.flush({ success: true, data: charge, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<ManualChargeResponse>);

    const reload = httpMock.expectOne('/api/properties/prop-1/manual-charges');
    reload.flush({ success: true, data: [charge], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<
      ManualChargeResponse[]
    >);

    expect(toastService.show).toHaveBeenCalledWith('manualCharges.modal.addedToast');
    expect(component['showForm']()).toBe(false);
  });

  it('save() can scope a charge to a specific resident', () => {
    const component = createComponent();
    open(component);

    component['startAdd']();
    component['form'].patchValue({
      residentId: 'resident-1',
      description: 'Playground Key Pass',
      amount: 15,
      dueDate: '2026-09-20',
    });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/manual-charges');
    expect(req.request.body.residentId).toBe('resident-1');
    req.flush({ success: true, data: charge, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<ManualChargeResponse>);
    httpMock.expectOne('/api/properties/prop-1/manual-charges').flush({
      success: true, data: [charge], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ManualChargeResponse[]>);
  });

  it('does not submit an invalid form', () => {
    const component = createComponent();
    open(component);
    component['startAdd'](); // description/amount/dueDate required, left blank/zero

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/manual-charges');
  });

  it('deleteCharge() calls DELETE and reloads the list', () => {
    const component = createComponent();
    open(component);

    component['deleteCharge'](charge);

    const req = httpMock.expectOne('/api/properties/prop-1/manual-charges/charge-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    const reload = httpMock.expectOne('/api/properties/prop-1/manual-charges');
    reload.flush({ success: true, data: [], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ManualChargeResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('manualCharges.modal.removedToast');
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });
});
