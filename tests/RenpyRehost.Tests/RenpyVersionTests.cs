using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class RenpyVersionTests
{
    [Theory]
    [InlineData("Ren'Py 8.2.3.24061702", 8, 2, 3)]
    [InlineData("RenPy 7.4", 7, 4, 0)]
    [InlineData("8.3.0", 8, 3, 0)]
    [InlineData("nope", -1, -1, -1)]
    public void Parse_extracts_first_version(string input, int major, int minor, int patch)
    {
        var v = RenpyVersion.Parse(input);
        if (major < 0) { Assert.Null(v); return; }
        Assert.Equal(new RenpyVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData(8, 3, 0, true)]
    [InlineData(8, 0, 0, true)]
    [InlineData(7, 4, 0, true)]
    [InlineData(7, 3, 5, false)]
    [InlineData(6, 99, 0, false)]
    public void SupportsWebTarget_is_74_and_up(int major, int minor, int patch, bool expected)
        => Assert.Equal(expected, new RenpyVersion(major, minor, patch).SupportsWebTarget);

    [Fact]
    public void Comparable_orders_by_component()
    {
        var list = new[]
        {
            new RenpyVersion(8, 2, 10),
            new RenpyVersion(8, 2, 3),
            new RenpyVersion(7, 4, 0),
            new RenpyVersion(8, 3, 0),
        };
        Array.Sort(list);
        Assert.Equal(
            new[] { new RenpyVersion(7, 4, 0), new RenpyVersion(8, 2, 3), new RenpyVersion(8, 2, 10), new RenpyVersion(8, 3, 0) },
            list);
    }
}
