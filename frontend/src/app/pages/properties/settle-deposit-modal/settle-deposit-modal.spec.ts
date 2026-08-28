import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { SecurityDepositResponse } from '../../../core/models/ledger.models';
import { SettleDepositResponse } from '../../../core/services/deposit.service';
import { ToastService } from '../../../core/services/toast.service';
import { SettleDepositModal } from './settle-deposit-modal';

describe('SettleDepositModal', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

  const settledDeposit: SecurityDepositResponse = {
    id: 'deposit-1',
    propertyId: 'prop-1',
    residentProfileId: 'resident-1',
    residentName: 'Jamie Rivera',
    originalAmount: 1200,
    amountHeld: 0,
    collectedDate: '2026-01-01',
    status: 'Settled',
  };

  function createComponent(): SettleDepositModal {
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
    const fixture = TestBed.createComponent(SettleDepositModal);
    const component = fixture.componentInstance;
    fixture.componentRef.setInput('propertyId', 'prop-1');
    fixture.componentRef.setInput('depositId', 'deposit-1');
    fixture.componentRef.setInput('open', true);
    return component;
  }

  afterEach(() => httpMock.verify());

  it('defaults tenderType to Check', () => {
    const component = createComponent();

    expect(component['form'].controls.tenderType.value).toBe('Check');
  });

  it('save() posts a settle request and shows the with-refund toast when a refund was issued', () => {
    const component = createComponent();
    const savedEmitted = vi.fn();
    component.saved.subscribe(savedEmitted);

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/deposits/deposit-1/settle');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ tenderType: 'Check', referenceNumber: null });
    req.flush({
      success: true,
      data: { deposit: settledDeposit, amountAppliedToCharges: 300, amountRefunded: 900 } satisfies SettleDepositResponse,
      message: null,
      statusCode: 200,
      traceId: 't1',
    } satisfies ApiResponse<SettleDepositResponse>);

    expect(toastService.show).toHaveBeenCalledWith('deposits.modal.settledWithRefundToast');
    expect(savedEmitted).toHaveBeenCalled();
  });

  it('shows the no-refund toast when dues consumed the whole deposit', () => {
    const component = createComponent();

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/deposits/deposit-1/settle');
    req.flush({
      success: true,
      data: { deposit: settledDeposit, amountAppliedToCharges: 1200, amountRefunded: 0 } satisfies SettleDepositResponse,
      message: null,
      statusCode: 200,
      traceId: 't1',
    } satisfies ApiResponse<SettleDepositResponse>);

    expect(toastService.show).toHaveBeenCalledWith('deposits.modal.settledNoRefundToast');
  });

  it('shows an error toast when the server rejects the request', () => {
    const component = createComponent();

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/deposits/deposit-1/settle');
    req.flush({ type: 'about:blank', title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });

    expect(toastService.show).toHaveBeenCalledWith('deposits.modal.errorToast');
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });
});
