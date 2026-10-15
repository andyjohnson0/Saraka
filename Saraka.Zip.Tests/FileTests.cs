using System.IO.Compression;
using System.Text;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

public class FileTests : ZipTestBase
{
    [Fact]
    public async Task GetLengthAsync_ReturnsUncompressedSize()
    {
        var file = await CreateFileAsync(Root, "a.txt", new string('x', 10000));

        Assert.Equal(10000, await file.GetLengthAsync());
        await ReopenAsync();
        Assert.Equal(10000, await (await Root.GetFileAsync("a.txt")).GetLengthAsync());
    }

    [Fact]
    public async Task GetLengthAsync_AfterWriting_ReturnsNewSize()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello world");

        await using (var stream = await file.OpenWriteAsync())
            await stream.WriteAsync(Encoding.UTF8.GetBytes("hi"));

        Assert.Equal(2, await file.GetLengthAsync());
    }

    [Fact]
    public async Task ReadAllBytesAsync_ReturnsContents()
    {
        var data = new byte[] { 0, 1, 2, 254, 255 };
        await Root.CreateFileAsync("a.bin", new MemoryStream(data));

        Assert.Equal(data, await (await Root.GetFileAsync("a.bin")).ReadAllBytesAsync());
        await ReopenAsync(canWrite: false);
        Assert.Equal(data, await (await Root.GetFileAsync("a.bin")).ReadAllBytesAsync());
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
        var file = await Root.CreateFileAsync("a.txt", new MemoryStream(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' }));

        Assert.Equal("hi", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task ReadAllTextAsync_DoesNotDetectUtf16()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hi")).ToArray();
        var file = await Root.CreateFileAsync("a.txt", new MemoryStream(bytes));

        Assert.NotEqual("hi", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task ReadAllTextAsync_CancelledToken_IsCancelled()
    {
        var file = await CreateFileAsync(Root, "a.txt", "x");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => file.ReadAllTextAsync(new CancellationToken(canceled: true)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenReadAsync_ReturnsReadOnlyStreamOverContents(bool canWrite)
    {
        await CreateFileAsync(Root, "a.txt", "hello");
        await ReopenAsync(canWrite);
        var file = await Root.GetFileAsync("a.txt");

        await using var stream = await file.OpenReadAsync();

        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenReadAsync_AllowsConcurrentReaders(bool canWrite)
    {
        await CreateFileAsync(Root, "a.txt", "hello");
        await CreateFileAsync(Root, "b.txt", "world");
        await ReopenAsync(canWrite);
        var a = await Root.GetFileAsync("a.txt");
        var b = await Root.GetFileAsync("b.txt");

        await using var first = await a.OpenReadAsync();
        await using var second = await a.OpenReadAsync();
        await using var third = await b.OpenReadAsync();

        Assert.Equal("hello", await new StreamReader(first).ReadToEndAsync());
        Assert.Equal("hello", await new StreamReader(second).ReadToEndAsync());
        Assert.Equal("world", await new StreamReader(third).ReadToEndAsync());
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

        Assert.Equal("hi", await file.ReadAllTextAsync());
        Assert.Equal("hi", (await ReadZipAsync())["a.txt"]);
    }

    [Fact]
    public async Task OpenWriteAsync_UpdatesLastWriteTime()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");
        var old = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await file.SetLastWriteTimeUtcAsync(old);

        await using (var stream = await file.OpenWriteAsync())
            await stream.WriteAsync(Encoding.UTF8.GetBytes("hi"));

        Assert.True(await file.GetLastWriteTimeUtcAsync() > old);
    }

    [Fact]
    public async Task OpenWriteAsync_WhileOpen_OtherAccessToTheFileThrowsIOException()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");

        await using (var stream = await file.OpenWriteAsync())
        {
            await Assert.ThrowsAsync<IOException>(() => file.ReadAllTextAsync());
            await Assert.ThrowsAsync<IOException>(() => file.OpenReadAsync());
            await Assert.ThrowsAsync<IOException>(() => file.OpenWriteAsync());
            await Assert.ThrowsAsync<IOException>(() => file.RenameAsync("b.txt"));
        }

        Assert.Equal("", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheEntry()
    {
        await CreateFileAsync(Root, "a.txt", "a");
        await CreateFileAsync(Root, "b.txt", "b");

        await (await Root.GetFileAsync("a.txt")).DeleteAsync();

        Assert.Equal(new[] { "b.txt" }, (await ReadZipAsync()).Keys);
    }

    [Fact]
    public async Task DeleteAsync_LastFileInImplicitDirectory_KeepsTheDirectory()
    {
        await UseZipAsync(("d/a.txt", "a"));

        await (await (await Root.GetDirectoryAsync("d")).GetFileAsync("a.txt")).DeleteAsync();

        Assert.Equal("/d", Assert.Single(await Root.GetEntriesAsync()).Path);
        Assert.Equal(new[] { "d/" }, (await ReadZipAsync()).Keys);
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
        await (await Root.GetFileAsync("a.txt")).DeleteAsync();

        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(file, operation));
    }

    [Theory]
    [MemberData(nameof(FileOperations))]
    public async Task Operations_PathIsNowADirectory_ThrowFileNotFoundException(string operation)
    {
        var file = await Root.CreateFileAsync("a");
        await (await Root.GetFileAsync("a")).DeleteAsync();
        await Root.CreateDirectoryAsync("a");

        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(file, operation));
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
