import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { RoleNames } from '../../core/constants/roles';
import { AppHeader } from '../../shared/app-header/app-header';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-dashboard',
  imports: [TranslatePipe, AppHeader, RouterLink],
  templateUrl: './dashboard.html',
})
export class Dashboard {
  private readonly authService = inject(AuthService);

  protected readonly session = this.authService.session;

  /** Mirrors Permissions.Resident.Read's grant list -- see app.routes.ts's matching
   * admin/directory route guard. */
  protected readonly canViewDirectoryAdmin = () => {
    const role = this.authService.role();
    return role === RoleNames.PropertyManager || role === RoleNames.SuperAdmin;
  };
}
