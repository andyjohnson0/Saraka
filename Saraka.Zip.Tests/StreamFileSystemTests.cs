using System.Text;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

/// <summary>
/// Tests for a zip file opened from a stream.
/// </summary>
public class StreamFileSystemTests : ZipTestBase
{
    [Fact]
    public async Task OpenAsync_NullStream_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => FileSystem.OpenAsync((Stream)null!, "test.zip"));
    }

    [Fact]
    public async Task OpenAsync_NullDisplayName_ThrowsArgumentNullExceptionAndDisposesTheStream()
    {
        var stream = await ZipStreamAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => FileSystem.OpenAsync(stream, null!));
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OpenAsync_StreamCantReadOrSeek_ThrowsArgumentExceptionAndDisposesTheStream(bool canRead, bool canSeek)
    {
        var stream = await ZipStreamAsync();
        stream.ReportCanRead = canRead;
        stream.ReportCanSeek = canSeek;

        await Assert.ThrowsAsync<ArgumentException>(() => FileSystem.OpenAsync(stream, "test.zip"));
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task OpenAsync_NotAZipFile_ThrowsInvalidDataExceptionAndDisposesTheStream()
    {
        var stream = new TrackingStream(Encoding.UTF8.GetBytes("this is not a zip file"));

        await Assert.ThrowsAsync<InvalidDataException>(() => FileSystem.OpenAsync(stream, "bad.zip"));
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task OpenAsync_CancelledToken_IsCancelledAndDisposesTheStream()
    {
        var stream = await ZipStreamAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FileSystem.OpenAsync(stream, "test.zip", new CancellationToken(canceled: true)));
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task OpenAsync_IsReadOnlyWithTheGivenDisplayName()
    {
        await using var fs = await FileSystem.OpenAsync(await ZipStreamAsync(), "photos.zip");

        Assert.False(fs.CanWrite);
        Assert.Equal("photos.zip", fs.DisplayName);
    }

    [Fact]
    public async Task ReadOperations_Work()
    {
        await using var fs = await FileSystem.OpenAsync(await ZipStreamAsync(("a.txt", "hello"), ("d/b.txt", "world")), "test.zip");

        Assert.Equal(2, (await fs.Root.GetEntriesAsync()).Count());
        var file = Assert.Single(await fs.Root.EnumerateFilesAsync());
        Assert.Equal("hello", await file.ReadAllTextAsync());
        Assert.Equal(5, (await file.ReadAllBytesAsync()).Length);
        Assert.Equal(5, await file.GetLengthAsync());
        Assert.Equal("world", await (await fs.GetFileAsync("/d/b.txt")).ReadAllTextAsync());
        await using (var stream = await file.OpenReadAsync())
        {
            Assert.True(stream.CanRead);
            Assert.False(stream.CanWrite);
        }
    }

    [Fact]
    public async Task WriteOperations_ThrowInvalidOperationException()
    {
        await using var fs = await FileSystem.OpenAsync(await ZipStreamAsync(("a.txt", "hello")), "test.zip");
        var file = await fs.GetFileAsync("/a.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.OpenWriteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => file.DeleteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fs.Root.CreateDirectoryAsync("new"));
    }

    [Fact]
    public async Task EmptyRoot_ReportsTheUnixEpoch()
    {
        await using var fs = await FileSystem.OpenAsync(await ZipStreamAsync(), "test.zip");

        Assert.Equal(DateTime.UnixEpoch, await fs.Root.GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheStream()
    {
        var stream = await ZipStreamAsync(("a.txt", "hello"));
        var fs = await FileSystem.OpenAsync(stream, "test.zip");

        await fs.DisposeAsync();

        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task ReadStreams_StayUsableAfterDispose()
    {
        var fs = await FileSystem.OpenAsync(await ZipStreamAsync(("a.txt", "hello")), "test.zip");
        var stream = await (await fs.GetFileAsync("/a.txt")).OpenReadAsync();

        await fs.DisposeAsync();

        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ParallelReads_ReturnTheRightContents()
    {
        var entries = Enumerable.Range(0, 20).Select(i => ($"f{i}.txt", (string?)new string((char)('a' + i), 10000 + i))).ToArray();
        await using var fs = await FileSystem.OpenAsync(await ZipStreamAsync(entries), "test.zip");

        var texts = await Task.WhenAll(entries.Select(e => Task.Run(async () =>
            await (await fs.GetFileAsync("/" + e.Item1)).ReadAllTextAsync())));

        Assert.Equal(entries.Select(e => e.Item2), texts);
    }

    /// <summary>
    /// Writes a zip file holding exactly the given entries, and returns its contents as a stream.
    /// </summary>
    /// <param name="entries">The entries, as for <see cref="ZipTestBase.UseZipAsync"/>.</param>
    /// <returns>A stream over the zip file's contents.</returns>
    private async Task<TrackingStream> ZipStreamAsync(params (string Name, string? Content)[] entries)
    {
        await UseZipAsync(entries);
        await Fs.DisposeAsync();
        var bytes = await File.ReadAllBytesAsync(ZipPath);
        await ReopenAsync();
        return new TrackingStream(bytes);
    }
}

/// <summary>
/// A stream over bytes in memory that records whether it has been disposed, and can report that it
/// can't read or seek.
/// </summary>
internal sealed class TrackingStream : MemoryStream
{
    /// <summary>
    /// Creates a read-only stream over some bytes.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    public TrackingStream(byte[] bytes)
        : base(bytes, writable: false)
    {
    }

    /// <summary>True once the stream has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>The value that <see cref="CanRead"/> reports while the stream is open.</summary>
    public bool ReportCanRead { get; set; } = true;

    /// <summary>The value that <see cref="CanSeek"/> reports while the stream is open.</summary>
    public bool ReportCanSeek { get; set; } = true;

    /// <inheritdoc/>
    public override bool CanRead => ReportCanRead && base.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => ReportCanSeek && base.CanSeek;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
