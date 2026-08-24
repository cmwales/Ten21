import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { PropertyResponse } from '../../../core/models/property.models';
import { ToastService } from '../../../core/services/toast.service';
import { PropertyFormContainer } from './property-form-container';

describe('PropertyFormContainer', () => {
  let httpMock: HttpTestingController;
  let router: { navigateByUrl: ReturnType<typeof vi.fn>; navigate: ReturnType<typeof vi.fn> };
  let toastService: { show: ReturnType<typeof vi.fn> };

  const property: PropertyResponse = {
    id: 'prop-1',
    name: 'Riverside Apartments - Suite A',
    propertyType: 'MultiFamily',
    streetAddress1: '100 Main St',
    streetAddress2: null,
    city: 'Provo',
    state: 'UT',
    postalCode: '84601',
    country: 'USA',
    unitIdentifier: 'Suite A',
    targetRent: 1200,
    occupancyStatus: 'Vacant',
  };

  function createComponent(routeId: string | null): PropertyFormContainer {
    router = { navigateByUrl: vi.fn(), navigate: vi.fn() };
    toastService = { show: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: Router, useValue: router },
        { provide: ToastService, useValue: toastService },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => routeId } } } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PropertyFormContainer);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  function flushGetProperty(): void {
    httpMock
      .expectOne('/api/properties/prop-1')
      .flush({ success: true, data: property, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<PropertyResponse>);
  }

  afterEach(() => httpMock.verify());

  it('starts with an empty flat form in create mode', () => {
    const component = createComponent(null);
    expect(component['propertyId']()).toBeNull();
    expect(component.hasUnsavedChanges()).toBe(false);
  });

  it('does not submit an invalid form', () => {
    const component = createComponent(null);
    component['save']();
    httpMock.expectNone('/api/properties');
  });

  it('save() posts the flat form, shows a toast, and navigates to the property list', () => {
    const component = createComponent(null);
    component['form'].patchValue({
      name: 'Riverside Apartments - Suite A',
      streetAddress1: '100 Main St',
      city: 'Provo',
      state: 'UT',
      postalCode: '84601',
      unitIdentifier: 'Suite A',
      targetRent: 1200,
    });

    component['save']();

    const req = httpMock.expectOne('/api/properties');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.name).toBe('Riverside Apartments - Suite A');
    expect(req.request.body.unitIdentifier).toBe('Suite A');
    expect(req.request.body.units).toBeUndefined();
    req.flush({ success: true, data: property, message: null, statusCode: 201, traceId: 't1' } satisfies ApiResponse<PropertyResponse>);

    expect(toastService.show).toHaveBeenCalledWith('properties.form.savedToast');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/properties');
    expect(component.hasUnsavedChanges()).toBe(false);
  });

  it('edit mode loads the existing property, including its unit identifier', () => {
    const component = createComponent('prop-1');
    flushGetProperty();

    expect(component['form'].controls.name.value).toBe('Riverside Apartments - Suite A');
    expect(component['form'].controls.unitIdentifier.value).toBe('Suite A');
    expect(component['form'].controls.targetRent.value).toBe(1200);
    expect(component.hasUnsavedChanges()).toBe(false);
  });

  it('apply() puts the update and keeps the user on the page (replaceUrl)', () => {
    const component = createComponent('prop-1');
    flushGetProperty();

    component['apply']();

    const req = httpMock.expectOne('/api/properties/prop-1');
    expect(req.request.method).toBe('PUT');
    req.flush({ success: true, data: property, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<PropertyResponse>);

    expect(router.navigate).toHaveBeenCalledWith(['/properties', 'prop-1'], { replaceUrl: true });
  });
});
