using PhiDos.Core;

namespace PhiDos.Tests;

public class VersionTests
{
    [Theory]
    [InlineData("0.1.0", "0.10")]
    [InlineData("5.2.10", "5.21")]
    [InlineData("2.0", "2.00")]
    [InlineData("3.3", "3.30")]
    [InlineData("6.22", "6.22")]
    [InlineData("1.12.3", "1.12")]
    [InlineData("1.0.0-beta", "1.00")]
    [InlineData("01.5", "1.50")]
    [InlineData("10.0.1", "10.01")]
    [InlineData("X.XX", "X.XX")]
    [InlineData("7", "7")]
    public void FormatVersion_IsDosStyle(string raw, string shown)
    {
        Assert.Equal(shown, PhiDosInfo.FormatVersion(raw));
    }

    [Fact]
    public void DefaultBuild_UsesPlaceholder()
    {
        Assert.Equal("X.XX", PhiDosInfo.RawVersion);
        Assert.Equal("phi-DOS Version X.XX", PhiDosInfo.Banner);
    }
}
