import { Component, Input } from '@angular/core';

/**
 * Audit Refinement Sprint: the actual tooltip bubble rendered inside the CDK overlay by
 * TooltipDirective. Plain Tailwind classes matching the app's dark-surface popover
 * convention (Slate Navy fill, white text) -- this codebase has no Angular Material, so the
 * "Angular CDK tooltip" is built on @angular/cdk/overlay primitives directly rather than
 * @angular/material/tooltip, which would pull in Material's own theming against
 * CLAUDE.md's single-master-stylesheet/Tailwind-only rule.
 */
@Component({
  selector: 'app-tooltip-content',
  template: `
    <div role="tooltip" class="max-w-xs rounded-md bg-slate-navy px-3 py-1.5 text-xs font-medium text-white shadow-xl">
      {{ text }}
    </div>
  `,
})
export class TooltipContent {
  @Input() text = '';
}
