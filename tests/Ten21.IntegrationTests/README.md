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
