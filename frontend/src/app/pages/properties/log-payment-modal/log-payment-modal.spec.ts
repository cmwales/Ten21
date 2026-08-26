import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { PaymentTransactionResponse } from '../../../core/models/ledger.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ToastService } from '../../../core/services/toast.service';
import { LogPaymentModal } from './log-payment-modal';

describe('LogPaymentModal', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

  const resident: ResidentResponse = {
    id: 'resident-1',
    propertyId: 'prop-1',
    userId: null,
    occupantType: 'Primary',
    firstName: 'Jamie',
    lastName: 'Rivera',
    email: null,
    phoneNumber: null,
    forwardingAddress: null,
    noticeGivenDate: null,
    showInDirectory: false,
    emergencyContacts: [],
  };

  const payment: PaymentTransactionResponse = {
    id: 'payment-1',
    propertyId: 'prop-1',
    residentProfileId: 'resident-1',
    residentName: 'Jamie Rivera',
    paymentDate: '2026-09-01',
    amountPaid: 500,
    tenderType: 'Check',
    referenceNumber: 'CHK-1001',
    notes: null,
    allocations: [],
  };

  function createComponent(): LogPaymentModal {
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
    const fixture = TestBed.createComponent(LogPaymentModal);
    const component = fixture.componentInstance;
    component.propertyId = 'prop-1';
    return component;
  }

  function open(component: LogPaymentModal): void {
    component.open = true;
    component.ngOnChanges({ open: new SimpleChange(false, true, false) });
    httpMock.expectOne('/api/properties/prop-1/residents').flush({
      success: true, data: [resident], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ResidentResponse[]>);
  }

  afterEach(() => httpMock.verify());

  it('loads residents for the property when opened, and defaults tenderType to Cash', () => {
    const component = createComponent();
    open(component);

    expect(component['residents']()).toEqual([resident]);
    expect(component['form'].controls.tenderType.value).toBe('Cash');
  });

  it('does not reload residents when open changes to false', () => {
    const component = createComponent();
    component.open = false;
    component.ngOnChanges({ open: new SimpleChange(true, false, false) });

    httpMock.expectNone('/api/properties/prop-1/residents');
  });

  it('save() posts a payment request, toasts, resets, and emits saved + closed', () => {
    const component = createComponent();
    open(component);
    const savedEmitted = vi.fn();
    const closedEmitted = vi.fn();
    component.saved.subscribe(savedEmitted);
    component.closed.subscribe(closedEmitted);

    component['form'].patchValue({
      residentProfileId: 'resident-1',
      paymentDate: '2026-09-01',
      amountPaid: 500,
      tenderType: 'Check',
      referenceNumber: 'CHK-1001',
    });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/payments');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      residentProfileId: 'resident-1',
      paymentDate: '2026-09-01',
      amountPaid: 500,
      tenderType: 'Check',
      referenceNumber: 'CHK-1001',
      notes: null,
    });
    req.flush({ success: true, data: payment, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<PaymentTransactionResponse>);

    expect(toastService.show).toHaveBeenCalledWith('payments.modal.addedToast');
    expect(savedEmitted).toHaveBeenCalled();
    expect(closedEmitted).toHaveBeenCalled();
    expect(component['form'].controls.amountPaid.value).toBe(0);
    expect(component['form'].controls.residentProfileId.value).toBe('');
  });

  it('does not submit an invalid form', () => {
    const component = createComponent();
    open(component); // residentProfileId/paymentDate/amountPaid required, left at defaults

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/payments');
  });

  it('shows an error toast when the server rejects the payment', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ residentProfileId: 'resident-1', paymentDate: '2026-09-01', amountPaid: 500 });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/payments');
    req.flush({ type: 'about:blank', title: 'Bad Request', status: 400 }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.show).toHaveBeenCalledWith('payments.modal.errorToast');
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });
});
