using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// Every WebApplicationFactory{Program}-based test class sets Jwt__Key/Turnstile__* etc.
/// via Environment.SetEnvironmentVariable in its own InitializeAsync (see
/// AuthEndToEndTests's class comment for why env vars, not ConfigureAppConfiguration, are
/// required here). That state is PROCESS-WIDE, not per-test-class -- xUnit runs different
/// test classes in parallel by default, so two such classes racing to set the same env vars
/// to their own Testcontainers Postgres connection string can cross-wire which physical
/// database each factory's host actually talks to, surfacing as spurious failures (e.g. a
/// "duplicate key" on RoleNameIndex from two hosts both auto-seeding into the SAME
/// container). Every class carrying [Collection(Name)] runs sequentially with every other
/// member of the same collection, closing that race -- each still gets its own, fully
/// isolated Postgres container, they just never run at the literal same moment as each
/// other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class SequentialWebApplicationFactoryCollection
{
    public const string Name = "Sequential WebApplicationFactory tests";
}
