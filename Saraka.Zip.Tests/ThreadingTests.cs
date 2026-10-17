using System.Text;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

/// <summary>
/// Tests for use from several threads, blocking on tasks, and streams that outlive the file system.
/// </summary>
public class ThreadingTests : ZipTestBase
{
    private const int FileCount = 20;

    /// <summary>
    /// The contents of test file <paramref name="n"/>: different for each file, and long enough
    /// to be compressed across several reads.
    /// </summary>
    /// <param name="n">The file number.</param>
    /// <returns>The file's text.</returns>
    private static string Content(int n) => string.Concat(Enumerable.Repeat($"file {n} line\n", 2000 + n));

    /// <summary>
    /// Replaces the zip file with <see cref="FileCount"/> test files, then opens it.
    /// </summary>
    /// <param name="canWrite">True to open it writable.</param>
    private async Task UseTestFilesAsync(bool canWrite)
    {
        await UseZipAsync(Enumerable.Range(0, FileCount).Select(n => ($"d/f{n}.txt", (string?)Content(n))).ToArray());
        await ReopenAsync(canWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParallelReads_ReturnTheRightContents(bool canWrite)
    {
        await UseTestFilesAsync(canWrite);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            var n = i % FileCount;
            var file = await Fs.GetFileAsync($"/d/f{n}.txt");
            Assert.Equal(Content(n), await file.ReadAllTextAsync());
            using (var stream = await file.OpenReadAsync())
            using (var reader = new StreamReader(stream))
                Assert.Equal(Content(n), await reader.ReadToEndAsync());
            Assert.Equal(FileCount, (await (await Root.GetDirectoryAsync("d")).GetEntriesAsync()).Count());
        })));
    }

    [Fact]
    public async Task ParallelWritesAndReads_LeaveAConsistentArchive()
    {
        await UseTestFilesAsync(canWrite: true);
        var dir = await Root.GetDirectoryAsync("d");

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes($"new {i}"));
            await dir.CreateFileAsync($"new{i}.txt", content);
            var n = i % FileCount;
            Assert.Equal(Content(n), await (await dir.GetFileAsync($"f{n}.txt")).ReadAllTextAsync());
            await dir.GetEntriesAsync(new EntryOptions { SortField = EntrySortField.Name });
        })));
        await ReopenAsync(canWrite: false);

        var reopened = await Root.GetDirectoryAsync("d");
        Assert.Equal(FileCount + 50, (await reopened.GetEntriesAsync()).Count());
        for (var i = 0; i < 50; i++)
            Assert.Equal($"new {i}", await (await reopened.GetFileAsync($"new{i}.txt")).ReadAllTextAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadStreams_StayUsableAfterDispose(bool canWrite)
    {
        await UseTestFilesAsync(canWrite);
        var stream = await (await Fs.GetFileAsync("/d/f3.txt")).OpenReadAsync();

        await Fs.DisposeAsync();

        using (stream)
        using (var reader = new StreamReader(stream))
            Assert.Equal(Content(3), await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockingOnTasks_WithAContextThatNeverRunsCallbacks_DoesNotDeadlock(bool canWrite)
    {
        await UseTestFilesAsync(canWrite);
        var file = await Fs.GetFileAsync("/d/f1.txt");

        var text = BlockingContext.Run(() => file.ReadAllTextAsync());
        var entries = BlockingContext.Run(() => Root.GetEntriesAsync(new EntryOptions { SortField = EntrySortField.Name }));
        // The test's own awaits use ConfigureAwait(false), so only the library's code is tested.
        var length = BlockingContext.Run(async () =>
        {
            using var stream = await file.OpenReadAsync().ConfigureAwait(false);
            var buffer = new byte[1000];
            return await stream.ReadAsync(buffer).ConfigureAwait(false);
        });

        Assert.Equal(Content(1), text);
        Assert.Single(entries);
        Assert.True(length > 0);
        if (canWrite)
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));
            var created = BlockingContext.Run(() => Root.CreateFileAsync("blocked.txt", content));
            Assert.Equal("/blocked.txt", created.Path);
        }
    }
}
