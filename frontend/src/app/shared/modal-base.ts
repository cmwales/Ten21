import { Directive, effect, input, output } from '@angular/core';

/**
 * Audit Refinement Sprint: shared open/close contract and "run this when opened" lifecycle,
 * extracted after an audit found 9 near-identical modal/drawer components independently
 * reimplementing the same `@Input() open`, `@Output() closed`, and
 * `OnChanges.ngOnChanges(changes) { if (changes['open'] && this.open) { ...reset form / load
 * data... } }` shape. Subclasses override onOpen() instead of implementing OnChanges
 * themselves.
 *
 * Also the migration to signal-based input()/output() the audit asked for -- template
 * bindings ([open], (closed)) are unaffected either way; Angular resolves a signal input/
 * output identically to a decorator-based one from the calling template's perspective, and
 * signal inputs/outputs declared on a base class ARE inherited by an extending @Component,
 * the same way decorator-based ones always were.
 *
 * Not itself a @Directive/@Component -- it's a plain abstract base class. Angular's DI
 * resolves `effect()` (and any inject() calls) here correctly because construction always
 * happens through the concrete @Component subclass's own injector context.
 */
@Directive()
export abstract class ModalBase {
  readonly open = input(false);
  readonly closed = output<void>();

  constructor() {
    effect(() => {
      if (this.open()) {
        this.onOpen();
      }
    });
  }

  /** Runs every time `open` transitions to (or is initially set) true -- override to reset a
   * form and/or load data, mirroring the old ngOnChanges(['open']) guard. */
  protected onOpen(): void {}

  protected close(): void {
    this.closed.emit();
  }
}
