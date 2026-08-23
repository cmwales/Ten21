using Ten21.Domain.Common;
using Xunit;

namespace Ten21.UnitTests;

public class FormulaInjectionGuardTests
{
    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+1234567890", "'+1234567890")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SomeCommand", "'@SomeCommand")]
    public void Sanitize_PrependsQuote_WhenValueStartsWithADangerousCharacter(string input, string expected)
    {
        Assert.Equal(expected, FormulaInjectionGuard.Sanitize(input));
    }

    [Theory]
    [InlineData("Riverside Apartments")]
    [InlineData("100 Main St")]
    [InlineData("")]
    public void Sanitize_LeavesOrdinaryValuesUnchanged(string input)
    {
        Assert.Equal(input, FormulaInjectionGuard.Sanitize(input));
    }
}
