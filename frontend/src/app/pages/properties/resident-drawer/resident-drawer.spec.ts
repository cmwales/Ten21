import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ToastService } from '../../../core/services/toast.service';
import { ResidentDrawer } from './resident-drawer';

describe('ResidentDrawer', () => {
  let httpMock: HttpTestingController;
  let toastService: { show: ReturnType<typeof vi.fn> };

  const resident: ResidentResponse = {
    id: 'resident-1',
    propertyId: 'prop-1',
    userId: null,
    occupantType: 'Primary',
    firstName: 'Jamie',
    lastName: 'Rivera',
    email: 'jamie@example.com',
    phoneNumber: '555-0100',
    forwardingAddress: null,
    noticeGivenDate: null,
    showInDirectory: false,
    emergencyContacts: [{ id: 'contact-1', name: 'Alex Rivera', phoneNumber: '555-0101', relationship: 'Spouse' }],
  };

  function createComponent(): ResidentDrawer {
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
    const fixture = TestBed.createComponent(ResidentDrawer);
    const component = fixture.componentInstance;
    component.propertyId = 'prop-1';
    return component;
  }

  afterEach(() => httpMock.verify());

  it('loads residents for the property when opened', () => {
    const component = createComponent();
    component.open = true;
    component.ngOnChanges({ open: new SimpleChange(false, true, false) });

    const req = httpMock.expectOne('/api/properties/prop-1/residents');
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [resident], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<
      ResidentResponse[]
    >);

    expect(component['residents']()).toEqual([resident]);
  });

  it('does not reload when open changes to false', () => {
    const component = createComponent();
    component.open = false;
    component.ngOnChanges({ open: new SimpleChange(true, false, false) });

    httpMock.expectNone('/api/properties/prop-1/residents');
  });

  it('startAdd() shows an empty form, and save() posts a create request', () => {
    const component = createComponent();
    component['startAdd']();
    component['form'].patchValue({ firstName: 'Jamie', lastName: 'Rivera', email: 'jamie@example.com' });

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/residents');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.firstName).toBe('Jamie');
    expect(req.request.body.occupantType).toBe('Primary');
    expect(req.request.body.emergencyContacts).toEqual([]);
    req.flush({ success: true, data: resident, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<ResidentResponse>);

    // save() reloads the list after a successful create.
    const reload = httpMock.expectOne('/api/properties/prop-1/residents');
    reload.flush({ success: true, data: [resident], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<
      ResidentResponse[]
    >);

    expect(toastService.show).toHaveBeenCalledWith('residents.drawer.addedToast');
    expect(component['showForm']()).toBe(false);
  });

  it('startEdit() populates the form including emergency contacts, and save() puts an update', () => {
    const component = createComponent();
    component['startEdit'](resident);

    expect(component['form'].controls.firstName.value).toBe('Jamie');
    expect(component['form'].controls.emergencyContacts.length).toBe(1);
    expect(component['form'].controls.emergencyContacts.at(0).controls.name.value).toBe('Alex Rivera');

    component['save']();

    const req = httpMock.expectOne('/api/properties/prop-1/residents/resident-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.emergencyContacts).toEqual([{ name: 'Alex Rivera', phoneNumber: '555-0101', relationship: 'Spouse' }]);
    req.flush({ success: true, data: resident, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ResidentResponse>);

    const reload = httpMock.expectOne('/api/properties/prop-1/residents');
    reload.flush({ success: true, data: [resident], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<
      ResidentResponse[]
    >);

    expect(toastService.show).toHaveBeenCalledWith('residents.drawer.savedToast');
  });

  it('does not submit an invalid form', () => {
    const component = createComponent();
    component['startAdd'](); // firstName/lastName required, left blank

    component['save']();

    httpMock.expectNone('/api/properties/prop-1/residents');
  });

  it('addEmergencyContactRow()/removeEmergencyContactRow() add and remove form rows', () => {
    const component = createComponent();
    component['startAdd']();

    component['addEmergencyContactRow']();
    expect(component['form'].controls.emergencyContacts.length).toBe(1);

    component['removeEmergencyContactRow'](0);
    expect(component['form'].controls.emergencyContacts.length).toBe(0);
  });

  it('deleteResident() calls DELETE and reloads the list', () => {
    const component = createComponent();

    component['deleteResident'](resident);

    const req = httpMock.expectOne('/api/properties/prop-1/residents/resident-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    const reload = httpMock.expectOne('/api/properties/prop-1/residents');
    reload.flush({ success: true, data: [], message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ResidentResponse[]>);

    expect(toastService.show).toHaveBeenCalledWith('residents.drawer.removedToast');
  });

  it('close() emits the closed output', () => {
    const component = createComponent();
    const emitted = vi.fn();
    component.closed.subscribe(emitted);

    component['close']();

    expect(emitted).toHaveBeenCalled();
  });
});
