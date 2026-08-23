import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ProblemDetails } from '../../../core/models/auth.models';
import { ImportPropertiesResponse } from '../../../core/models/property.models';
import { PropertyService } from '../../../core/services/property.service';

const MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024;
const ALLOWED_EXTENSIONS = ['.csv', '.xlsx'];

/** US-21: dropzone -> upload -> preview grid, all in one request/response round trip (see
 * PropertiesController.ImportProperties' doc comment for why: the server already returns
 * every parsed row alongside its validation outcome, so there's no separate "preview" call
 * before the real one). */
@Component({
  selector: 'app-property-import',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './property-import.html',
})
export class PropertyImport {
  private readonly propertyService = inject(PropertyService);

  protected readonly dragActive = signal(false);
  protected readonly uploading = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly result = signal<ImportPropertiesResponse | null>(null);
  protected readonly fileName = signal<string | null>(null);

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(true);
  }

  protected onDragLeave(): void {
    this.dragActive.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      this.handleFile(file);
    }
  }

  protected onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      this.handleFile(file);
    }
  }

  protected reset(): void {
    this.result.set(null);
    this.errorKey.set(null);
    this.fileName.set(null);
  }

  private handleFile(file: File): void {
    this.errorKey.set(null);
    this.result.set(null);

    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!ALLOWED_EXTENSIONS.includes(extension)) {
      this.errorKey.set('properties.import.invalidExtensionError');
      return;
    }

    if (file.size > MAX_FILE_SIZE_BYTES) {
      this.errorKey.set('properties.import.tooLargeError');
      return;
    }

    this.fileName.set(file.name);
    this.uploading.set(true);

    this.propertyService.importProperties(file).subscribe({
      next: (response) => {
        this.uploading.set(false);
        this.result.set(response);
      },
      error: (error: unknown) => {
        this.uploading.set(false);
        this.errorKey.set(this.resolveErrorKey(error));
      },
    });
  }

  private resolveErrorKey(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'properties.import.networkError';
    }

    if (error.status === 400) {
      const problem = error.error as ProblemDetails | undefined;
      return problem?.errors?.['File']?.[0] ?? 'properties.import.networkError';
    }

    return 'properties.import.networkError';
  }
}
