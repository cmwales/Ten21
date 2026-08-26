import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { ManualChargeResponse } from '../../../core/models/manual-charge.models';
import { ToastService } from '../../../core/services/toast.service';
import { ManualChargeModal } from './manual-charge-modal';

describe('ManualChargeModal', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

  const charge: ManualChargeResponse = {
    id: 'charge-1',
    propertyId: 'prop-1',
    description: 'Trash Violation Fine',
    amount: 75,
    dueDate: '2026-09-15',
    accountingCode: 'GL-4100',
    paidDate: null,
  };

  function createComponent(): ManualChargeModal {
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
  }

  afterEach(() => httpMock.verify());

  it('loads charges for the property when opened', () => {
    const component = createComponent();
    open(component);

    expect(component['charges']()).toEqual([charge]);
  });

  it('does not reload when open changes to false', () => {
    const component = createComponent();
    component.open = false;
    component.ngOnChanges({ open: new SimpleChange(true, false, false) });

    httpMock.expectNone('/api/properties/prop-1/manual-charges');
  });

  it('startAdd() shows an empty form, and save() posts a create request billed to the unit', () => {
    const component = createComponent();
    open(component);

    component['startAdd']();
    component['form'].patchValue({ description: 'Trash Violation Fine', amount: 75, dueDate: '2026-09-15' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/manual-charges');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.description).toBe('Trash Violation Fine');
    expect(req.request.body.paidDate).toBeNull();
    req.flush({ success: true, data: charge, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<ManualChargeResponse>);

    const reload = httpMock.expectOne('/api/properties/prop-1/manual-charges');
    reload.flush({ success: true, data: [charge], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<
      ManualChargeResponse[]
    >);

    expect(toastService.show).toHaveBeenCalledWith('manualCharges.modal.addedToast');
    expect(component['showForm']()).toBe(false);
  });

  it('markPaidToday() PUTs the charge with a PaidDate of today', () => {
    const component = createComponent();
    open(component);

    component['markPaidToday'](charge);

    const req = httpMock.expectOne('/api/properties/prop-1/manual-charges/charge-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.paidDate).toBe(new Date().toISOString().substring(0, 10));
    expect(req.request.body.description).toBe(charge.description);
    req.flush({
      success: true, data: { ...charge, paidDate: req.request.body.paidDate }, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ManualChargeResponse>);

    expect(component['charges']()[0].paidDate).toBe(req.request.body.paidDate);
    expect(component['savingPaidDateForChargeId']()).toBeNull();
  });

  it('onPaidDateChange() PUTs an explicit paid date (paid Monday, entered later)', () => {
    const component = createComponent();
    open(component);

    component['onPaidDateChange'](charge, '2026-09-14');

    const req = httpMock.expectOne('/api/properties/prop-1/manual-charges/charge-1');
    expect(req.request.body.paidDate).toBe('2026-09-14');
    req.flush({
      success: true, data: { ...charge, paidDate: '2026-09-14' }, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ManualChargeResponse>);
  });

  it('onPaidDateChange() with an empty value clears the paid date', () => {
    const paidCharge = { ...charge, paidDate: '2026-09-14' };
    const component = createComponent();
    component.open = true;
    component.ngOnChanges({ open: new SimpleChange(false, true, false) });
    httpMock.expectOne('/api/properties/prop-1/manual-charges').flush({
      success: true, data: [paidCharge], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ManualChargeResponse[]>);

    component['onPaidDateChange'](paidCharge, '');

    const req = httpMock.expectOne('/api/properties/prop-1/manual-charges/charge-1');
    expect(req.request.body.paidDate).toBeNull();
    req.flush({ success: true, data: charge, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ManualChargeResponse>);
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
