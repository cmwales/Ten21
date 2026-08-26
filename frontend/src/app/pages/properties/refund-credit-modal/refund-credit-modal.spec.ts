import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { RefundTransactionResponse } from '../../../core/models/ledger.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ToastService } from '../../../core/services/toast.service';
import { RefundCreditModal } from './refund-credit-modal';

describe('RefundCreditModal', () => {
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

  const refund: RefundTransactionResponse = {
    id: 'refund-1',
    residentProfileId: 'resident-1',
    residentName: 'Jamie Rivera',
    propertyId: 'prop-1',
    amount: 75,
    refundDate: '2026-09-10',
    tenderType: 'Check',
    referenceNumber: 'CHK-2001',
    reason: 'OverpaymentRefund',
    createdAt: '2026-09-10T00:00:00Z',
  };

  function createComponent(): RefundCreditModal {
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
    const fixture = TestBed.createComponent(RefundCreditModal);
    const component = fixture.componentInstance;
    component.propertyId = 'prop-1';
    return component;
  }

  function open(component: RefundCreditModal): void {
    component.open = true;
    component.ngOnChanges({ open: new SimpleChange(false, true, false) });
    httpMock.expectOne('/api/properties/prop-1/residents').flush({
      success: true, data: [resident], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ResidentResponse[]>);
  }

  afterEach(() => httpMock.verify());

  it('loads residents for the property when opened', () => {
    const component = createComponent();
    open(component);

    expect(component['residents']()).toEqual([resident]);
  });

  it('save() posts a refund request, toasts, resets, and emits saved + closed', () => {
    const component = createComponent();
    open(component);
    const savedEmitted = vi.fn();
    const closedEmitted = vi.fn();
    component.saved.subscribe(savedEmitted);
    component.closed.subscribe(closedEmitted);

    component['form'].patchValue({
      residentProfileId: 'resident-1',
      amount: 75,
      refundDate: '2026-09-10',
      tenderType: 'Check',
      referenceNumber: 'CHK-2001',
    });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/refunds');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      residentProfileId: 'resident-1',
      amount: 75,
      refundDate: '2026-09-10',
      tenderType: 'Check',
      referenceNumber: 'CHK-2001',
    });
    req.flush({ success: true, data: refund, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<RefundTransactionResponse>);

    expect(toastService.show).toHaveBeenCalledWith('refunds.modal.addedToast');
    expect(savedEmitted).toHaveBeenCalled();
    expect(closedEmitted).toHaveBeenCalled();
    expect(component['form'].controls.amount.value).toBe(0);
  });

  it('does not submit an invalid form', () => {
    const component = createComponent();
    open(component);

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/refunds');
  });

  it('shows an insufficient-credit toast when the server returns 409', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ residentProfileId: 'resident-1', amount: 500, refundDate: '2026-09-10' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/refunds');
    req.flush({ type: 'about:blank', title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });

    expect(toastService.show).toHaveBeenCalledWith('refunds.modal.insufficientCreditToast');
  });

  it('shows a generic error toast on other failures', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ residentProfileId: 'resident-1', amount: 75, refundDate: '2026-09-10' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/refunds');
    req.flush({ type: 'about:blank', title: 'Bad Request', status: 400 }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.show).toHaveBeenCalledWith('refunds.modal.errorToast');
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });
});
