import { Component, inject } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SUPPORTED_LANGUAGES, SupportedLanguage } from '../../core/i18n/locale-detection';

interface LanguageOption {
  code: SupportedLanguage;
  label: string;
}

const LANGUAGE_LABELS: Record<SupportedLanguage, string> = {
  'en-US': 'English',
  'es-US': 'Español',
  'fr-CA': 'Français',
};

@Component({
  selector: 'app-language-selector',
  imports: [TranslatePipe],
  templateUrl: './language-selector.html',
})
export class LanguageSelector {
  protected readonly translate = inject(TranslateService);

  protected readonly languages: LanguageOption[] = SUPPORTED_LANGUAGES.map((code) => ({
    code,
    label: LANGUAGE_LABELS[code],
  }));

  protected onLanguageChange(event: Event): void {
    const code = (event.target as HTMLSelectElement).value;
    this.translate.use(code);
  }
}
