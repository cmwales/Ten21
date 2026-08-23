import { CanDeactivateFn } from '@angular/router';

/** A component that wants Cancel/browser-back navigation guarded against losing unsaved
 * form state implements this and the router calls it before leaving the route. */
export interface ComponentWithUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * US-19 AC: "Cancel evaluates CanDeactivate guard to warn of unsaved changes and exits."
 * Generic (not PropertyFormContainer-specific) so any future form page can reuse it by
 * implementing ComponentWithUnsavedChanges -- same reasoning as denyRolesGuard being a
 * factory rather than one-off guards per route.
 */
export const unsavedChangesGuard: CanDeactivateFn<ComponentWithUnsavedChanges> = (component) => {
  if (!component.hasUnsavedChanges()) {
    return true;
  }

  // A native confirm() is the simplest reliable cross-browser "are you sure" prompt for a
  // navigation guard; a custom modal would need its own async CanDeactivate plumbing for
  // marginal UX gain over this.
  return confirm('You have unsaved changes. Leave this page and discard them?');
};
