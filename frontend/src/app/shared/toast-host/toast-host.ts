import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-toast-host',
  imports: [TranslatePipe],
  templateUrl: './toast-host.html',
})
export class ToastHost {
  protected readonly toastService = inject(ToastService);
}
