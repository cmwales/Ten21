/**
 * US-18: Cloudflare Turnstile bot defense on the registration form.
 *
 * The site key is intentionally NOT a secret -- it's meant to be embedded in every page
 * that renders the widget (Cloudflare's own docs put it directly in HTML markup). The
 * matching secret key never leaves the backend (Turnstile:SecretKey via `dotnet
 * user-secrets`, verified server-side in TurnstileVerificationService).
 */
export const TURNSTILE_SITE_KEY = '0x4AAAAAAEZWdaEWEjBwAHJC';

export interface TurnstileRenderOptions {
  sitekey: string;
  action?: string;
  callback?: (token: string) => void;
  'error-callback'?: () => void;
  'expired-callback'?: () => void;
}

declare global {
  interface Window {
    turnstile?: {
      render(container: HTMLElement, options: TurnstileRenderOptions): string;
      reset(widgetId: string): void;
      remove(widgetId: string): void;
    };
    __ten21TurnstileReady?: () => void;
  }
}

let readyPromise: Promise<void> | null = null;

/** Resolves once window.turnstile is available -- driven by the api.js `onload` query
 * param in index.html, not a timing guess. */
export function turnstileReady(): Promise<void> {
  if (typeof window === 'undefined') {
    return Promise.resolve();
  }
  if (window.turnstile) {
    return Promise.resolve();
  }
  if (!readyPromise) {
    readyPromise = new Promise<void>((resolve) => {
      window.__ten21TurnstileReady = () => resolve();
    });
  }
  return readyPromise;
}
