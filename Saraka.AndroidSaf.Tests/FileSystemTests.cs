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
    public async Task OpenAsync_NullOptions_UsesTheDefaults()
    {
        Source.AddFile(RootId, "b.txt");
        Source.AddFile(RootId, "a.txt");
        Source.AddFile(RootId, ".hidden.txt");
        Source.AddFolder(RootId, ".hidden");

        var root = await OpenRootAsync(null);

        Assert.Equal(new[] { "a.txt", "b.txt" }, Names(await root.GetEntriesAsync()));
    }

    public static TheoryData<string> InvalidOptions => new()
    {
        "SortField",
        "SortOrder",
        "NullFileType",
        "EmptyFileType",
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task OpenAsync_InvalidOptions_ThrowsArgumentException(string invalid)
    {
        var options = invalid switch
        {
            "SortField" => new FileSystemOptions { SortField = (SortField)99 },
            "SortOrder" => new FileSystemOptions { SortOrder = (SortOrder)99 },
            "NullFileType" => new FileSystemOptions { FileTypes = new[] { "png", null! } },
            "EmptyFileType" => new FileSystemOptions { FileTypes = new[] { "png", "" } },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => OpenAsync(options));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public async Task Options_ChangedAfterOpening_HaveNoEffect()
    {
        Source.AddFile(RootId, "a.png");
        Source.AddFile(RootId, "b.gif");
        Source.AddFile(RootId, ".c.png");
        var fileTypes = new[] { "png" };
        var options = new FileSystemOptions { FileTypes = fileTypes };
        var root = await OpenRootAsync(options);

        options.IncludeHiddenFiles = true;
        options.FileTypes = null;
        fileTypes[0] = "gif";

        Assert.Equal(new[] { "a.png" }, Names(await root.GetEntriesAsync()));
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        var fs = await OpenAsync();

        await fs.DisposeAsync();
        await fs.DisposeAsync();
    }

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
}
