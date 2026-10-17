using System.Text;
using Saraka.Abstractions;

namespace Saraka.SystemIO.Tests;

/// <summary>
/// Tests for use from several threads, blocking on tasks, and streams that outlive the file system.
/// </summary>
public class ThreadingTests : FileSystemTestBase
{
    private const int FileCount = 20;

    /// <summary>
    /// Creates <see cref="FileCount"/> test files in a directory "d".
    /// </summary>
    private void CreateTestFiles()
    {
        Directory.CreateDirectory(HostPath("d"));
        for (var n = 0; n < FileCount; n++)
            File.WriteAllText(HostPath("d", $"f{n}.txt"), $"file {n}");
    }

    [Fact]
    public async Task ParallelOperations_GiveConsistentResults()
    {
        CreateTestFiles();
        var dir = await Root.GetDirectoryAsync("d");

        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            var n = i % FileCount;
            Assert.Equal($"file {n}", await (await Fs.GetFileAsync($"/d/f{n}.txt")).ReadAllTextAsync());
            using var content = new MemoryStream(Encoding.UTF8.GetBytes($"new {i}"));
            await dir.CreateFileAsync($"new{i}.txt", content);
            await dir.GetEntriesAsync(new EntryOptions { SortField = EntrySortField.Name });
        })));

        Assert.Equal(FileCount + 100, (await dir.GetEntriesAsync()).Count());
    }

    [Fact]
    public async Task BlockingOnTasks_WithAContextThatNeverRunsCallbacks_DoesNotDeadlock()
    {
        CreateTestFiles();
        var file = await Fs.GetFileAsync("/d/f1.txt");

        var text = BlockingContext.Run(() => file.ReadAllTextAsync());
        var bytes = BlockingContext.Run(() => file.ReadAllBytesAsync());
        var entries = BlockingContext.Run(() => Root.GetEntriesAsync());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        var created = BlockingContext.Run(() => Root.CreateFileAsync("blocked.txt", content));

        Assert.Equal("file 1", text);
        Assert.Equal(Encoding.UTF8.GetBytes("file 1"), bytes);
        Assert.Single(entries);
        Assert.Equal("/blocked.txt", created.Path);
    }

    [Fact]
    public async Task Streams_StayUsableAfterDispose()
    {
        CreateTestFiles();
        var readStream = await (await Fs.GetFileAsync("/d/f2.txt")).OpenReadAsync();
        var writeStream = await (await Fs.GetFileAsync("/d/f3.txt")).OpenWriteAsync();

        await Fs.DisposeAsync();

        using (var reader = new StreamReader(readStream))
            Assert.Equal("file 2", await reader.ReadToEndAsync());
        using (var writer = new StreamWriter(writeStream))
            await writer.WriteAsync("rewritten");
        Assert.Equal("rewritten", File.ReadAllText(HostPath("d", "f3.txt")));
    }
}
