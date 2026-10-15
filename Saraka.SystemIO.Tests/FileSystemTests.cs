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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Root.GetEntriesAsync(cancelled));
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
}
