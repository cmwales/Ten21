using Ten21.Infrastructure.Identity.Services;
using Xunit;

namespace Ten21.UnitTests;

public class RefreshTokenHasherTests
{
    [Fact]
    public void GenerateRawToken_ProducesDifferentValuesEachCall()
    {
        var first = RefreshTokenHasher.GenerateRawToken();
        var second = RefreshTokenHasher.GenerateRawToken();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GenerateRawToken_IsUrlSafe()
    {
        var token = RefreshTokenHasher.GenerateRawToken();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        var raw = RefreshTokenHasher.GenerateRawToken();

        var first = RefreshTokenHasher.Hash(raw);
        var second = RefreshTokenHasher.Hash(raw);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Hash_DifferentInputsProduceDifferentHashes()
    {
        var hashA = RefreshTokenHasher.Hash(RefreshTokenHasher.GenerateRawToken());
        var hashB = RefreshTokenHasher.Hash(RefreshTokenHasher.GenerateRawToken());

        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void Hash_NeverEqualsTheRawTokenItself()
    {
        // Sanity check against a hypothetical future refactor that accidentally "hashes"
        // by just returning the input -- the whole point is the DB never stores the raw value.
        var raw = RefreshTokenHasher.GenerateRawToken();

        Assert.NotEqual(raw, RefreshTokenHasher.Hash(raw));
    }
}
