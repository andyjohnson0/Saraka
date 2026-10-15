using System.Text;
using Saraka.Abstractions;

namespace Saraka.SystemIO.Tests;

public class FileTests : FileSystemTestBase
{
    [Fact]
    public async Task GetLengthAsync_ReturnsSizeInBytes()
    {
        var file = await CreateFileAsync(Root, "a.txt", "héllo");

        Assert.Equal(6, await file.GetLengthAsync());
    }

    [Fact]
    public async Task ReadAllBytesAsync_ReturnsContents()
    {
        var data = new byte[] { 0, 1, 2, 254, 255 };
        File.WriteAllBytes(HostPath("a.bin"), data);
        var file = await Root.GetFileAsync("a.bin");

        Assert.Equal(data, await file.ReadAllBytesAsync());
    }

    [Fact]
    public async Task ReadAllBytesAsync_EmptyFile_ReturnsEmptyArray()
    {
        var file = await Root.CreateFileAsync("a.bin");

        Assert.Empty(await file.ReadAllBytesAsync());
    }

    [Fact]
    public async Task ReadAllTextAsync_DecodesUtf8()
    {
        var file = await CreateFileAsync(Root, "a.txt", "héllo wörld");

        Assert.Equal("héllo wörld", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task ReadAllTextAsync_SkipsUtf8ByteOrderMark()
    {
        File.WriteAllBytes(HostPath("a.txt"), new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' });
        var file = await Root.GetFileAsync("a.txt");

        Assert.Equal("hi", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task ReadAllTextAsync_DoesNotDetectUtf16()
    {
        // The contract says UTF-8, so a UTF-16 byte order mark doesn't switch the decoding.
        File.WriteAllBytes(HostPath("a.txt"), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hi")).ToArray());
        var file = await Root.GetFileAsync("a.txt");

        Assert.NotEqual("hi", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task ReadAllTextAsync_CancelledToken_IsCancelled()
    {
        var file = await CreateFileAsync(Root, "a.txt", "x");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => file.ReadAllTextAsync(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsReadOnlyStreamOverContents()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");

        await using var stream = await file.OpenReadAsync();

        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenReadAsync_AllowsConcurrentReaders()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");

        await using var first = await file.OpenReadAsync();
        await using var second = await file.OpenReadAsync();

        Assert.Equal(5, first.Length);
        Assert.Equal(5, second.Length);
    }

    [Fact]
    public async Task OpenWriteAsync_TruncatesAndWrites()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello world");

        await using (var stream = await file.OpenWriteAsync())
        {
            Assert.True(stream.CanWrite);
            await stream.WriteAsync(Encoding.UTF8.GetBytes("hi"));
        }

        Assert.Equal("hi", File.ReadAllText(HostPath("a.txt")));
        Assert.Equal(2, await file.GetLengthAsync());
    }

    [Fact]
    public async Task OpenWriteAsync_WritingUpdatesLastWriteTime()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");
        var old = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await file.SetLastWriteTimeUtcAsync(old);

        await using (var stream = await file.OpenWriteAsync())
            await stream.WriteAsync(Encoding.UTF8.GetBytes("hi"));

        Assert.True(await file.GetLastWriteTimeUtcAsync() > old);
    }

    [Fact]
    public async Task DeleteAsync_DeletesTheFile()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await file.DeleteAsync();

        Assert.False(File.Exists(HostPath("a.txt")));
    }

    public static TheoryData<string> FileOperations => new()
    {
        "GetLength",
        "ReadAllBytes",
        "ReadAllText",
        "OpenRead",
        "OpenWrite",
        "Delete",
    };

    [Theory]
    [MemberData(nameof(FileOperations))]
    public async Task Operations_MissingFile_ThrowFileNotFoundException(string operation)
    {
        var file = await Root.CreateFileAsync("a.txt");
        File.Delete(HostPath("a.txt"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(file, operation));

        Assert.False(File.Exists(HostPath("a.txt")));
    }

    [Theory]
    [MemberData(nameof(FileOperations))]
    public async Task Operations_PathIsNowADirectory_ThrowFileNotFoundException(string operation)
    {
        var file = await Root.CreateFileAsync("a");
        File.Delete(HostPath("a"));
        Directory.CreateDirectory(HostPath("a"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(file, operation));

        Assert.True(Directory.Exists(HostPath("a")));
    }

    private static async Task Run(IFile file, string operation)
    {
        switch (operation)
        {
            case "GetLength": await file.GetLengthAsync(); break;
            case "ReadAllBytes": await file.ReadAllBytesAsync(); break;
            case "ReadAllText": await file.ReadAllTextAsync(); break;
            case "OpenRead": (await file.OpenReadAsync()).Dispose(); break;
            case "OpenWrite": (await file.OpenWriteAsync()).Dispose(); break;
            case "Delete": await file.DeleteAsync(); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
