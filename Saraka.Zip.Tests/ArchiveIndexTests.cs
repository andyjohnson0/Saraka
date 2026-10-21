using System.Diagnostics;
using System.IO.Compression;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

/// <summary>
/// Tests for the index that a read-only file system builds when it opens. A writable file system
/// searches the archive instead, so the two must give the same answers for the same zip file.
/// </summary>
public class ArchiveIndexTests : ZipTestBase
{
    /// <summary>
    /// The entries of an awkward zip file: (name, last-write time, MS-DOS attributes, content).
    /// It has explicit and implied directories, nested implied directories, a file and a directory
    /// with the same name, duplicate names, names that aren't valid, and hidden entries.
    /// </summary>
    private static readonly (string Name, DateTime Time, FileAttributes Attributes, string? Content)[] AwkwardEntries =
    {
        ("b.txt", Local(2021, 1, 1), FileAttributes.Archive, "b"),
        ("explicit/", Local(2020, 5, 1), FileAttributes.Directory | FileAttributes.Hidden, null),
        ("explicit/x.txt", Local(2022, 3, 4), FileAttributes.Archive, "x"),
        ("implied/inner/deep.txt", Local(2023, 7, 8), FileAttributes.Archive, "deep"),
        ("implied/y.txt", Local(2019, 2, 2), FileAttributes.Hidden, "y"),
        ("a.txt", Local(2018, 1, 1), FileAttributes.ReadOnly, "first"),
        ("a.txt", Local(2024, 1, 1), FileAttributes.Archive, "second"),
        ("both", Local(2017, 6, 6), FileAttributes.Archive, "file"),
        ("both/child.txt", Local(2016, 6, 6), FileAttributes.Archive, "child"),
        ("bad//empty.txt", Local(2015, 1, 1), FileAttributes.Archive, "e"),
        ("bad/../up.txt", Local(2015, 1, 1), FileAttributes.Archive, "u"),
        ("bad\\back.txt", Local(2015, 1, 1), FileAttributes.Archive, "b"),
        ("/rooted.txt", Local(2014, 1, 1), FileAttributes.Archive, "r"),
        ("dup/", Local(2013, 1, 1), FileAttributes.Directory, null),
        ("dup/", Local(2012, 1, 1), FileAttributes.Directory | FileAttributes.Hidden, null),
        ("implied/inner/deeper/z.txt", Local(2011, 1, 1), FileAttributes.Archive, "z"),
    };

    [Fact]
    public async Task ReadOnly_GivesTheSameDirectoryEntriesAsWritable()
    {
        WriteAwkwardZip();

        // Read-only first: disposing a writable file system writes the zip file again.
        var actual = await DescribeAsync(canWrite: false);
        var expected = await DescribeAsync(canWrite: true);

        Assert.Equal(expected, actual);
        Assert.Contains(actual, line => line.StartsWith("  dir /both ", StringComparison.Ordinal));
        Assert.Contains(actual, line => line.StartsWith("  file /both ", StringComparison.Ordinal));
        Assert.Contains(actual, line => line.StartsWith("/implied/inner/deeper ", StringComparison.Ordinal));
        Assert.DoesNotContain(actual, line => line.Contains("rooted", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/a.txt", true, false)]
    [InlineData("/both", true, true)]
    [InlineData("/explicit", false, true)]
    [InlineData("/implied", false, true)]
    [InlineData("/implied/inner/deeper", false, true)]
    [InlineData("/implied/inner/deeper/z.txt", true, false)]
    [InlineData("/bad", false, true)]
    [InlineData("/dup", false, true)]
    [InlineData("/missing", false, false)]
    [InlineData("/implied/missing.txt", false, false)]
    [InlineData("/expl", false, false)]
    public async Task ReadOnly_PathLookupsMatchWritable(string path, bool isFile, bool isDirectory)
    {
        WriteAwkwardZip();

        foreach (var canWrite in new[] { false, true })
        {
            await using var fs = await FileSystem.OpenAsync(ZipPath, canWrite);
            Assert.Equal(isFile, await SucceedsAsync(() => fs.GetFileAsync(path)));
            Assert.Equal(isDirectory, await SucceedsAsync(() => fs.GetDirectoryAsync(path)));
        }
    }

    [Fact]
    public async Task ReadOnly_DuplicateNames_UseTheFirstEntry()
    {
        WriteAwkwardZip();
        await using var fs = await FileSystem.OpenAsync(ZipPath, canWrite: false);

        var file = await fs.GetFileAsync("/a.txt");

        Assert.Equal("first", await file.ReadAllTextAsync());
        Assert.Equal(FileAttributes.ReadOnly, await file.GetAttributesAsync());
        Assert.Equal(FileAttributes.Directory, await (await fs.GetDirectoryAsync("/dup")).GetAttributesAsync());
    }

    [Fact]
    public async Task ReadOnly_ImpliedDirectory_ReportsTheLatestTimeInsideIt()
    {
        WriteAwkwardZip();
        await using var fs = await FileSystem.OpenAsync(ZipPath, canWrite: false);

        var implied = await fs.GetDirectoryAsync("/implied");

        Assert.Equal(Local(2023, 7, 8).ToUniversalTime(), await implied.GetLastWriteTimeUtcAsync());
        Assert.Equal(Local(2020, 5, 1).ToUniversalTime(), await (await fs.GetDirectoryAsync("/explicit")).GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task ReadOnly_LargeDirectory_IsEnumeratedQuickly()
    {
        // Searching the archive for each entry made this take seconds; the index makes it linear.
        const int count = 40000;
        await Fs.DisposeAsync();
        File.Delete(ZipPath);
        using (var archive = ZipFile.Open(ZipPath, ZipArchiveMode.Create))
        {
            for (var i = 0; i < count; i++)
                archive.CreateEntry($"big/f{i:D5}.jpg", CompressionLevel.NoCompression);
        }
        await using var fs = await FileSystem.OpenAsync(ZipPath, canWrite: false);
        var big = await fs.GetDirectoryAsync("/big");
        var options = new EntryOptions { IncludeHiddenFiles = false, SortField = EntrySortField.LastWriteTime };

        var stopwatch = Stopwatch.StartNew();
        var entries = (await big.GetEntriesAsync(options)).ToList();
        for (var i = 0; i < count; i += 10)
            await fs.GetFileAsync($"/big/f{i:D5}.jpg");
        stopwatch.Stop();

        Assert.Equal(count, entries.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Took {stopwatch.Elapsed}.");
    }

    /// <summary>
    /// Opens the zip file and describes every directory's entries, with each entry's kind,
    /// attributes and time, in the order the file system returns them, using several options.
    /// </summary>
    /// <param name="canWrite">True to open the zip file writable, false for read-only.</param>
    /// <returns>One line per entry.</returns>
    private async Task<List<string>> DescribeAsync(bool canWrite)
    {
        var lines = new List<string>();
        var optionSets = new EntryOptions?[]
        {
            null,
            new() { IncludeHiddenFiles = false, IncludeHiddenDirectories = false },
            new() { SortField = EntrySortField.LastWriteTime },
            new() { SortField = EntrySortField.Name, SortDirection = EntrySortDirection.Descending },
        };
        await using var fs = await FileSystem.OpenAsync(ZipPath, canWrite);
        var pending = new Queue<IDirectory>();
        pending.Enqueue(fs.Root);
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            lines.Add($"{directory.Path} {await directory.GetAttributesAsync()} {await directory.GetLastWriteTimeUtcAsync():O}");
            foreach (var options in optionSets)
            {
                lines.Add($"  options {optionSets.ToList().IndexOf(options)}");
                foreach (var entry in await directory.GetEntriesAsync(options))
                {
                    var kind = entry is IDirectory ? "dir" : "file";
                    lines.Add($"  {kind} {entry.Path} {await entry.GetAttributesAsync()} {await entry.GetLastWriteTimeUtcAsync():O}");
                }
            }
            lines.Add("  files " + string.Join(",", (await directory.EnumerateFilesAsync("*.txt")).Select(f => f.Name)));
            lines.Add("  dirs " + string.Join(",", (await directory.EnumerateDirectoriesAsync("i*")).Select(d => d.Name)));
            foreach (var child in await directory.EnumerateDirectoriesAsync())
                pending.Enqueue(child);
        }
        return lines;
    }

    /// <summary>
    /// Replaces the zip file with <see cref="AwkwardEntries"/>, written directly with
    /// System.IO.Compression, leaving the file system closed.
    /// </summary>
    private void WriteAwkwardZip()
    {
        Fs.DisposeAsync().AsTask().GetAwaiter().GetResult();
        File.Delete(ZipPath);
        using var archive = ZipFile.Open(ZipPath, ZipArchiveMode.Create);
        foreach (var (name, time, attributes, content) in AwkwardEntries)
        {
            var entry = archive.CreateEntry(name);
            entry.LastWriteTime = new DateTimeOffset(time);
            entry.ExternalAttributes = (int)attributes;
            if (content != null)
            {
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
    }

    /// <summary>
    /// Runs a lookup and reports whether it found something.
    /// </summary>
    /// <param name="lookup">The lookup.</param>
    /// <returns>True if it succeeded, false if it threw a not-found exception.</returns>
    private static async Task<bool> SucceedsAsync(Func<Task> lookup)
    {
        try
        {
            await lookup();
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a local time at midnight, which zip can store exactly.
    /// </summary>
    private static DateTime Local(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Local);
}
