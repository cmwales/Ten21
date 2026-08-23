using Ten21.Infrastructure.Storage;
using Xunit;

namespace Ten21.UnitTests;

public class ObjectKeySanitizerTests
{
    [Fact]
    public void StripsPathTraversalCharacters()
    {
        var result = ObjectKeySanitizer.SanitizeSegment("../../other-tenant");

        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('.', result);
    }

    [Fact]
    public void PreservesLettersDigitsDashesAndUnderscores()
    {
        var result = ObjectKeySanitizer.SanitizeSegment("lease-docs_2026");

        Assert.Equal("lease-docs_2026", result);
    }

    [Fact]
    public void FallsBackToMisc_WhenEverythingIsStripped()
    {
        var result = ObjectKeySanitizer.SanitizeSegment("../../../");

        Assert.Equal("misc", result);
    }

    [Fact]
    public void FallsBackToMisc_ForEmptyInput()
    {
        Assert.Equal("misc", ObjectKeySanitizer.SanitizeSegment(""));
    }
}
