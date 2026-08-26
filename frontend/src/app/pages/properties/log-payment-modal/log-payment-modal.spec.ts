import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { PaymentTransactionResponse } from '../../../core/models/ledger.models';
import { ToastService } from '../../../core/services/toast.service';
import { LogPaymentModal } from './log-payment-modal';

describe('LogPaymentModal', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

  const payment: PaymentTransactionResponse = {
    id: 'payment-1',
    propertyId: 'prop-1',
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
    component.open = true;
    return component;
  }

  afterEach(() => httpMock.verify());

  it('defaults tenderType to Cash', () => {
    const component = createComponent();

    expect(component['form'].controls.tenderType.value).toBe('Cash');
  });

  it('save() posts a payment request, toasts, resets, and emits saved + closed', () => {
    const component = createComponent();
    const savedEmitted = vi.fn();
    const closedEmitted = vi.fn();
    component.saved.subscribe(savedEmitted);
    component.closed.subscribe(closedEmitted);

    component['form'].patchValue({
      paymentDate: '2026-09-01',
      amountPaid: 500,
      tenderType: 'Check',
      referenceNumber: 'CHK-1001',
    });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/payments');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
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
  });

  it('does not submit an invalid form', () => {
    const component = createComponent();

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/payments');
  });

  it('shows an error toast when the server rejects the payment', () => {
    const component = createComponent();
    component['form'].patchValue({ paymentDate: '2026-09-01', amountPaid: 500 });

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
