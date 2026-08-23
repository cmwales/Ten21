import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';
import { RoleNames } from '../../core/constants/roles';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './dashboard.html',
})
export class Dashboard {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly session = this.authService.session;
  protected readonly isTenant = () => this.authService.role() === RoleNames.Tenant;
  protected readonly canManageProperties = () => {
    const role = this.authService.role();
    return role !== null && role !== RoleNames.Tenant && role !== RoleNames.Vendor;
  };

  protected logout(): void {
    this.authService.logout().subscribe(() => void this.router.navigateByUrl('/login'));
  }
}
