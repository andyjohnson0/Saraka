using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Tests for opening, options and disposal.
/// </summary>
public class FileSystemTests : AndroidSafTestBase
{
    [Fact]
    public async Task OpenAsync_RootHasEmptyNameSlashPathAndNoParent()
    {
        var fs = await OpenAsync();

        Assert.Equal(string.Empty, fs.Root.Name);
        Assert.Equal("/", fs.Root.Path);
        Assert.Null(fs.Root.Parent);
    }

    [Fact]
    public async Task CanWrite_IsFalse()
    {
        var fs = await OpenAsync();

        Assert.False(fs.CanWrite);
    }

    [Fact]
    public async Task OpenAsync_RootMissing_ThrowsDirectoryNotFoundException()
    {
        Source.Remove(RootId);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => OpenAsync());
    }

    [Fact]
    public async Task OpenAsync_RootIsAFile_ThrowsDirectoryNotFoundException()
    {
        Source.SetIsDirectory(RootId, false);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => OpenAsync());
    }

    [Fact]
    public async Task OpenAsync_AccessDenied_ThrowsUnauthorizedAccessException()
    {
        Source.Fail = (method, id) => new UnauthorizedAccessException();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => OpenAsync());
    }

    [Fact]
    public async Task OpenAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OpenAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        var fs = await OpenAsync();

        await fs.DisposeAsync();
        await fs.DisposeAsync();
    }

    /// <summary>
    /// The names of the operations that must fail after disposal.
    /// </summary>
    public static TheoryData<string> Operations => new()
    {
        "GetEntries",
        "GetFile",
        "GetDirectory",
        "EnumerateFiles",
        "EnumerateDirectories",
        "GetAttributes",
        "GetLastWriteTime",
        "GetCreationTime",
        "GetLength",
        "ReadAllBytes",
        "ReadAllText",
        "OpenRead",
        "Rename",
        "OpenWrite",
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task AfterDispose_OperationsThrowObjectDisposedException(string operation)
    {
        Source.AddFile(RootId, "a.txt", "hello");
        var fs = await OpenAsync();
        var root = fs.Root;
        var file = await root.GetFileAsync("a.txt");

        await fs.DisposeAsync();

        Task task = operation switch
        {
            "GetEntries" => root.GetEntriesAsync(),
            "GetFile" => root.GetFileAsync("a.txt"),
            "GetDirectory" => root.GetDirectoryAsync("d"),
            "EnumerateFiles" => root.EnumerateFilesAsync(),
            "EnumerateDirectories" => root.EnumerateDirectoriesAsync(),
            "GetAttributes" => file.GetAttributesAsync(),
            "GetLastWriteTime" => file.GetLastWriteTimeUtcAsync(),
            "GetCreationTime" => file.GetCreationTimeUtcAsync(),
            "GetLength" => file.GetLengthAsync(),
            "ReadAllBytes" => file.ReadAllBytesAsync(),
            "ReadAllText" => file.ReadAllTextAsync(),
            "OpenRead" => file.OpenReadAsync(),
            "Rename" => file.RenameAsync("b.txt"),
            "OpenWrite" => file.OpenWriteAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        await Assert.ThrowsAsync<ObjectDisposedException>(() => task);
    }

    [Fact]
    public async Task AfterDispose_OpenStreamsStayUsable()
    {
        Source.AddFile(RootId, "a.txt", "hello");
        var fs = await OpenAsync();
        var file = await fs.Root.GetFileAsync("a.txt");
        using var stream = await file.OpenReadAsync();

        await fs.DisposeAsync();

        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task NameComparer_IgnoresCase()
    {
        var fs = await OpenAsync();

        Assert.Same(StringComparer.OrdinalIgnoreCase, fs.NameComparer);
    }

    [Fact]
    public async Task DisplayName_IsTheRootFoldersDisplayName()
    {
        var fs = await OpenAsync();

        Assert.Equal(FakeDocumentSource.RootName, fs.DisplayName);
        Assert.Equal(string.Empty, fs.Root.Name);
    }

    [Fact]
    public async Task FileSystem_IsTheEntrysFileSystem()
    {
        var dirId = Source.AddFolder(RootId, "d");
        Source.AddFile(dirId, "a.txt");
        var fs = await OpenAsync();
        var dir = await fs.Root.GetDirectoryAsync("d");
        var file = await dir.GetFileAsync("a.txt");

        Assert.Same(fs, fs.Root.FileSystem);
        Assert.Same(fs, dir.FileSystem);
        Assert.Same(fs, file.FileSystem);
    }
}
