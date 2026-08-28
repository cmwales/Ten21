// Audit Refinement Sprint: self-registering "global" locale data bundles, so Angular's own
// built-in pipes (currency, date, number -- distinct from @ngx-translate's string catalog)
// know how to format for es-US/fr-CA once LOCALE_ID below is set to one of them. en-US needs
// no import -- it's Angular's own compiled-in default locale.
import '@angular/common/locales/global/es-US';
import '@angular/common/locales/global/fr-CA';

import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { DEFAULT_LANGUAGE, detectLocale } from './core/i18n/locale-detection';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideTranslateService({
      lang: detectLocale(),
      fallbackLang: DEFAULT_LANGUAGE,
      loader: provideTranslateHttpLoader({ prefix: '/assets/i18n/', suffix: '.json' }),
    }),
    // Audit Refinement Sprint: LOCALE_ID drives Angular's built-in currency/date/number
    // pipes -- set from the same detectLocale() the translate service itself uses, so a
    // fr-CA/es-US browser gets correctly formatted currency (e.g. "1 234,56 $" for fr-CA)
    // from first paint. Like @ngx-translate's own `lang`, this is resolved once at bootstrap;
    // switching languages via the in-app selector (LanguageSelector) changes the translated
    // strings immediately but won't retroactively reformat already-rendered currency/date
    // values without a full reload -- a known Angular LOCALE_ID limitation, not a regression
    // (the selector doesn't persist across reloads today either).
    { provide: LOCALE_ID, useValue: detectLocale() },
  ],
};
