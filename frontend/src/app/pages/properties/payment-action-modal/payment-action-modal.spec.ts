import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { PaymentTransactionResponse } from '../../../core/models/ledger.models';
import { OccupancyStatuses, PropertyListItemDto, PropertyListResponse, PropertyTypes } from '../../../core/models/property.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ToastService } from '../../../core/services/toast.service';
import { PaymentActionModal } from './payment-action-modal';

describe('PaymentActionModal', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<PaymentActionModal>;

  const otherProperty: PropertyListItemDto = {
    id: 'prop-2',
    name: 'Second Property',
    propertyType: PropertyTypes.MultiFamily,
    streetAddress1: '43 Statutory Way',
    city: 'Provo',
    state: 'UT',
    postalCode: '84601',
    unitIdentifier: 'Unit 2',
    targetRent: 1200,
    occupancyStatus: OccupancyStatuses.Occupied,
  };

  const resident: ResidentResponse = {
    id: 'resident-2',
    propertyId: 'prop-2',
    userId: null,
    occupantType: 'Primary',
    firstName: 'Alex',
    lastName: 'Kim',
    email: null,
    phoneNumber: null,
    forwardingAddress: null,
    noticeGivenDate: null,
    showInDirectory: false,
    emergencyContacts: [],
  };

  const payment: PaymentTransactionResponse = {
    id: 'payment-2',
    propertyId: 'prop-2',
    residentProfileId: 'resident-2',
    residentName: 'Alex Kim',
    paymentDate: '2026-09-01',
    amountPaid: 500,
    tenderType: 'Check',
    referenceNumber: null,
    notes: null,
    unallocatedAmount: 0,
    status: 'Reversed',
    reversalReason: 'NSF',
    reallocatedToId: null,
    allocations: [],
  };

  function createComponent(): PaymentActionModal {
    toastService = { show: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: ToastService, useValue: toastService },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PaymentActionModal);
    const component = fixture.componentInstance;
    fixture.componentRef.setInput('propertyId', 'prop-1');
    fixture.componentRef.setInput('paymentId', 'payment-1');
    return component;
  }

  function open(_component: PaymentActionModal): void {
    fixture.componentRef.setInput('open', true);
    TestBed.flushEffects();
    httpMock.expectOne('/api/properties').flush({
      success: true,
      data: { items: [otherProperty], totalCount: 1, pageNumber: 1, pageSize: 50 },
      message: null,
      statusCode: 200,
      traceId: 't1',
    } satisfies ApiResponse<PropertyListResponse>);
  }

  afterEach(() => httpMock.verify());

  it('loads properties (excluding the current one) when opened, defaulting to reverse', () => {
    const component = createComponent();
    open(component);

    expect(component['properties']()).toEqual([otherProperty]);
    expect(component['form'].controls.actionType.value).toBe('reverse');
  });

  it('save() posts a reverse request when actionType is reverse', () => {
    const component = createComponent();
    open(component);
    const savedEmitted = vi.fn();
    component.saved.subscribe(savedEmitted);
    component['form'].patchValue({ reversalReason: 'Bounced check' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/payments/payment-1/reverse');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reversalReason: 'Bounced check' });
    req.flush({ success: true, data: payment, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<PaymentTransactionResponse>);

    expect(toastService.show).toHaveBeenCalledWith('payments.action.reversedToast');
    expect(savedEmitted).toHaveBeenCalled();
  });

  it('onTargetPropertyChange() loads residents for the selected target property', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ actionType: 'reallocate', targetPropertyId: 'prop-2' });

    component['onTargetPropertyChange']();

    const req = httpMock.expectOne('/api/properties/prop-2/residents');
    req.flush({ success: true, data: [resident], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ResidentResponse[]>);

    expect(component['targetResidents']()).toEqual([resident]);
  });

  it('save() posts a reallocate request with the target property/resident', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({
      actionType: 'reallocate',
      targetPropertyId: 'prop-2',
      targetResidentProfileId: 'resident-2',
      reversalReason: 'Posted to the wrong door',
    });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/payments/payment-1/reallocate');
    expect(req.request.body).toEqual({
      targetPropertyId: 'prop-2',
      targetResidentProfileId: 'resident-2',
      reversalReason: 'Posted to the wrong door',
    });
    req.flush({ success: true, data: payment, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<PaymentTransactionResponse>);

    expect(toastService.show).toHaveBeenCalledWith('payments.action.reallocatedToast');
  });

  it('does not submit reallocate without a target resident', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ actionType: 'reallocate', targetPropertyId: 'prop-2', reversalReason: 'reason' });

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/payments/payment-1/reallocate');
  });

  it('does not submit without a reason', () => {
    const component = createComponent();
    open(component);

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/payments/payment-1/reverse');
  });

  it('shows an error toast when the server rejects the request', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ reversalReason: 'Bounced check' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/payments/payment-1/reverse');
    req.flush({ type: 'about:blank', title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });

    expect(toastService.show).toHaveBeenCalledWith('payments.action.errorToast');
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });
});
