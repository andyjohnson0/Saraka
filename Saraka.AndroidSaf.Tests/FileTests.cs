using System.Text;
using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Tests for reading files.
/// </summary>
public class FileTests : AndroidSafTestBase
{
    /// <summary>
    /// Creates "a.txt" in the root, opens a file system and gets the file.
    /// </summary>
    /// <param name="content">The file's text.</param>
    /// <param name="reportSize">False if the provider doesn't report the size.</param>
    /// <returns>The file.</returns>
    private async Task<IFile> CreateFileAsync(string content = "hello", bool reportSize = true)
    {
        Source.AddFile(RootId, "a.txt", content, reportSize: reportSize);
        var root = await OpenRootAsync();
        return await root.GetFileAsync("a.txt");
    }

    [Fact]
    public async Task GetLengthAsync_UsesTheSizeTheProviderReports()
    {
        var file = await CreateFileAsync("hello");

        Assert.Equal(5, await file.GetLengthAsync());
        Assert.Equal(0, Source.OpenReadCount);
    }

    [Fact]
    public async Task GetLengthAsync_NoSizeReported_ReadsTheFile()
    {
        var file = await CreateFileAsync("hello, world", reportSize: false);

        Assert.Equal(12, await file.GetLengthAsync());
        Assert.Equal(1, Source.OpenReadCount);
    }

    [Fact]
    public async Task GetLengthAsync_NoSizeReportedForALargeFile_CountsEveryByte()
    {
        Source.AddFile(RootId, "big.bin", new byte[200_000], reportSize: false);
        var root = await OpenRootAsync();

        Assert.Equal(200_000, await (await root.GetFileAsync("big.bin")).GetLengthAsync());
    }

    [Fact]
    public async Task ReadAllBytesAsync_ReturnsTheContents()
    {
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Source.AddFile(RootId, "a.bin", bytes);
        var root = await OpenRootAsync();

        Assert.Equal(bytes, await (await root.GetFileAsync("a.bin")).ReadAllBytesAsync());
    }

    [Fact]
    public async Task ReadAllTextAsync_ReadsUtf8()
    {
        var file = await CreateFileAsync("café ☕");

        Assert.Equal("café ☕", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task ReadAllTextAsync_SkipsAUtf8ByteOrderMark()
    {
        Source.AddFile(RootId, "a.txt", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("hello")).ToArray());
        var root = await OpenRootAsync();

        Assert.Equal("hello", await (await root.GetFileAsync("a.txt")).ReadAllTextAsync());
    }

    [Fact]
    public async Task Reads_OpenTheDocumentWithoutQueryingItFirst()
    {
        var file = await CreateFileAsync("hello");
        var queries = Source.QueryCount;

        using (await file.OpenReadAsync())
        {
        }
        await file.ReadAllBytesAsync();
        await file.ReadAllTextAsync();

        Assert.Equal(queries, Source.QueryCount);
        Assert.Equal(3, Source.OpenReadCount);
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsAReadableStreamOfTheContents()
    {
        var file = await CreateFileAsync("hello");

        using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);

        Assert.True(stream.CanRead);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    /// <summary>
    /// The names of the read operations that <c>Read</c> can perform.
    /// </summary>
    public static TheoryData<string> ReadOperations => new()
    {
        "GetLength",
        "ReadAllBytes",
        "ReadAllText",
        "OpenRead",
    };

    [Theory]
    [MemberData(nameof(ReadOperations))]
    public async Task Reads_FileRemoved_ThrowFileNotFoundException(string operation)
    {
        var id = Source.AddFile(RootId, "a.txt", "hello");
        var root = await OpenRootAsync();
        var file = await root.GetFileAsync("a.txt");

        Source.Remove(id);

        await Assert.ThrowsAsync<FileNotFoundException>(() => Read(operation, file));
    }

    // The file can disappear between the existence check and opening it.
    [Theory]
    [MemberData(nameof(ReadOperations))]
    public async Task Reads_OpenReportsTheFileMissing_ThrowFileNotFoundExceptionForThePath(string operation)
    {
        var file = await CreateFileAsync(reportSize: false);
        var missing = new FileNotFoundException("gone");
        Source.Fail = (method, id) => method == nameof(IDocumentSource.OpenRead) ? missing : null;

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => Read(operation, file));

        Assert.Contains("/a.txt", ex.Message);
        Assert.Same(missing, ex.InnerException);
    }

    [Theory]
    [InlineData("GetLength")]
    [InlineData("ReadAllBytes")]
    [InlineData("ReadAllText")]
    public async Task Reads_StreamReportsTheFileMissing_ThrowFileNotFoundExceptionForThePath(string operation)
    {
        var file = await CreateFileAsync(reportSize: false);
        var missing = new FileNotFoundException("gone");
        Source.ReadFailure = missing;

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => Read(operation, file));

        Assert.Contains("/a.txt", ex.Message);
        Assert.Same(missing, ex.InnerException);
    }

    [Theory]
    [MemberData(nameof(ReadOperations))]
    public async Task Reads_AccessDenied_ThrowUnauthorizedAccessException(string operation)
    {
        var file = await CreateFileAsync(reportSize: false);
        Source.Fail = (method, id) => method == nameof(IDocumentSource.OpenRead) ? new UnauthorizedAccessException() : null;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(operation, file));
    }

    [Theory]
    [InlineData("GetLength")]
    [InlineData("ReadAllBytes")]
    [InlineData("ReadAllText")]
    public async Task Reads_StreamFails_ThrowIOException(string operation)
    {
        var file = await CreateFileAsync(reportSize: false);
        Source.ReadFailure = new IOException("failed");

        var ex = await Assert.ThrowsAsync<IOException>(() => Read(operation, file));

        Assert.Equal("failed", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ReadOperations))]
    public async Task Reads_Cancelled_ThrowOperationCanceledException(string operation)
    {
        var file = await CreateFileAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(operation, file, cts.Token));
    }

    /// <summary>
    /// Performs a named read operation.
    /// </summary>
    /// <param name="operation">The operation's name, from <see cref="ReadOperations"/>.</param>
    /// <param name="file">The file.</param>
    /// <param name="ct">The cancellation token to pass.</param>
    /// <returns>A task for the operation.</returns>
    private static Task Read(string operation, IFile file, CancellationToken ct = default)
    {
        return operation switch
        {
            "GetLength" => file.GetLengthAsync(ct),
            "ReadAllBytes" => file.ReadAllBytesAsync(ct),
            "ReadAllText" => file.ReadAllTextAsync(ct),
            "OpenRead" => file.OpenReadAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }
}
