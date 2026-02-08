using Server.Qcat.Bot;

namespace Server.Qcat.Tests;

public class CommandParsingTests
{
    [Theory]
    [InlineData("#1", 1)]
    [InlineData("＃2", 2)]
    [InlineData("# 3", 3)]
    public void TryParseHashIndex_Works(string input, int expected)
    {
        Assert.True(CommandParsing.TryParseHashIndex(input, out var idx));
        Assert.Equal(expected, idx);
    }

    [Theory]
    [InlineData("cx")]
    [InlineData("#")]
    [InlineData("#x")]
    public void TryParseHashIndex_Fails(string input)
    {
        Assert.False(CommandParsing.TryParseHashIndex(input, out _));
    }

    [Fact]
    public void TryParseBan_Works_WithSpacesInReason()
    {
        var input = "/ban 1 7656119 60 reason with spaces";
        Assert.True(CommandParsing.TryParseBan(input, out var idx, out var id, out var time, out var reason));
        Assert.Equal(1, idx);
        Assert.Equal("7656119", id);
        Assert.Equal("60", time);
        Assert.Equal("reason with spaces", reason);
    }
}

