using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>Bug #1283: About must never read "Version" with nothing after it.</summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("0.7.0", "1.0.0+4253d8a0123", "0.7.0")]
    [InlineData(" 0.7.0 ", null, "0.7.0")]
    [InlineData("", "1.0.0+4253d8a0123456", "development (4253d8a)")]
    [InlineData("", "1.0.0+abc", "development (abc)")]
    [InlineData("", "1.0.0", "development")]
    [InlineData("", "1.0.0+", "development")]
    [InlineData(null, null, "development")]
    [InlineData("   ", null, "development")]
    public void Describe_never_returns_an_empty_version(string? config, string? informational, string expected)
    {
        Assert.Equal(expected, AppVersion.Describe(config, informational));
    }
}
