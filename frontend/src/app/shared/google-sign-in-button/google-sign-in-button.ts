import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  Output,
  ViewChild,
} from '@angular/core';
import { GOOGLE_CLIENT_ID, googleReady } from '../../core/google-auth/google-auth';

/**
 * US-15: renders Google's own "Sign in with Google" button. Deliberately a no-op (renders
 * nothing, no console noise) when GOOGLE_CLIENT_ID is empty -- see that constant's comment.
 * Once a real Client ID is configured, this activates with no other code changes.
 */
@Component({
  selector: 'app-google-sign-in-button',
  template: '<div #container></div>',
})
export class GoogleSignInButton implements AfterViewInit {
  @ViewChild('container') private container?: ElementRef<HTMLDivElement>;
  @Output() readonly credential = new EventEmitter<string>();

  async ngAfterViewInit(): Promise<void> {
    if (!GOOGLE_CLIENT_ID || !this.container) {
      return;
    }

    await googleReady();
    if (!window.google) {
      return;
    }

    window.google.accounts.id.initialize({
      client_id: GOOGLE_CLIENT_ID,
      callback: (response) => this.credential.emit(response.credential),
    });

    window.google.accounts.id.renderButton(this.container.nativeElement, {
      type: 'standard',
      theme: 'outline',
      size: 'large',
      text: 'continue_with',
      width: 320,
    });
  }
}
