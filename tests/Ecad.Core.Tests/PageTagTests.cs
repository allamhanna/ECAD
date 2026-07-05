using Ecad.Core.ValueObjects;
using Xunit;

namespace Ecad.Core.Tests;

public class PageTagTests
{
    [Fact]
    public void Parse_SplitsAllFourSegments()
    {
        var tag = PageTag.Parse("=K1+A1&EPLAN1/5");
        Assert.Equal("K1", tag.Function);
        Assert.Equal("A1", tag.Location);
        Assert.Equal("EPLAN1", tag.DocumentType);
        Assert.Equal("5", tag.Page);
    }

    [Fact]
    public void ToString_RoundTripsFullTag()
    {
        var tag = PageTag.Parse("=K1+A1&EPLAN1/5");
        Assert.Equal("=K1+A1&EPLAN1/5", tag.ToString());
    }

    [Fact]
    public void Parse_HandlesMissingSegments()
    {
        var tag = PageTag.Parse("/5");
        Assert.Null(tag.Function);
        Assert.Null(tag.Location);
        Assert.Null(tag.DocumentType);
        Assert.Equal("5", tag.Page);
    }
}
