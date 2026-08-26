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
});
