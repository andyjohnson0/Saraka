using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Tests for use from several threads and blocking on tasks.
/// </summary>
public class ThreadingTests : AndroidSafTestBase
{
    private const int FileCount = 20;

    [Fact]
    public async Task ParallelOperations_GiveConsistentResults()
    {
        var dirId = Source.AddFolder(RootId, "d");
        for (var n = 0; n < FileCount; n++)
            Source.AddFile(dirId, $"f{n}.txt", $"file {n}", lastModified: n);
        var fs = await OpenAsync();
        var dir = await fs.GetDirectoryAsync("/d");
        var byTime = new EntryOptions { SortField = EntrySortField.LastWriteTime };

        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            var n = i % FileCount;
            Assert.Equal($"file {n}", await (await fs.GetFileAsync($"/d/f{n}.txt")).ReadAllTextAsync());
            var names = (await dir.EnumerateFilesAsync(options: byTime)).Select(f => f.Name);
            Assert.Equal(Enumerable.Range(0, FileCount).Select(k => $"f{k}.txt"), names);
        })));
    }

    [Fact]
    public async Task BlockingOnTasks_WithAContextThatNeverRunsCallbacks_DoesNotDeadlock()
    {
        Source.AddFile(RootId, "a.txt", "hello");

        // The test's own awaits use ConfigureAwait(false), so only the library's code is tested.
        var fs = BlockingContext.Run(() => FileSystem.OpenAsync(Source));
        await using var disposeFs = fs;
        var file = BlockingContext.Run(() => fs.GetFileAsync("/a.txt"));
        var text = BlockingContext.Run(() => file.ReadAllTextAsync());
        var entries = BlockingContext.Run(() => fs.Root.GetEntriesAsync());
        var read = BlockingContext.Run(async () =>
        {
            using var stream = await file.OpenReadAsync().ConfigureAwait(false);
            return await stream.ReadAsync(new byte[10]).ConfigureAwait(false);
        });

        Assert.Equal("hello", text);
        Assert.Single(entries);
        Assert.Equal(5, read);
    }
}
