using Ten21.Domain.Common;
using Xunit;

namespace Ten21.UnitTests;

public class PermissionsDiscoveryTests
{
    [Fact]
    public void All_ContainsTheDocCitedExampleConstants()
    {
        // US-03 acceptance criteria cite these two constants by name as worked examples --
        // pinning them here catches an accidental rename silently breaking the doc's own
        // example text.
        Assert.Contains(Permissions.Ledger.Read, Permissions.All);
        Assert.Contains(Permissions.WorkOrders.Write, Permissions.All);
    }

    [Fact]
    public void All_HasNoDuplicates()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct().Count());
    }

    [Fact]
    public void All_IsNotEmpty()
    {
        Assert.NotEmpty(Permissions.All);
    }
}
