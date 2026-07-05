using Ecad.Core.ValueObjects;
using Xunit;

namespace Ecad.Core.Tests;

public class DeviceTagTests
{
    [Theory]
    [InlineData("=K1+A1-K1", "K1", "A1", "K1")]
    [InlineData("-K1", null, null, "K1")]
    [InlineData("+A1-K1", null, "A1", "K1")]
    public void Parse_SplitsSegments(string input, string? function, string? location, string? designation)
    {
        var tag = DeviceTag.Parse(input);
        Assert.Equal(function, tag.Function);
        Assert.Equal(location, tag.Location);
        Assert.Equal(designation, tag.Designation);
    }

    [Fact]
    public void ToString_RoundTripsFullTag()
    {
        var tag = DeviceTag.Parse("=K1+A1-K1");
        Assert.Equal("=K1+A1-K1", tag.ToString());
    }

    [Fact]
    public void ToString_OmitsAbsentSegments()
    {
        var tag = DeviceTag.Parse("-K1");
        Assert.Equal("-K1", tag.ToString());
    }
}
