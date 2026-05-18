using EfmlGen.Core;
using Xunit;

namespace EfmlGen.Tests;

public class CsKeywordsTests
{
    [Theory]
    [InlineData("class", "@class")]
    [InlineData("event", "@event")]
    [InlineData("int", "@int")]
    [InlineData("string", "@string")]
    [InlineData("Operator", "Operator")]    // case-sensitive: PascalCase không là keyword
    [InlineData("Code", "Code")]
    [InlineData("Name", "Name")]
    [InlineData("ID_GUID", "ID_GUID")]
    [InlineData("", "")]
    public void Escape_PrefixesReservedKeywords(string input, string expected)
    {
        Assert.Equal(expected, CsKeywords.Escape(input));
    }

    [Theory]
    [InlineData("class", true)]
    [InlineData("Class", false)]
    [InlineData("Code", false)]
    public void IsReserved_DetectsExactLowercaseMatch(string input, bool expected)
    {
        Assert.Equal(expected, CsKeywords.IsReserved(input));
    }
}
