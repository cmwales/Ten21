import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  messageKey: string;
}

/** US-19 AC: "Save... displays toast notification." A small, app-wide signal-based toast
 * queue -- the first feature in this codebase that needs one, so it's built minimal
 * (auto-dismiss, no actions/queuing beyond a simple list) rather than pulling in a UI
 * library for one notification type. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly _toasts = signal<Toast[]>([]);
  readonly toasts = this._toasts.asReadonly();
  private nextId = 0;

  show(messageKey: string, durationMs = 4000): void {
    const id = this.nextId++;
    this._toasts.update((toasts) => [...toasts, { id, messageKey }]);
    setTimeout(() => this.dismiss(id), durationMs);
  }

  dismiss(id: number): void {
    this._toasts.update((toasts) => toasts.filter((t) => t.id !== id));
  }
}
