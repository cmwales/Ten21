import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { ChargeResponse } from '../../../core/models/charge.models';
import { ToastService } from '../../../core/services/toast.service';
import { ChargeModal } from './charge-modal';

describe('ChargeModal', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

  const charge: ChargeResponse = {
    id: 'charge-1',
    propertyId: 'prop-1',
    description: 'Trash Violation Fine',
    amount: 75,
    dueDate: '2026-09-15',
    accountingCode: 'GL-4100',
    category: 'LateFee',
    status: 'Active',
    allocatedAmount: 0,
    outstandingAmount: 75,
    paymentStatus: 'Unpaid',
    isLocked: false,
    notes: null,
  };

  function createComponent(): ChargeModal {
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
    const fixture = TestBed.createComponent(ChargeModal);
    const component = fixture.componentInstance;
    component.propertyId = 'prop-1';
    return component;
  }

  function open(component: ChargeModal): void {
    component.open = true;
    component.ngOnChanges({ open: new SimpleChange(false, true, false) });
    httpMock.expectOne('/api/properties/prop-1/charges').flush({
      success: true, data: [charge], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ChargeResponse[]>);
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

    httpMock.expectNone('/api/properties/prop-1/charges');
  });

  it('startAdd() shows an empty form defaulted to AddOn, and save() posts a create request with a category', () => {
    const component = createComponent();
    open(component);

    component['startAdd']();
    expect(component['form'].controls.category.value).toBe('AddOn');
    component['form'].patchValue({ description: 'Pet Rent - September', amount: 50, dueDate: '2026-09-01', category: 'AddOn' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/charges');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.category).toBe('AddOn');
    expect(req.request.body.description).toBe('Pet Rent - September');
    req.flush({ success: true, data: charge, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<ChargeResponse>);

    const reload = httpMock.expectOne('/api/properties/prop-1/charges');
    reload.flush({ success: true, data: [charge], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ChargeResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('charges.modal.addedToast');
    expect(component['showForm']()).toBe(false);
  });

  it('does not submit an invalid form', () => {
    const component = createComponent();
    open(component);
    component['startAdd'](); // description/amount/dueDate required, left blank/zero

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/charges');
  });

  it('deleteCharge() calls DELETE and reloads the list', () => {
    const component = createComponent();
    open(component);

    component['deleteCharge'](charge);

    const req = httpMock.expectOne('/api/properties/prop-1/charges/charge-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    const reload = httpMock.expectOne('/api/properties/prop-1/charges');
    reload.flush({ success: true, data: [], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ChargeResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('charges.modal.removedToast');
  });

  it('deleteCharge() shows a locked-error toast when the server rejects it', () => {
    const component = createComponent();
    open(component);

    component['deleteCharge']({ ...charge, isLocked: true });

    const req = httpMock.expectOne('/api/properties/prop-1/charges/charge-1');
    req.flush({ type: 'about:blank', title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });

    expect(toastService.show).toHaveBeenCalledWith('charges.modal.lockedErrorToast');
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });

  it('voidCharge() calls POST .../void and reloads the list', () => {
    const component = createComponent();
    open(component);

    component['voidCharge'](charge);

    const req = httpMock.expectOne('/api/properties/prop-1/charges/charge-1/void');
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, data: { ...charge, status: 'Voided' }, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ChargeResponse>);

    const reload = httpMock.expectOne('/api/properties/prop-1/charges');
    reload.flush({ success: true, data: [charge], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ChargeResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('charges.modal.voidedToast');
  });

  it('voidCharge() shows a locked-error toast when the server rejects it', () => {
    const component = createComponent();
    open(component);

    component['voidCharge']({ ...charge, isLocked: true });

    const req = httpMock.expectOne('/api/properties/prop-1/charges/charge-1/void');
    req.flush({ type: 'about:blank', title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });

    expect(toastService.show).toHaveBeenCalledWith('charges.modal.lockedErrorToast');
  });

  it('startAdjust() shows a form defaulted to CreditAdjustment, and saveAdjustment() posts against the charge', () => {
    const component = createComponent();
    open(component);

    component['startAdjust'](charge);
    expect(component['adjustingCharge']()).toEqual(charge);
    expect(component['adjustmentForm'].controls.adjustmentType.value).toBe('CreditAdjustment');
    component['adjustmentForm'].patchValue({ amount: 25, reason: 'Goodwill credit for late maintenance' });

    component['saveAdjustment']();

    const req = httpMock.expectOne('/api/properties/prop-1/charges/charge-1/adjustments');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ adjustmentType: 'CreditAdjustment', amount: 25, reason: 'Goodwill credit for late maintenance' });
    req.flush({
      success: true,
      data: { id: 'adj-1', adjustmentType: 'CreditAdjustment', amount: 25, reason: 'Goodwill credit for late maintenance', createdAt: '2026-09-05T00:00:00Z' },
      message: null,
      statusCode: 201,
      traceId: 't1',
    });

    const reload = httpMock.expectOne('/api/properties/prop-1/charges');
    reload.flush({ success: true, data: [charge], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ChargeResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('charges.modal.adjustedToast');
    expect(component['adjustingCharge']()).toBeNull();
  });

  it('does not submit an invalid adjustment form', () => {
    const component = createComponent();
    open(component);
    component['startAdjust'](charge); // amount/reason required, left at defaults

    component['saveAdjustment']();

    httpMock.expectNone('/api/properties/prop-1/charges/charge-1/adjustments');
  });

  it('cancelAdjust() clears the adjusting charge', () => {
    const component = createComponent();
    open(component);
    component['startAdjust'](charge);

    component['cancelAdjust']();

    expect(component['adjustingCharge']()).toBeNull();
  });
});
