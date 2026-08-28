import { Overlay, OverlayRef, OverlayPositionBuilder } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { Directive, ElementRef, HostListener, Input, OnDestroy, inject } from '@angular/core';
import { TooltipContent } from './tooltip-content';

/**
 * Audit Refinement Sprint (Property Form UX): a CDK Overlay-based tooltip, used to
 * disambiguate the "Apply" vs "Save" header actions on the property form
 * ("Save changes and stay on page" / "Save changes and return to list") without adding a
 * third button or shrinking the existing header layout. Shows on hover AND keyboard focus
 * (not just :hover) so the disambiguating text is still available to keyboard/screen-reader
 * users, not only mouse users.
 */
@Directive({
  selector: '[appTooltip]',
})
export class TooltipDirective implements OnDestroy {
  @Input('appTooltip') text = '';

  private readonly overlay = inject(Overlay);
  private readonly overlayPositionBuilder = inject(OverlayPositionBuilder);
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private overlayRef: OverlayRef | null = null;

  @HostListener('mouseenter')
  @HostListener('focus')
  show(): void {
    if (!this.text || this.overlayRef?.hasAttached()) {
      return;
    }

    if (!this.overlayRef) {
      const positionStrategy = this.overlayPositionBuilder
        .flexibleConnectedTo(this.elementRef)
        .withPositions([
          { originX: 'center', originY: 'bottom', overlayX: 'center', overlayY: 'top', offsetY: 8 },
          { originX: 'center', originY: 'top', overlayX: 'center', overlayY: 'bottom', offsetY: -8 },
        ]);
      this.overlayRef = this.overlay.create({
        positionStrategy,
        scrollStrategy: this.overlay.scrollStrategies.reposition(),
      });
    }

    const componentRef = this.overlayRef.attach(new ComponentPortal(TooltipContent));
    componentRef.instance.text = this.text;
  }

  @HostListener('mouseleave')
  @HostListener('blur')
  hide(): void {
    this.overlayRef?.detach();
  }

  ngOnDestroy(): void {
    this.overlayRef?.dispose();
  }
}
