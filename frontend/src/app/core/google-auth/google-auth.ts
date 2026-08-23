/**
 * US-15: Google Sign-In (Google Identity Services).
 *
 * GOOGLE_CLIENT_ID is intentionally empty -- "build it now, get credentials later" (see
 * User_Stories_Phase_5.md). It is NOT a secret (Google OAuth Client IDs are public by
 * design, same as the Turnstile site key), so once a real one exists it can go directly
 * here. Until then, GoogleSignInButton deliberately does not attempt to render at all
 * (an empty client_id would make Google's own script throw) -- see that component.
 */
export const GOOGLE_CLIENT_ID = '';

export interface GoogleCredentialResponse {
  credential: string; // the ID token to verify server-side
}

interface GoogleIdConfiguration {
  client_id: string;
  callback: (response: GoogleCredentialResponse) => void;
}

interface GoogleButtonOptions {
  type?: 'standard' | 'icon';
  theme?: 'outline' | 'filled_blue' | 'filled_black';
  size?: 'large' | 'medium' | 'small';
  text?: 'signin_with' | 'signup_with' | 'continue_with' | 'signin';
  width?: number;
}

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize(config: GoogleIdConfiguration): void;
          renderButton(container: HTMLElement, options: GoogleButtonOptions): void;
        };
      };
    };
    __ten21GoogleReady?: () => void;
  }
}

let readyPromise: Promise<void> | null = null;

/** Resolves once window.google.accounts.id is available -- driven by the GIS script's
 * `onload` query param in index.html, not a timing guess. */
export function googleReady(): Promise<void> {
  if (typeof window === 'undefined') {
    return Promise.resolve();
  }
  if (window.google?.accounts?.id) {
    return Promise.resolve();
  }
  if (!readyPromise) {
    readyPromise = new Promise<void>((resolve) => {
      window.__ten21GoogleReady = () => resolve();
    });
  }
  return readyPromise;
}
