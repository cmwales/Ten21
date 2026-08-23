# Ten21.IntegrationTests

Real-Postgres tests that `Ten21.UnitTests` (SQLite-backed) structurally can't cover. Both
require Docker running locally -- Testcontainers pulls and manages a `postgres:16-alpine`
container automatically per test class, no manual setup needed.

## RlsIsolationTests

Proves Postgres Row-Level Security (`sql/rls-policies.sql`, folded into the
`InitialCreate` migration's `Up()`) blocks a cross-tenant read even with the EF Core query
filter entirely out of the picture -- raw SQL over a second connection, no `Ten21DbContext`
involved. Connects as a deliberately low-privilege `ten21_app_test` role rather than the
container's default `postgres` superuser: Postgres always exempts superusers (and any
`BYPASSRLS` role) from RLS regardless of `FORCE ROW LEVEL SECURITY`, so testing through the
superuser would silently prove nothing.

## AuthEndToEndTests

Login -> refresh -> revoke -> refresh-fails, through the real HTTP pipeline
(`WebApplicationFactory<Program>`) against a real Postgres, real ASP.NET Core Identity, and
real HTTP-only cookies -- the one thing `RefreshTokenServiceTests` (SQLite, service-level)
can't exercise. Two things worth knowing if you touch this file:

- `Program.cs` reads `Jwt:Key`/`Issuer`/`Audience` into local variables **before**
  `builder.Build()` runs, so `WithWebHostBuilder(...).ConfigureAppConfiguration(...)` is too
  late to reach them -- environment variables are folded in synchronously inside
  `WebApplication.CreateBuilder(args)` itself, so this test sets `Jwt__Key` etc. via
  `Environment.SetEnvironmentVariable` before the factory's lazy host init fires instead.
  Those are process-wide, so if a second end-to-end test class is ever added, it needs to
  either reuse the same values or run in a serialized xUnit collection to avoid racing.
- The refresh-token cookie is only non-`Secure` in `Development` (see
  `RefreshTokenCookie.Set`), and `TestServer`'s default client talks plain `http://`, so the
  factory is explicitly pinned to `builder.UseEnvironment("Development")` -- otherwise the
  cookie silently never round-trips and every assertion after login fails with a misleading
  "no refresh token" 401.
- Those env vars are process-wide, so a second `WebApplicationFactory<Program>`-based test
  class (`GoogleAuthEndToEndTests`) DID race with this one the first time it was added --
  xUnit runs different test classes in parallel by default, and two classes each setting
  `ConnectionStrings__Ten21Database` to their own Testcontainers container mid-run can
  cross-wire which physical database either host actually talks to (surfaced as a spurious
  "duplicate key" on `RoleNameIndex` from two hosts racing to seed the same container). Both
  classes now carry `[Collection(SequentialWebApplicationFactoryCollection.Name)]`, which
  serializes them against each other -- each still gets its own fully isolated Postgres
  container, they just never run at the literal same moment. **Any future
  `WebApplicationFactory<Program>`-based test class needs the same `[Collection(...)]`
  attribute**, or this will resurface.

## GoogleAuthEndToEndTests

US-15's Google Sign-In flow, end to end. A real Google-signed ID token can't be fabricated
in a test, so `IGoogleIdTokenVerifier` is substituted via
`WithWebHostBuilder(...).ConfigureTestServices(...)` with a fake returning a canned
`GoogleIdentity` (or `null`, to simulate an invalid token) -- everything downstream of that
one seam (user creation/linking via Identity's real external-login store, interim tokens,
`complete-profile`, workspace provisioning) is the real production code path against a real
Postgres.
