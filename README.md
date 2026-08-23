# Ten21 Backend — Phase 0 Complete Scaffold (US-01 through US-09)

## ⚠️ How this was built (read this first)

This scaffold was authored in a sandbox with **no .NET SDK and no network access** — so
nothing here has been run, restored, or compiled. Every file was hand-written to be
correct, but treat first build/test as the real verification step:

```bash
cd Ten21
dotnet restore
dotnet build
dotnet test tests/Ten21.UnitTests
```

If anything doesn't compile cleanly, tell me the exact error and I'll fix it — I'd rather
you catch that in one pass than debug it blind later.

## Layering (why 6 projects, not 1)

```
Ten21.Domain            <- zero dependencies. Entities + ITenantScopedEntity only.
    ^
Ten21.Application        <- depends on Domain. Contracts (ITenantContext) only.
    ^
Ten21.Infrastructure     <- depends on Domain + Application. EF Core, Npgsql, RLS, middleware.
    ^
Ten21.Api                <- depends on Infrastructure. Program.cs, controllers, composition root.

Ten21.UnitTests           -> Infrastructure  (SQLite in-memory, fast, no external deps)
Ten21.IntegrationTests     -> Api             (Postgres via Testcontainers — scaffolded, not written)
```

Dependencies point one direction only: `Api -> Infrastructure -> Application -> Domain`.
Domain never knows EF Core exists. This is what lets `ITenantScopedEntity` and
`ITenantContext` get reused unchanged if, say, object storage swaps from S3 to R2, or a
repository layer gets added later — those are Infrastructure-level changes that never
ripple into Domain.

### Where we deliberately did NOT add an interface

Per your KISS note: `Ten21DbContext` is used directly by `PropertiesController` — no
`IPropertyRepository` wrapping it. A generic repository over EF Core (which is already a
unit-of-work/repository abstraction) is exactly the kind of interfacing-for-its-own-sake
you flagged. `ITenantContext` is the one place we did add an interface, because it has a
concrete, near-term reason to vary by caller (HTTP request vs. background job vs. seed
script), not because "it might be nice someday."

## What US-01 delivers

- `ITenantScopedEntity` (Domain) — the one-property contract every tenant-scoped entity
  implements.
- `ITenantContext` / `TenantContext` (Application/Infrastructure) — scoped, request-lived,
  set exactly once, throws if you try to set it twice.
- `TenantMiddleware` (Infrastructure) — resolves tenant context from **JWT claims only**,
  never from headers (see the comment in the file for why we deviated from the literal
  acceptance-criteria wording here).
- `Ten21DbContext` (Infrastructure) — reflection-based global query filter applied to every
  `ITenantScopedEntity`, fail-closed (unresolved context = zero rows, not all rows), plus
  write-time auto-stamping and a defense-in-depth block on cross-tenant updates.
- `TenantSessionInterceptor` + `sql/rls-policies.sql` — the Postgres RLS backstop, so a bug
  in the EF Core filter specifically doesn't equal a cross-tenant leak.
- `PropertiesController` — a deliberately thin, temporary endpoint whose only job is to
  prove the whole chain works end-to-end. Not the real Properties API.

## What US-02 delivers

- **Design resolution**: `ApplicationUser` (Infrastructure) is a **global identity** — no
  `TenantId`, not `ITenantScopedEntity`. `TenantMembership` (Domain) is the tenant-scoped
  join `{ UserId, TenantId, RoleId, IsPrimary }` that actually carries "who can act as what
  role in which tenant." This resolves a real conflict between the literal US-02 acceptance
  criteria and the multi-tenant claims model already agreed for US-03/US-04 — see the
  class-level comments on both entities for the full reasoning.
- `RefreshToken` (Domain) — deliberately **not** `ITenantScopedEntity`, even though it
  carries a `TenantId` column. Refresh happens before any tenant context can be resolved
  (no JWT yet); making it tenant-scoped would make every refresh attempt return zero rows
  under the fail-closed filter. See its class-level comment.
- `IJwtTokenService` / `JwtTokenService` — mints 15-minute HMAC-signed access tokens
  carrying `user_id`, `tenant_id`, `organization_id` (if present), and a role claim.
- `IRefreshTokenService` / `RefreshTokenService` — issues, rotates, and revokes 7-day
  refresh tokens. Raw values are never persisted, only SHA-256 hashes (`RefreshTokenHasher`,
  unit-tested in isolation). Token reuse (a revoked token presented again — a theft signal)
  revokes the entire rotation chain, not just the one token.
- `AuthController` — `POST /api/auth/login`, `POST /api/auth/refresh-token`,
  `POST /api/auth/revoke-token`. Refresh tokens travel as HTTP-only cookies scoped to
  `/api/auth`, never in a JSON body. Brute-force lockout (5 attempts / 15 min) is enforced
  via `UserManager`, not `SignInManager` — this is a stateless API, not a cookie-auth MVC
  app, so `SignInManager`'s interactive-sign-in machinery isn't the right fit.
- **A real, documented gap, not an oversight**: `tenant_memberships` does NOT get a
  Postgres RLS policy, unlike `properties`. `IgnoreQueryFilters()` (used deliberately in
  `AuthController` for the login/refresh bootstrap lookups) only bypasses the *EF Core*
  filter — RLS would still silently zero out those exact queries, since no tenant context
  exists yet at that point in the request. See the comment block in
  `sql/rls-policies.sql` for the full reasoning and the hardening path if this needs
  closing later (a separate DB role with `BYPASSRLS` used only by the auth code path).
- **Secure-by-default fallback policy**: every endpoint now requires an authenticated
  caller unless explicitly marked `[AllowAnonymous]` — so `PropertiesController` (which has
  no `[Authorize]` attribute of its own) is protected by the fallback, not by an
  easy-to-forget per-controller attribute. `/health` is explicitly opted out, since uptime
  checks need to stay open.
- `RoleSeeder` + `DevSeeder` (dev-only, gated the same way as auto-migration) — seed the
  9-tier role taxonomy and one test user/tenant, so login is actually testable on a fresh
  database. `DevSeeder` is an explicit stopgap for the fact there's no real
  registration/onboarding endpoint yet (that's Phase 5) — delete it once one exists, don't
  extend it.
- 9 new xUnit tests (`RefreshTokenHasherTests`, `JwtTokenServiceTests`) — pure logic, no DB,
  no ASP.NET Core host required.

## What US-03 delivers

- `Permissions` (Domain) — the permission-claim vocabulary (e.g. `Permissions.Ledger.Read`).
  Deliberately small and doc-grounded, not a speculative full catalog — FEATURES.docx §1
  requires every new feature story to declare its own required claims, so this grows
  feature-by-feature through Phase 2, not all at once now.
- `RolePermissions` (Domain) — the additive role→permission bundle table. Every grant is
  traceable to specific SECURITY.docx §4.1 or BUSINESS_RULES.docx §1 wording (see inline
  comments) — this is a Phase-0 starting point, not a final claims matrix.
- `TenantRestrictedPermissionPrefixes` (Domain) + `TenantHardBlockAuthorizationHandler`
  (Infrastructure) — SECURITY.docx §4.2's "Owner vs. Tenant Isolation Principle" enforced as
  a structural invariant independent of `RolePermissions`, not just a starting bundle. Even
  a future bug that accidentally grants Tenant a ledger/voting permission still gets
  blocked here — the same belt-and-suspenders principle as the EF filter + Postgres RLS
  pairing in US-01.
- `PermissionClaimsTransformation` (Infrastructure) — expands the JWT's single role claim
  into its full permission bundle at request time, server-side, rather than baking
  permissions into the token at issuance. Deliberate choice over widening
  `IJwtTokenService`'s signature — see its class comment for the reasoning (smaller tokens,
  role changes take effect immediately rather than after every issued token expires).
- `AuthorizationConfiguration.AddTen21Authorization()` (Infrastructure) — reflects one
  `[Authorize(Policy = ...)]` policy into existence per `Permissions.All` entry, and now
  owns the secure-by-default fallback policy (moved here from Program.cs, since policy
  *shape* is a security-model concern, not host wiring).
- `GET /api/auth/me` — not part of US-02/US-03's literal acceptance criteria, but a
  near-universal frontend need, and doubles as a live end-to-end proof that the claims
  transformation is actually running.
- 12 new xUnit tests, including a couple worth calling out specifically:
  `RolePermissionsTests.TenantBundle_NeverIncludesARestrictedPermission` (verifies the
  *primary* layer independently honors the same invariant the handler backstops) and
  `RolePermissionsTests.PropertyManagerBundle_HasNoVotingPermission` (pins SECURITY.docx's
  explicit "cannot cast HOA board votes" statement so it can't silently regress).

## What US-04 delivers (Parent Org Hierarchy & Context Switching)

- `GET /api/organization/tenants`, `POST /api/organization/switch-context` — list every
  tenant the caller has membership in, and mint a fresh scoped JWT for a different one.
- **A third documented use of `IgnoreQueryFilters()` on `TenantMemberships`**, alongside
  login and refresh from US-02 — listing "which tenants can I switch to" is inherently a
  cross-tenant lookup for the caller's own rows only. Same already-documented exception,
  used again for a different legitimate reason.
- **Flagged for you specifically, not just buried in a comment**: switching context does
  NOT touch the refresh-token cookie. `RefreshToken` is fixed to the tenant it was issued
  under (a US-02 design choice), so once a switched-context access token expires,
  refreshing reverts the caller to their *primary* tenant, not the one they switched to. A
  PMC user actively working in a non-primary property would need the frontend to re-call
  `switch-context` after every silent token refresh. Worth deciding now, before Angular
  work assumes otherwise — see `OrganizationController.SwitchContext`'s doc comment.

## What US-05 delivers (Rate Limiting)

- 5 requests/minute per client IP on every `/api/auth/*` route (including `/me`), via a
  properly IP-*partitioned* sliding-window limiter — not ASP.NET Core's simpler
  single-global-bucket overload, which would let one busy caller lock out everyone else.
- Real unit tests against the actual limiter (not just its wiring): exactly 5 requests
  succeed and the 6th is rejected, different IPs get independent budgets, and a missing
  `RemoteIpAddress` still gets rate-limited rather than bypassing the limit entirely.
- Brute-force lockout (5 attempts / 15 min) was already configured in US-02.

## What US-06 delivers (Presigned Object Storage)

- `POST /api/documents/presign-upload` — validates MIME type and declared size, then
  returns a 15-minute presigned S3/R2 PUT URL scoped to
  `{TenantId}/{Category}/{EntityId}/{Guid}.ext`.
- `ObjectKeySanitizer` — the `Category` segment comes straight from client input and gets
  embedded in the object key path; unsanitized, a crafted value could manipulate the key
  structure. Not in the literal acceptance criteria, added as necessary defense-in-depth.
- **A real, honestly-stated limitation**: generating a presigned PUT URL is pure local
  signing — it never contacts S3/R2, and by itself does NOT cryptographically enforce the
  10MB ceiling on the actual upload. Validation only checks the *client-declared* size
  before signing; a dishonest client could request a valid URL with an honest declared size
  and then upload more bytes than declared. See `S3StorageService`'s class comment for the
  hardening options if this needs to be airtight later.

## What US-07 delivers (Audit Logging & Soft Delete)

- `IAuditableEntity` / `ISoftDelete` (Domain) — deliberately **opt-in**, not applied to
  every entity. Auditing ASP.NET Core Identity's own bookkeeping would flood the log with
  framework noise instead of meaningful business changes.
- `AuditSaveChangesInterceptor` — captures JSON diffs and converts real deletes into
  soft-deletes, both inside the *same* transaction as the change itself. `Property` now
  implements both interfaces as the proof-of-concept; `TenantMembership` is audited but
  deliberately not soft-deletable yet — offboarding semantics aren't designed (Phase 2).
- `Ten21DbContext`'s query-filter loop now handles three shapes (tenant-only,
  soft-delete-only, both combined) — worth a careful look, it's exactly the kind of
  AND-vs-overwrite bug that's easy to get subtly wrong.
- `ITenantContext` gained `UserId` (populated by `TenantMiddleware`) — the interceptor needs
  to know who made a change. A small extension to already-built US-01 infrastructure rather
  than a parallel "current user" interface.

## What US-08 delivers (Standardized API Response Envelope)

- `ApiResponseWrappingFilter` — a global MVC result filter that wraps every 2xx response in
  `ApiResponse<T>` automatically, so no controller has to remember to do it. Uses the same
  reflection-based generic-dispatch technique as `Ten21DbContext`'s query filters, for a
  genuine reason: the filter only ever sees `object`, never a compile-time `T`.
- Error responses are untouched by this filter — those go through US-09's RFC 7807 shape,
  an intentionally different envelope.

## What US-09 delivers (Global Exception Engine & Error Taxonomy)

- Six exception types (`DomainException`, `NotFoundException`, `ValidationException`,
  `UnauthorizedException`, `ForbiddenException`, `ConflictException`) in Domain — plain BCL
  `Exception` subclasses, no framework dependency.
- `GlobalExceptionHandler` — the single registered `IExceptionHandler`, mapping each type to
  its designated status code and RFC 7807 `ProblemDetails`, masking internal exception
  detail outside Development while always logging full detail server-side regardless.
- **A real gap, flagged rather than hidden**: existing controllers (`Auth`, `Organization`,
  `Documents`) still return `BadRequest()`/`Unauthorized()`/`Forbid()` directly rather than
  throwing this new taxonomy. A full realization of "all errors handled identically
  system-wide" would refactor those call sites to throw instead, so literally everything
  funnels through `GlobalExceptionHandler`. Not retrofitted here — touching already-written,
  uncompiled controller code across four files with no compiler to catch mistakes was a
  worse trade than shipping the engine now and doing that refactor as a deliberate,
  reviewable follow-up.

## What's explicitly NOT done yet (by design, not oversight)

All 9 Phase 0 user stories are now written. What's still genuinely open:

- **Nothing has been compiled or run.** Everything was hand-written across this entire
  scaffold without a .NET SDK available in this sandbox. This is the real gate before any
  of it counts as done — treat the first `dotnet build && dotnet test` as the actual test,
  not a formality.
- **US-09's exception taxonomy isn't retrofitted into existing controllers** — see "What
  US-09 delivers" above.
- **US-04's context-switch/refresh-token interaction** needs a decision before Angular
  work assumes a specific behavior — see "What US-04 delivers" above.
- **No EF Core migration.** `sql/rls-policies.sql` now covers `properties`,
  `document_attachments`, and `audit_logs`; `tenant_memberships` remains the one documented
  RLS exception. All of it needs folding into a real migration once `dotnet ef` is
  available locally — see that file's header.
- **No real Postgres integration test.** `Ten21.IntegrationTests` is still scaffolded but
  empty — see its README.
- ~~No user registration/onboarding endpoint.~~ **Done as of Phase 5 / US-14** —
  `POST /api/auth/register` now provisions a real account + workspace; `DevSeeder` has been
  deleted. See `docs/User_Stories_Phase_5.md`.
- **Permission catalog stays intentionally minimal.** Grows feature-by-feature through
  Phase 2, per FEATURES.docx §1.

## Database setup (local Postgres via Docker, auto-migrated at runtime)

```bash
cd Ten21
docker compose up -d
docker compose ps   # wait for postgres to report "healthy"
```

The container's own credentials are hardcoded in `docker-compose.yml` (see the comment
there for why that's fine for this specific case — localhost-only, throwaway dev data).

The app's connection string is a real secret, though, and lives in **.NET User Secrets**
instead — stored outside the repo entirely (`~/.microsoft/usersecrets/<id>/secrets.json`
on Linux/Mac), so it physically cannot end up in a Git commit, unlike a gitignored file
sitting in the project folder:

```bash
cd src/Ten21.Api
dotnet user-secrets set "ConnectionStrings:Ten21Database" \
  "Host=localhost;Port=5432;Database=ten21_dev;Username=ten21_app;Password=ten21_dev_only_local"
```

`Ten21.Api.csproj` already has `<UserSecretsId>ten21-api-dev</UserSecretsId>` set, so
ASP.NET Core loads this automatically whenever `ASPNETCORE_ENVIRONMENT=Development` — no
code change needed, and `appsettings.json` keeps shipping with an **empty** connection
string so a misconfigured non-Development environment fails loudly instead of quietly
running against nothing.

The JWT signing key is the same story — a real secret, set the same way:

```bash
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
```

(`Jwt:Issuer` and `Jwt:Audience` are not secrets — they're just checked into
`appsettings.json` as `https://api.ten21.io` / `https://app.ten21.io`.)

Note this is a **local-machine mechanism only** — it won't help GitLab CI or a real
deployment. Those need their own secret source (GitLab CI/CD variables, a proper secrets
manager) when the time comes; don't reach for User Secrets outside local dev.

**One-time**, once you have `dotnet ef` tooling available locally, generate the migration
and fold the RLS policies into it (exact steps are in the header comment of
`sql/rls-policies.sql`):

```bash
dotnet ef migrations add InitialCreate --project src/Ten21.Infrastructure --startup-project src/Ten21.Api
# then paste the RLS SQL into that migration's Up()/Down(), per sql/rls-policies.sql
```

After that one-time step, the loop is just:

```bash
docker compose up -d
dotnet run --project src/Ten21.Api
```

`Program.cs` calls `Database.MigrateAsync()` automatically, but **only when
`ASPNETCORE_ENVIRONMENT=Development`** (the default for plain `dotnet run`, set via
`Properties/launchSettings.json`). Schema, RLS policies, the 9 seeded roles, and one test
user/tenant all apply on that first run; EF's migration history table and `DevSeeder`'s own
"does any user already exist" check both keep this a no-op on every restart after that.
This is deliberately never enabled outside Development — see the comment in `Program.cs`
for why auto-migrating (and auto-seeding) in production is a real footgun the moment you
run more than one instance.

**Try the registration + login flow** once the app is running. `DevSeeder` is retired as of
US-14 (`docs/User_Stories_Phase_5.md`) — `POST /api/auth/register` is now the real way to
get a usable account on a fresh database, no seeded test user required:

```bash
curl -i http://localhost:5080/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"firstName":"Dev","lastName":"User","email":"dev@ten21.io","password":"Dev-Only-Passw0rd!1","phoneNumber":null,"address":null,"workspaceName":"Dev Test HOA","portfolioSize":1,"agreedToTerms":true}'

curl -i http://localhost:5080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"dev@ten21.io","password":"Dev-Only-Passw0rd!1"}'
```

(The default `launchSettings.json` profile is plain HTTP on `localhost:5080`, which is why
the refresh-token cookie's `Secure` flag is relaxed in Development — see the comment in
`RefreshTokenCookie.Set`.) Both calls return a JSON body with an `accessToken` and a
`ten21_refresh_token` cookie in the response headers — registration already logs you in, so
the follow-up `/login` call above is only there to prove the account persisted.

## Getting this into GitLab

The scaffold already has what a fresh repo needs:

- `.gitignore` — excludes `bin/`, `obj/`, and build output. Real secrets never need a
  gitignore entry in the first place: the app's connection string lives in .NET User
  Secrets (outside the repo entirely), and the local Postgres container's password is a
  hardcoded, clearly-labeled dev-only value with no real sensitivity (see
  `docker-compose.yml`).
- `.gitlab-ci.yml` — a minimal two-stage pipeline (`build`, then `unit_tests` against the
  SQLite-based tests). No database or Docker-in-Docker required for it to pass. The
  Postgres/Testcontainers integration-test stage is commented out with an explanation —
  enable it once your runner supports Docker-in-Docker.

```bash
cd Ten21
git init
git add .
git commit -m "Phase 0 / US-01: multi-tenant isolation engine scaffold"
git remote add origin <your-gitlab-repo-url>
git push -u origin main
```

The first pipeline run will restore + build + test in GitLab's hosted runner — a good
independent check that this compiles, since I built it without a compiler available here.

## Next steps I'd suggest

1. Push to GitLab and let the pipeline run — first real compiler check on this code across
   all 9 stories (Identity/JWT/Authorization/S3/RateLimiting packages all only get verified
   against a real restore now).
2. `docker compose up -d`, set `ConnectionStrings:Ten21Database` and `Jwt:Key` via
   `dotnet user-secrets` (above), run the one-time `dotnet ef migrations add` step (fold in
   the RLS SQL from `sql/rls-policies.sql`), then `dotnet run --project src/Ten21.Api`. Try
   the login curl example above, then
   `curl http://localhost:5080/api/auth/me -H "Authorization: Bearer <accessToken>"`.
   `ObjectStorage:AccessKey`/`ObjectStorage:SecretKey`/`ObjectStorage:BucketName` also need
   `dotnet user-secrets` entries before `/api/documents/presign-upload` will produce a URL
   that actually works against a real bucket (presign generation itself works with
   placeholder credentials — see `S3StorageService`'s comment).
3. Decide the US-04 context-switch/refresh-token question above before Angular work starts
   assuming a specific behavior.
4. Consider the US-09 controller-retrofit follow-up (throwing the exception taxonomy
   instead of returning results directly) as a dedicated, reviewable pass.
