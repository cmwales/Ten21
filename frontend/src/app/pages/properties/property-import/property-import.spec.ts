import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../../core/models/auth.models';
import { ImportPropertiesResponse } from '../../../core/models/property.models';
import { PropertyImport } from './property-import';

describe('PropertyImport', () => {
  let httpMock: HttpTestingController;

  function createComponent(): PropertyImport {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' })],
    });
    httpMock = TestBed.inject(HttpTestingController);
    return TestBed.createComponent(PropertyImport).componentInstance;
  }

  function fileOf(name: string, sizeBytes = 100): File {
    return new File([new Uint8Array(sizeBytes)], name);
  }

  afterEach(() => httpMock.verify());

  it('rejects an unsupported extension before ever uploading', () => {
    const component = createComponent();
    const input = { files: [fileOf('properties.pdf')] } as unknown as HTMLInputElement;

    component['onFileSelected']({ target: input } as unknown as Event);

    expect(component['errorKey']()).toBe('properties.import.invalidExtensionError');
    httpMock.expectNone('/api/properties/import');
  });

  it('rejects a file over the 10MB limit before ever uploading', () => {
    const component = createComponent();
    const input = { files: [fileOf('properties.csv', 11 * 1024 * 1024)] } as unknown as HTMLInputElement;

    component['onFileSelected']({ target: input } as unknown as Event);

    expect(component['errorKey']()).toBe('properties.import.tooLargeError');
    httpMock.expectNone('/api/properties/import');
  });

  it('uploads a valid file and stores the response for the preview grid', () => {
    const component = createComponent();
    const input = { files: [fileOf('properties.csv')] } as unknown as HTMLInputElement;

    component['onFileSelected']({ target: input } as unknown as Event);

    const req = httpMock.expectOne('/api/properties/import');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);

    const response: ImportPropertiesResponse = {
      success: true,
      totalRows: 1,
      invalidRowCount: 0,
      propertiesCreated: 1,
      rows: [
        {
          rowNumber: 2,
          propertyName: 'Riverside Apartments',
          propertyType: 'MultiFamily',
          streetAddress1: '100 Main St',
          city: 'Provo',
          state: 'UT',
          postalCode: '84601',
          country: 'USA',
          unitIdentifier: '101',
          targetRent: '1200',
          isValid: true,
          errors: [],
        },
      ],
    };
    req.flush({ success: true, data: response, message: null, statusCode: 200, traceId: 't1' } satisfies ApiResponse<ImportPropertiesResponse>);

    expect(component['result']()).toEqual(response);
    expect(component['uploading']()).toBe(false);
  });

  it('resets back to the dropzone', () => {
    const component = createComponent();
    component['result'].set({
      success: true,
      totalRows: 0,
      invalidRowCount: 0,
      propertiesCreated: 0,
      rows: [],
    });

    component['reset']();

    expect(component['result']()).toBeNull();
    expect(component['errorKey']()).toBeNull();
  });
});
