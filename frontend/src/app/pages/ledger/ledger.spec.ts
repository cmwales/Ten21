import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiResponse } from '../../core/models/auth.models';
import { WorkspaceLedgerResponse } from '../../core/models/workspace-ledger.models';
import { Ledger } from './ledger';

describe('Ledger', () => {
  let httpMock: HttpTestingController;

  const workspaceLedger: WorkspaceLedgerResponse = {
    totalBalance: 1500,
    properties: [
      { propertyId: 'prop-1', propertyName: 'Riverside A', unitIdentifier: 'Unit 1', balance: 1000 },
      { propertyId: 'prop-2', propertyName: 'Riverside B', unitIdentifier: null, balance: 500 },
    ],
  };

  function createComponent(): Ledger {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ lang: 'en-US', fallbackLang: 'en-US' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Ledger);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  afterEach(() => httpMock.verify());

  it('loads the workspace ledger and exposes it', () => {
    const component = createComponent();

    httpMock.expectOne('/api/workspace/ledger').flush({
      success: true, data: workspaceLedger, message: null, statusCode: 200, traceId: 't1',
    } satisfies ApiResponse<WorkspaceLedgerResponse>);

    expect(component['ledger']()).toEqual(workspaceLedger);
    expect(component['loading']()).toBe(false);
  });

  it('sets an error and stops loading when the request fails', () => {
    const component = createComponent();

    httpMock.expectOne('/api/workspace/ledger').flush(
      { type: 'about:blank', title: 'Server Error', status: 500 },
      { status: 500, statusText: 'Server Error' },
    );

    expect(component['loading']()).toBe(false);
    expect(component['errorKey']()).toBe('ledger.loadError');
  });
});
