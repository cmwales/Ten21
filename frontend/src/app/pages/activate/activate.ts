import { Component, inject, signal } from '@angular/core';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { LanguageSelector } from '../../shared/language-selector/language-selector';

type ActivationState = 'pending' | 'success' | 'error';

/** US-16: the landing page for an activation link's URL (?userId=&token=). Fires the
 * confirmation call automatically on load -- there's nothing for the user to fill in. */
@Component({
  selector: 'app-activate',
  imports: [RouterLink, TranslatePipe, LanguageSelector],
  templateUrl: './activate.html',
})
export class Activate {
  private readonly route = inject(ActivatedRoute);
  private readonly authService = inject(AuthService);

  protected readonly state = signal<ActivationState>('pending');

  constructor() {
    const userId = this.route.snapshot.queryParamMap.get('userId');
    const token = this.route.snapshot.queryParamMap.get('token');

    if (!userId || !token) {
      this.state.set('error');
      return;
    }

    this.authService.activate(userId, token).subscribe({
      next: () => this.state.set('success'),
      error: () => this.state.set('error'),
    });
  }
}
