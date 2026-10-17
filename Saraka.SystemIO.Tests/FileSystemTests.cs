using Saraka.Abstractions;

namespace Saraka.SystemIO.Tests;

public class FileSystemTests : FileSystemTestBase
{
    [Fact]
    public async Task OpenAsync_NullPath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => FileSystem.OpenAsync(null!, canWrite: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenAsync_EmptyPath_ThrowsArgumentException(string rootPath)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => FileSystem.OpenAsync(rootPath, canWrite: true));
    }

    [Fact]
    public async Task OpenAsync_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => FileSystem.OpenAsync(HostPath("missing"), canWrite: true));
    }

    [Fact]
    public async Task OpenAsync_PathIsAFile_ThrowsDirectoryNotFoundException()
    {
        File.WriteAllText(HostPath("file.txt"), "x");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => FileSystem.OpenAsync(HostPath("file.txt"), canWrite: true));
    }

    [Fact]
    public async Task OpenAsync_CancelledToken_IsCancelled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FileSystem.OpenAsync(RootPath, canWrite: true, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task OpenAsync_RelativePath_IsResolvedAgainstCurrentDirectory()
    {
        File.WriteAllText(HostPath("marker.txt"), "x");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, RootPath);

        await using var fs = await FileSystem.OpenAsync(relative, canWrite: false);

        var file = await fs.Root.GetFileAsync("marker.txt");
        Assert.Equal("/marker.txt", file.Path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanWrite_IsTheValuePassedToOpenAsync(bool canWrite)
    {
        await using var fs = await FileSystem.OpenAsync(RootPath, canWrite);

        Assert.Equal(canWrite, fs.CanWrite);
    }

    [Fact]
    public void Root_HasRootPathEmptyNameAndNoParent()
    {
        Assert.Equal("/", Root.Path);
        Assert.Equal(string.Empty, Root.Name);
        Assert.Null(Root.Parent);
    }

    [Fact]
    public void Root_ReturnsTheSameInstance()
    {
        Assert.Same(Root, Root);
    }

    [Fact]
    public void Entry_ToString_ReturnsPath()
    {
        Assert.Equal("/", Root.ToString());
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Root.GetEntriesAsync(cancellationToken: cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Root.GetFileAsync("a.txt", cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Root.CreateDirectoryAsync("d", cancelled));
        Assert.False(Directory.Exists(HostPath("d")));
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
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        await Fs.DisposeAsync();
        await Fs.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_LeavesOpenStreamsUsable()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");
        await using var stream = await file.OpenReadAsync();

        await Fs.DisposeAsync();

        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Paths_UseForwardSlashOnEveryPlatform()
    {
        var a = await Root.CreateDirectoryAsync("a");
        var b = await a.CreateDirectoryAsync("b");
        var file = await b.CreateFileAsync("c.txt");

        Assert.Equal("/a/b/c.txt", file.Path);
        Assert.True(File.Exists(HostPath("a", "b", "c.txt")));
    }

    [Fact]
    public void NameComparer_IgnoresCaseOnlyOnWindowsAndMacOS()
    {
        var ignoresCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.Equal(ignoresCase, Fs.NameComparer.Equals("a.txt", "A.TXT"));
        Assert.True(Fs.NameComparer.Equals("a.txt", "a.txt"));
    }

    [Fact]
    public void DisplayName_IsTheRootDirectorysName()
    {
        Assert.Equal(Path.GetFileName(RootPath), Fs.DisplayName);
    }

    [Fact]
    public async Task DisplayName_ForADriveRoot_IsTheFullPath()
    {
        var driveRoot = Path.GetPathRoot(RootPath)!;
        await using var fs = await FileSystem.OpenAsync(driveRoot, canWrite: false);

        Assert.Equal(driveRoot, fs.DisplayName);
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

    // Lookup by path

    [Fact]
    public async Task GetDirectoryAsync_Root_ReturnsTheRoot()
    {
        Assert.Equal("/", (await Fs.GetDirectoryAsync("/")).Path);
    }

    [Fact]
    public async Task GetDirectoryAndFileAsync_NestedPaths_FindTheEntries()
    {
        Directory.CreateDirectory(HostPath("a", "b"));
        File.WriteAllText(HostPath("a", "b", "c.txt"), "hello");

        var dir = await Fs.GetDirectoryAsync("/a/b");
        var file = await Fs.GetFileAsync("/a/b/c.txt");

        Assert.Equal("/a/b", dir.Path);
        Assert.Equal("/a/b/c.txt", file.Path);
        Assert.Equal("hello", await file.ReadAllTextAsync());
        Assert.Equal("/a", dir.Parent!.Path);
    }

    [Fact]
    public async Task GetDirectoryAsync_Missing_ThrowsDirectoryNotFoundException()
    {
        File.WriteAllText(HostPath("f.txt"), "x");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Fs.GetDirectoryAsync("/missing"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Fs.GetDirectoryAsync("/f.txt"));
    }

    [Fact]
    public async Task GetFileAsync_Missing_ThrowsFileOrDirectoryNotFoundException()
    {
        Directory.CreateDirectory(HostPath("d"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => Fs.GetFileAsync("/d/missing.txt"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Fs.GetFileAsync("/d"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Fs.GetFileAsync("/missing/a.txt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("/a/")]
    [InlineData("/a//b")]
    [InlineData("/a/../b")]
    [InlineData("/a\\b")]
    public async Task PathLookups_InvalidPath_ThrowArgumentException(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Fs.GetDirectoryAsync(path));
        await Assert.ThrowsAsync<ArgumentException>(() => Fs.GetFileAsync(path));
    }

    [Fact]
    public async Task GetFileAsync_Root_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Fs.GetFileAsync("/"));
    }

    [Fact]
    public async Task PathLookups_Null_ThrowArgumentNullExceptionThroughTheTask()
    {
        Task? task = null;

        var thrown = Record.Exception(() => { task = Fs.GetFileAsync(null!); });

        Assert.Null(thrown);
        await Assert.ThrowsAsync<ArgumentNullException>(() => task!);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Fs.GetDirectoryAsync(null!));
    }

    [Fact]
    public async Task PathLookups_AfterDispose_ThrowObjectDisposedException()
    {
        await Fs.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => Fs.GetDirectoryAsync("/"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Fs.GetFileAsync("/a.txt"));
    }
}
