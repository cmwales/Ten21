import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { SecurityDepositResponse } from '../../../core/models/ledger.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ToastService } from '../../../core/services/toast.service';
import { CollectDepositModal } from './collect-deposit-modal';

describe('CollectDepositModal', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<CollectDepositModal>;

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

  const deposit: SecurityDepositResponse = {
    id: 'deposit-1',
    propertyId: 'prop-1',
    residentProfileId: 'resident-1',
    residentName: 'Jamie Rivera',
    originalAmount: 1200,
    amountHeld: 1200,
    collectedDate: '2026-01-01',
    status: 'Held',
  };

  function createComponent(): CollectDepositModal {
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
    fixture = TestBed.createComponent(CollectDepositModal);
    const component = fixture.componentInstance;
    fixture.componentRef.setInput('propertyId', 'prop-1');
    return component;
  }

  function open(_component: CollectDepositModal): void {
    fixture.componentRef.setInput('open', true);
    TestBed.flushEffects();
    httpMock.expectOne('/api/properties/prop-1/residents').flush({
      success: true, data: [resident], message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<ResidentResponse[]>);
  }

  afterEach(() => httpMock.verify());

  it('loads residents for the property when opened, defaulting residentProfileId to blank', () => {
    const component = createComponent();
    open(component);

    expect(component['residents']()).toEqual([resident]);
    expect(component['form'].controls.residentProfileId.value).toBe('');
  });

  it('save() posts a collect request with residentProfileId null when left blank (auto-default)', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ amount: 1200, collectedDate: '2026-01-01' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/deposits');
    expect(req.request.body).toEqual({ amount: 1200, collectedDate: '2026-01-01', residentProfileId: null });
    req.flush({ success: true, data: deposit, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<SecurityDepositResponse>);

    expect(toastService.show).toHaveBeenCalledWith('deposits.modal.collectedToast');
  });

  it('save() posts the explicit residentProfileId when one is selected', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ amount: 1200, collectedDate: '2026-01-01', residentProfileId: 'resident-1' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/deposits');
    expect(req.request.body.residentProfileId).toBe('resident-1');
    req.flush({ success: true, data: deposit, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<SecurityDepositResponse>);
  });

  it('does not submit an invalid form', () => {
    const component = createComponent();
    open(component);

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/deposits');
  });

  it('shows an error toast when the server rejects the request', () => {
    const component = createComponent();
    open(component);
    component['form'].patchValue({ amount: 1200, collectedDate: '2026-01-01' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/deposits');
    req.flush({ type: 'about:blank', title: 'Bad Request', status: 400 }, { status: 400, statusText: 'Bad Request' });

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
