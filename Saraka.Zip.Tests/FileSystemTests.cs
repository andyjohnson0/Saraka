using System.IO.Compression;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

public class FileSystemTests : ZipTestBase
{
    [Fact]
    public async Task OpenAsync_NullPath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => FileSystem.OpenAsync(null!, canWrite: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenAsync_EmptyPath_ThrowsArgumentException(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => FileSystem.OpenAsync(path, canWrite: true));
    }

    [Fact]
    public async Task OpenAsync_ReadOnlyMissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(TempPath, "missing.zip");

        await Assert.ThrowsAsync<FileNotFoundException>(() => FileSystem.OpenAsync(path, canWrite: false));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task OpenAsync_WritableMissingFile_CreatesAnEmptyZip()
    {
        var path = Path.Combine(TempPath, "new.zip");

        await using (var fs = await FileSystem.OpenAsync(path, canWrite: true))
            Assert.Empty(await fs.Root.GetEntriesAsync());

        using var archive = ZipFile.OpenRead(path);
        Assert.Empty(archive.Entries);
    }

    [Fact]
    public async Task OpenAsync_PathIsADirectory_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => FileSystem.OpenAsync(TempPath, canWrite: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenAsync_NotAZipFile_ThrowsInvalidDataExceptionAndLeavesFileAlone(bool canWrite)
    {
        var path = Path.Combine(TempPath, "bad.zip");
        File.WriteAllText(path, "this is not a zip file");

        await Assert.ThrowsAsync<InvalidDataException>(() => FileSystem.OpenAsync(path, canWrite));

        Assert.Equal("this is not a zip file", File.ReadAllText(path));
    }

    [Fact]
    public async Task OpenAsync_CancelledToken_IsCancelled()
    {
        var path = Path.Combine(TempPath, "new.zip");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FileSystem.OpenAsync(path, canWrite: true, new CancellationToken(canceled: true)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task OpenAsync_Writable_HoldsTheZipFileExclusively()
    {
        await Assert.ThrowsAsync<IOException>(() => FileSystem.OpenAsync(ZipPath, canWrite: true));
        await Assert.ThrowsAsync<IOException>(() => FileSystem.OpenAsync(ZipPath, canWrite: false));
    }

    [Fact]
    public async Task OpenAsync_ReadOnly_AllowsOtherReaders()
    {
        await ReopenAsync(canWrite: false);

        await using var second = await FileSystem.OpenAsync(ZipPath, canWrite: false);

        Assert.Empty(await second.Root.GetEntriesAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanWrite_IsTheValuePassedToOpenAsync(bool canWrite)
    {
        await ReopenAsync(canWrite);

        Assert.Equal(canWrite, Fs.CanWrite);
    }

    [Fact]
    public void Root_HasRootPathEmptyNameAndNoParent()
    {
        Assert.Equal("/", Root.Path);
        Assert.Equal(string.Empty, Root.Name);
        Assert.Null(Root.Parent);
        Assert.Same(Root, Root);
        Assert.Equal("/", Root.ToString());
    }

    [Fact]
    public async Task DisposeAsync_WritesChangesToTheZipFile()
    {
        var dir = await Root.CreateDirectoryAsync("data");
        await CreateFileAsync(dir, "a.txt", "hello");

        var entries = await ReadZipAsync();

        Assert.Equal(2, entries.Count);
        Assert.Null(entries["data/"]);
        Assert.Equal("hello", entries["data/a.txt"]);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        await Fs.DisposeAsync();
        await Fs.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ThenOperations_ThrowObjectDisposedException()
    {
        var file = await CreateFileAsync(Root, "a.txt", "x");

        await Fs.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => Root.GetEntriesAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Root.CreateFileAsync("b.txt"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => file.ReadAllTextAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => file.GetAttributesAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => file.RenameAsync("c.txt"));
    }

    [Fact]
    public async Task Operations_ReportExceptionsThroughTheTask()
    {
        var task = Root.GetFileAsync(null!);

        Assert.True(task.IsFaulted);
        await Assert.ThrowsAsync<ArgumentNullException>(() => task);
    }

    [Fact]
    public async Task Operations_CancelledToken_AreCancelled()
    {
        var cancelled = new CancellationToken(canceled: true);
        await CreateFileAsync(Root, "a.txt", "x");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Root.GetEntriesAsync(cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Root.GetFileAsync("a.txt", cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Root.CreateDirectoryAsync("d", cancelled));
        Assert.DoesNotContain(await Root.GetEntriesAsync(), e => e.Name == "d");
    }

    [Fact]
    public async Task Paths_MapToZipEntryNames()
    {
        var a = await Root.CreateDirectoryAsync("a");
        var b = await a.CreateDirectoryAsync("b");
        await CreateFileAsync(b, "c.txt", "x");

        var entries = await ReadZipAsync();

        Assert.Contains("a/b/c.txt", entries.Keys);
        Assert.Equal("/a/b/c.txt", (await (await (await Root.GetDirectoryAsync("a")).GetDirectoryAsync("b")).GetFileAsync("c.txt")).Path);
    }

    [Fact]
    public async Task Changes_SurviveReopening()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await CreateFileAsync(dir, "a.txt", "hello");
        await Root.CreateDirectoryAsync("empty");

        await ReopenAsync();

        var names = (await Root.GetEntriesAsync()).Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal);
        Assert.Equal(new[] { "/d", "/empty" }, names);
        Assert.Equal("hello", await (await (await Root.GetDirectoryAsync("d")).GetFileAsync("a.txt")).ReadAllTextAsync());
    }

    [Fact]
    public void NameComparer_IsCaseSensitive()
    {
        Assert.Same(StringComparer.Ordinal, Fs.NameComparer);
        Assert.False(Fs.NameComparer.Equals("a.txt", "A.txt"));
    }

    [Fact]
    public void DisplayName_IsTheZipFilesName()
    {
        Assert.Equal("test.zip", Fs.DisplayName);
    }

    [Fact]
    public async Task FileSystem_IsTheEntrysFileSystem()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var file = await CreateFileAsync(dir, "a.txt", "x");

        Assert.Same(Fs, Root.FileSystem);
        Assert.Same(Fs, dir.FileSystem);
        Assert.Same(Fs, file.FileSystem);
        Assert.Same(Fs, file.Parent!.FileSystem);
    }
}
