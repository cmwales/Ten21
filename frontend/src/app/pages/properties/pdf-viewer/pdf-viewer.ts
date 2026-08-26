import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { TranslatePipe } from '@ngx-translate/core';
import { StatementDateRangeValue, StatementDateRanges } from '../../../core/models/ledger.models';
import { ChargeService } from '../../../core/services/charge.service';
import { PaymentService } from '../../../core/services/payment.service';
import { AppHeader } from '../../../shared/app-header/app-header';

/**
 * US-40: renders a payment receipt or a unit statement as an embedded PDF -- one page for
 * both, distinguished by the `type` query param (`statement` | `receipt`). Deliberately just
 * fetches the PDF bytes and drops them into an &lt;iframe&gt;, rather than building a second
 * HTML rendering of the same data: the browser's own PDF viewer already supplies zoom/print/
 * download controls, so there's nothing this page needs to reimplement.
 */
@Component({
  selector: 'app-pdf-viewer',
  imports: [TranslatePipe, RouterLink, AppHeader],
  templateUrl: './pdf-viewer.html',
})
export class PdfViewer implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly chargeService = inject(ChargeService);
  private readonly paymentService = inject(PaymentService);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly propertyId = signal('');
  protected readonly pdfUrl = signal<SafeResourceUrl | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly isStatement = signal(true);
  protected readonly range = signal<StatementDateRangeValue>(StatementDateRanges.Lifetime);
  protected readonly ranges = Object.values(StatementDateRanges);

  private objectUrl: string | null = null;

  ngOnInit(): void {
    const propertyId = this.route.snapshot.paramMap.get('id');
    const type = this.route.snapshot.queryParamMap.get('type');
    const paymentId = this.route.snapshot.queryParamMap.get('paymentId');
    const requestedRange = this.route.snapshot.queryParamMap.get('range') as StatementDateRangeValue | null;

    if (!propertyId || (type === 'receipt' && !paymentId)) {
      this.errorKey.set('pdfViewer.loadError');
      this.loading.set(false);
      return;
    }

    this.propertyId.set(propertyId);
    this.isStatement.set(type !== 'receipt');
    if (requestedRange) {
      this.range.set(requestedRange);
    }

    if (this.isStatement()) {
      this.loadStatement();
    } else {
      this.loadReceipt(paymentId!);
    }
  }

  ngOnDestroy(): void {
    this.revokeObjectUrl();
  }

  protected onRangeChange(range: StatementDateRangeValue): void {
    this.range.set(range);
    this.router.navigate([], { relativeTo: this.route, queryParams: { type: 'statement', range }, queryParamsHandling: 'merge' });
    this.loadStatement();
  }

  private loadStatement(): void {
    this.loading.set(true);
    this.chargeService.getStatementPdf(this.propertyId(), this.range()).subscribe({
      next: (blob) => this.showBlob(blob),
      error: () => this.showError(),
    });
  }

  private loadReceipt(paymentId: string): void {
    this.loading.set(true);
    this.paymentService.getReceipt(this.propertyId(), paymentId).subscribe({
      next: (blob) => this.showBlob(blob),
      error: () => this.showError(),
    });
  }

  private showBlob(blob: Blob): void {
    this.revokeObjectUrl();
    this.objectUrl = URL.createObjectURL(blob);
    this.pdfUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.objectUrl));
    this.loading.set(false);
  }

  private showError(): void {
    this.loading.set(false);
    this.errorKey.set('pdfViewer.loadError');
  }

  private revokeObjectUrl(): void {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
  }
}
