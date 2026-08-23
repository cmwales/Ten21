import { TestBed } from '@angular/core/testing';
import { ComponentWithUnsavedChanges, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard', () => {
  function runGuard(hasUnsavedChanges: boolean) {
    const component: ComponentWithUnsavedChanges = { hasUnsavedChanges: () => hasUnsavedChanges };
    return TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(component, null as never, null as never, null as never),
    );
  }

  it('allows navigation when there are no unsaved changes', () => {
    expect(runGuard(false)).toBe(true);
  });

  it('prompts for confirmation when there are unsaved changes, honoring the answer', () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    expect(runGuard(true)).toBe(true);
    expect(confirmSpy).toHaveBeenCalled();

    confirmSpy.mockReturnValue(false);
    expect(runGuard(true)).toBe(false);
  });
});
