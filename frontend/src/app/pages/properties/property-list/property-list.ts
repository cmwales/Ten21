import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import {
  OccupancyStatusValue,
  PropertyListItemDto,
  PropertyTypeValue,
  PropertyTypes,
} from '../../../core/models/property.models';
import { PropertyService } from '../../../core/services/property.service';

const PAGE_SIZES = [15, 30, 50] as const;
type PageSize = (typeof PAGE_SIZES)[number];

/**
 * Fetches every property once (see PropertyService.listProperties' doc comment on why) and
 * does search/filter/pagination entirely client-side over that in-memory set -- appropriate
 * for a landlord's own portfolio size, not a dataset needing server-side query pushdown.
 * Flat table, one row per property/suite -- there is no grouping by address or nested unit
 * list (see Property's own class comment for why).
 */
@Component({
  selector: 'app-property-list',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './property-list.html',
})
export class PropertyList implements OnInit {
  private readonly propertyService = inject(PropertyService);
  private readonly searchInput$ = new Subject<string>();

  protected readonly pageSizes = PAGE_SIZES;
  protected readonly propertyTypes = Object.values(PropertyTypes);

  protected readonly occupancyBadgeClass: Record<OccupancyStatusValue, string> = {
    Vacant: 'bg-amber/10 text-amber',
    Occupied: 'bg-emerald/10 text-emerald',
    Maintenance: 'bg-rose/10 text-rose',
  };

  protected readonly loading = signal(true);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly properties = signal<PropertyListItemDto[]>([]);
  protected readonly searchTerm = signal('');
  protected readonly activeTypeFilters = signal<ReadonlySet<PropertyTypeValue>>(new Set());
  protected readonly pageSize = signal<PageSize>(15);
  protected readonly pageNumber = signal(1);

  protected readonly filteredProperties = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const typeFilters = this.activeTypeFilters();

    return this.properties().filter((property) => {
      if (typeFilters.size > 0 && !typeFilters.has(property.propertyType)) {
        return false;
      }
      if (!term) {
        return true;
      }
      const haystack = [
        property.name,
        property.streetAddress1,
        property.city,
        property.state,
        property.postalCode,
        property.unitIdentifier ?? '',
      ]
        .join(' ')
        .toLowerCase();
      return haystack.includes(term);
    });
  });

  protected readonly totalFiltered = computed(() => this.filteredProperties().length);
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalFiltered() / this.pageSize())));

  protected readonly pagedProperties = computed(() => {
    const size = this.pageSize();
    const start = (this.pageNumber() - 1) * size;
    return this.filteredProperties().slice(start, start + size);
  });

  protected readonly rangeStart = computed(() => (this.totalFiltered() === 0 ? 0 : (this.pageNumber() - 1) * this.pageSize() + 1));
  protected readonly rangeEnd = computed(() => Math.min(this.pageNumber() * this.pageSize(), this.totalFiltered()));

  protected readonly isEmptyWorkspace = computed(() => !this.loading() && this.properties().length === 0);
  protected readonly hasNoSearchResults = computed(
    () => !this.loading() && this.properties().length > 0 && this.totalFiltered() === 0,
  );

  constructor() {
    this.searchInput$.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed()).subscribe((term) => {
      this.searchTerm.set(term);
      this.pageNumber.set(1);
    });
  }

  ngOnInit(): void {
    this.propertyService.listProperties().subscribe({
      next: (response) => {
        this.properties.set(response.items);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorKey.set('properties.list.networkError');
      },
    });
  }

  protected onSearchInput(value: string): void {
    this.searchInput$.next(value);
  }

  protected toggleTypeFilter(type: PropertyTypeValue): void {
    this.activeTypeFilters.update((current) => {
      const next = new Set(current);
      if (next.has(type)) {
        next.delete(type);
      } else {
        next.add(type);
      }
      return next;
    });
    this.pageNumber.set(1);
  }

  protected setPageSize(size: PageSize): void {
    this.pageSize.set(size);
    this.pageNumber.set(1);
  }

  protected previousPage(): void {
    this.pageNumber.update((n) => Math.max(1, n - 1));
  }

  protected nextPage(): void {
    this.pageNumber.update((n) => Math.min(this.totalPages(), n + 1));
  }
}
