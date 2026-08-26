import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { PdfViewer } from './pdf-viewer';

describe('PdfViewer', () => {
  let httpMock: HttpTestingController;
  let router: { navigate: ReturnType<typeof vi.fn> };

  function createComponent(queryParams: Record<string, string> = { type: 'statement' }): PdfViewer {
    router = { navigate: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (key: string) => (key === 'id' ? 'prop-1' : null) },
              queryParamMap: { get: (key: string) => queryParams[key] ?? null },
            },
          },
        },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PdfViewer);
    return fixture.componentInstance;
  }

  afterEach(() => httpMock.verify());

  it('loads the statement PDF by default and exposes a sanitized blob URL', () => {
    const component = createComponent({ type: 'statement' });
    component.ngOnInit();

    const req = httpMock.expectOne('/api/properties/prop-1/charges/statement/pdf?range=Lifetime');
    expect(req.request.method).toBe('GET');
    req.flush(new Blob(['%PDF-1.7 fake'], { type: 'application/pdf' }));

    expect(component['isStatement']()).toBe(true);
    expect(component['pdfUrl']()).not.toBeNull();
    expect(component['loading']()).toBe(false);
  });

  it('loads the receipt PDF when type=receipt and paymentId is provided', () => {
    const component = createComponent({ type: 'receipt', paymentId: 'payment-1' });
    component.ngOnInit();

    const req = httpMock.expectOne('/api/properties/prop-1/payments/payment-1/receipt');
    req.flush(new Blob(['%PDF-1.7 fake'], { type: 'application/pdf' }));

    expect(component['isStatement']()).toBe(false);
    expect(component['pdfUrl']()).not.toBeNull();
  });

  it('sets an error when type=receipt but no paymentId is provided', () => {
    const component = createComponent({ type: 'receipt' });
    component.ngOnInit();

    httpMock.expectNone('/api/properties/prop-1/payments//receipt');
    expect(component['errorKey']()).toBe('pdfViewer.loadError');
    expect(component['loading']()).toBe(false);
  });

  it('sets an error when the PDF request fails', () => {
    const component = createComponent({ type: 'statement' });
    component.ngOnInit();

    const req = httpMock.expectOne('/api/properties/prop-1/charges/statement/pdf?range=Lifetime');
    req.flush(null, { status: 500, statusText: 'Server Error' });

    expect(component['errorKey']()).toBe('pdfViewer.loadError');
    expect(component['loading']()).toBe(false);
  });

  it('onRangeChange() updates the query params and reloads with the new range', () => {
    const component = createComponent({ type: 'statement' });
    component.ngOnInit();
    httpMock.expectOne('/api/properties/prop-1/charges/statement/pdf?range=Lifetime')
      .flush(new Blob(['%PDF-1.7 fake'], { type: 'application/pdf' }));

    component['onRangeChange']('YearToDate');

    expect(router.navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({ queryParams: { type: 'statement', range: 'YearToDate' } }),
    );
    httpMock.expectOne('/api/properties/prop-1/charges/statement/pdf?range=YearToDate')
      .flush(new Blob(['%PDF-1.7 fake'], { type: 'application/pdf' }));
  });
});
