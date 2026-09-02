# PRODUCTION READINESS CHECKLIST

A running list of things that work correctly in local dev today but need a deliberate
config/infra change before a real production launch. Items get added here whenever a
dev-only workaround, placeholder, or config gap surfaces during a sprint -- most of these
were first identified during Sprint 9's live end-to-end verification (see
`User_Stories_Sprint_9.md`) plus a follow-up codebase survey, not invented up front.

Each item names the current dev-only state, why it can't ship as-is, and what production
needs instead. Nothing here blocks *development* -- these are pre-launch/pre-scale gates.

## 1. Secrets & Config

- **SMTP is not configured -- email silently falls back to console logging.**
  `EmailServiceCollectionExtensions` registers `SmtpEmailSender` only if
  `Smtp:Username`/`Smtp:Password` are set; otherwise every email (welcome, password
  reset, 2FA codes) just gets logged via `ConsoleEmailSender`, never actually sent.
  `appsettings.json` ships both empty. **Before launch:** set real SMTP credentials
  (or a transactional email provider) via user-secrets/environment/key vault -- this
  is a hard launch blocker, not a nice-to-have (residents/PMs can't receive welcome
  emails, password resets, or 2FA codes otherwise).
- **`Turnstile:AllowedHostnames` defaults to `"localhost"`.** Must be set to the real
  production hostname(s) (e.g. `app.ten21.io`) before launch, or Cloudflare Turnstile
  will reject registration attempts from real users. See the dummy-testing-key gotcha
  in §4 below -- this exact setting is also what a stray dev-mode value would silently
  break in production if left unset or copy-pasted from a test config.
- **`FromAddress` defaults to a personal Gmail address** (`cmwales@gmail.com`) in
  `appsettings.json`. Not a credential leak, but a personal placeholder committed as
  the default outbound sender -- replace with a real org mailbox before launch.
- **Object storage (S3/R2) region is a placeholder, not a decision.** `AmazonS3Client`
  setup is real and production-ready (supports both real AWS S3 and R2/S3-compatible
  endpoints), but region is hardcoded to `USEast1` with an explicit `TODO` marking it
  as unchosen, and `AccessKey`/`SecretKey`/`ServiceUrl` are unset by default. **Before
  launch:** pick a real region/bucket and set real credentials.

## 2. Horizontal Scaling (breaks the moment there's more than one instance)

- **Rate limiting is in-memory, per-process.** `/api/auth/*`'s 5 req/min sliding-window
  limiter (`AuthRateLimiterPolicy`) is IP-partitioned but held in each process's own
  memory. Behind a load balancer with N instances, the effective limit becomes 5×N
  req/min, not 5. **Before scaling past one instance:** move to a distributed store
  (Redis-backed limiter) so the budget is actually shared.
- **ASP.NET Core Data Protection keys have no explicit persistence configured.** No
  `PersistKeysToFileSystem`/`PersistKeysToStackExchangeRedis`/`PersistKeysToAzureBlobStorage`
  call exists anywhere -- Identity's email-confirmation/password-reset tokens depend on
  Data Protection under the hood. With the framework default (ephemeral or per-machine
  key storage), tokens issued by one instance won't validate on another, and every
  outstanding token breaks on restart or redeploy. **Before scaling past one instance
  (and ideally before launch, since even single-instance redeploys would invalidate
  live tokens otherwise):** configure a shared, persistent key ring (DB-backed, Redis,
  or blob storage) with a stable application name.

## 3. Verify Before Launch, Not Yet Broken

- **CORS has no explicit configuration anywhere in the API.** Fine if the Angular SPA
  and API end up served same-origin (e.g. one domain behind a reverse proxy); if they
  ship on separate origins (`app.ten21.io` calling `api.ten21.io` directly), cross-origin
  requests will simply fail with no CORS policy in place. **Before launch:** confirm the
  actual production topology and add `AddCors`/`UseCors` if the origins differ.
- **Payment processing is manual-ledger only, by design (Phase 2+).** `PaymentService`/
  `PaymentTransaction` track cash/check/etc. payments a PM logs by hand -- there is no
  `IPaymentProcessor`, no Stripe/ACH tokenization integration yet. This matches the
  documented Phase 1/2 scope split (`CLAUDE.md`: "no automated payment processing yet"),
  not a gap to close before *this* phase's launch -- listed here so it isn't mistaken
  for an oversight later.

## 4. Gotchas Already Hit Once (avoid repeating)

- **Cloudflare's dummy Turnstile testing keys always report `hostname: "example.com"`
  from siteverify, regardless of the real page's actual origin.** Discovered live while
  verifying US-44: registering through a real browser with the dummy sitekey
  (`1x00000000000000000000AA`) + dummy secret (`1x0000000000000000000000000000000AA`)
  fails with "Bot verification failed" unless `Turnstile:AllowedHostnames` includes
  `example.com` specifically -- the default `"localhost"` does NOT cover it, even
  though the browser is genuinely on `localhost`. If `Turnstile:AllowedHostnames` is
  ever set to include `example.com` for a testing session, **it must be reverted**
  before that config reaches anything resembling production -- an allow-list containing
  `example.com` would accept a forged/replayed dummy-key token from anyone.
- **A shared `<ng-template>` invoked twice via `*ngTemplateOutlet` silently dropped
  every `@for`-driven `<option>` list positioned after the first nested `@if` inside
  it** (Angular 22, discovered in `LeaseDrawer`'s recurring-charge form -- see the fix
  commit on `feature/us-44-recurring-charge-templates`). The underlying component data
  was never empty; only the template's rendering of it was broken, and this passed
  both `ng build` and every unit test since HTTP-mocked component tests never render
  the real template. **Takeaway:** prefer duplicating a field block directly over
  factoring shared form-field markup through `ngTemplateOutlet` in this codebase, and
  treat "the build and unit tests pass" as insufficient proof for any new form UI --
  a live browser render caught this, nothing else did.
