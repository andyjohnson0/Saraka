namespace Saraka.Abstractions.Tests;

/// <summary>
/// Tests for <see cref="SarakaPath.Split"/>.
/// </summary>
public class SarakaPathTests
{
    [Theory]
    [InlineData("/", new string[0])]
    [InlineData("/a", new[] { "a" })]
    [InlineData("/data/photos/img 1.jpg", new[] { "data", "photos", "img 1.jpg" })]
    [InlineData("/.hidden/..x", new[] { ".hidden", "..x" })]
    public void Split_ReturnsTheNames(string path, string[] expected)
    {
        Assert.Equal(expected, SarakaPath.Split(path, "path"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a/b")]
    [InlineData("/a/")]
    [InlineData("//a")]
    [InlineData("/a//b")]
    [InlineData("/./a")]
    [InlineData("/a/..")]
    public void Split_InvalidPath_ThrowsArgumentException(string path)
    {
        var ex = Assert.Throws<ArgumentException>(() => SarakaPath.Split(path, "path"));

        Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public void Split_Null_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => SarakaPath.Split(null!, "path"));

        Assert.Equal("path", ex.ParamName);
    }
}
