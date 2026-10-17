using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Tests for the members shared by files and directories: attributes, times, existence and the
/// write operations, which all fail.
/// </summary>
public class EntryTests : AndroidSafTestBase
{
    [Fact]
    public async Task GetAttributesAsync_DistinguishesFilesAndDirectories()
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();

        Assert.Equal(FileAttributes.Normal, await (await root.GetFileAsync("a.txt")).GetAttributesAsync());
        Assert.Equal(FileAttributes.Directory, await (await root.GetDirectoryAsync("d")).GetAttributesAsync());
        Assert.Equal(FileAttributes.Directory, await root.GetAttributesAsync());
    }

    [Fact]
    public async Task GetAttributesAsync_HiddenNames_AreHidden()
    {
        Source.AddFile(RootId, ".a.txt");
        Source.AddFolder(RootId, ".d");
        var root = await OpenRootAsync();

        Assert.Equal(FileAttributes.Hidden, await (await root.GetFileAsync(".a.txt")).GetAttributesAsync());
        Assert.Equal(FileAttributes.Directory | FileAttributes.Hidden, await (await root.GetDirectoryAsync(".d")).GetAttributesAsync());
    }

    [Fact]
    public async Task GetLastWriteTimeUtcAsync_ConvertsTheProvidersMilliseconds()
    {
        Source.AddFile(RootId, "a.txt", lastModified: 1_690_000_000_123);
        Source.AddFolder(RootId, "d", lastModified: 1_620_000_000_000);
        var root = await OpenRootAsync();

        var fileTime = await (await root.GetFileAsync("a.txt")).GetLastWriteTimeUtcAsync();
        var dirTime = await (await root.GetDirectoryAsync("d")).GetLastWriteTimeUtcAsync();

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_690_000_000_123).UtcDateTime, fileTime);
        Assert.Equal(DateTimeKind.Utc, fileTime.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_620_000_000_000).UtcDateTime, dirTime);
    }

    [Fact]
    public async Task GetLastWriteTimeUtcAsync_NoTimeRecorded_ReturnsTheUnixEpoch()
    {
        Source.AddFile(RootId, "a.txt", lastModified: null);
        var root = await OpenRootAsync();

        var time = await (await root.GetFileAsync("a.txt")).GetLastWriteTimeUtcAsync();

        Assert.Equal(DateTime.UnixEpoch, time);
        Assert.Equal(DateTimeKind.Utc, time.Kind);
    }

    [Fact]
    public async Task GetCreationTimeUtcAsync_ReturnsTheLastWriteTime()
    {
        Source.AddFile(RootId, "a.txt", lastModified: 1_690_000_000_000);
        var root = await OpenRootAsync();
        var file = await root.GetFileAsync("a.txt");

        Assert.Equal(await file.GetLastWriteTimeUtcAsync(), await file.GetCreationTimeUtcAsync());
    }

    [Fact]
    public async Task Metadata_FileRemoved_ThrowsFileNotFoundException()
    {
        var id = Source.AddFile(RootId, "a.txt");
        var root = await OpenRootAsync();
        var file = await root.GetFileAsync("a.txt");

        Source.Remove(id);

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetAttributesAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetLastWriteTimeUtcAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetCreationTimeUtcAsync());
    }

    [Fact]
    public async Task Metadata_DirectoryRemoved_ThrowsDirectoryNotFoundException()
    {
        var id = Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();
        var dir = await root.GetDirectoryAsync("d");

        Source.Remove(id);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetAttributesAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task Metadata_FileBecameADirectory_ThrowsFileNotFoundException()
    {
        var id = Source.AddFile(RootId, "a");
        var root = await OpenRootAsync();
        var file = await root.GetFileAsync("a");

        Source.SetIsDirectory(id, true);

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetAttributesAsync());
    }

    [Fact]
    public async Task Metadata_AccessDenied_ThrowsUnauthorizedAccessException()
    {
        Source.AddFile(RootId, "a.txt");
        var root = await OpenRootAsync();
        var file = await root.GetFileAsync("a.txt");
        Source.Fail = (method, id) => new UnauthorizedAccessException();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => file.GetAttributesAsync());
    }

    // A documented divergence: an entry follows its document ID, so with a provider whose IDs
    // survive a rename, the entry keeps working under its old name.
    [Fact]
    public async Task Entry_DocumentRenamedByTheProvider_KeepsWorkingUnderItsOldPath()
    {
        var id = Source.AddFile(RootId, "a.txt", "hello");
        var root = await OpenRootAsync();
        var file = await root.GetFileAsync("a.txt");

        Source.Rename(id, "b.txt");

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("hello", await file.ReadAllTextAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => root.GetFileAsync("a.txt"));
    }

    [Fact]
    public async Task ToString_ReturnsThePath()
    {
        var id = Source.AddFolder(RootId, "d");
        Source.AddFile(id, "a.txt");
        var root = await OpenRootAsync();
        var dir = await root.GetDirectoryAsync("d");

        Assert.Equal("/d/a.txt", (await dir.GetFileAsync("a.txt")).ToString());
        Assert.Equal("/", root.ToString());
    }

    /// <summary>
    /// The names of the write operations that the test class can perform.
    /// </summary>
    public static TheoryData<string> WriteOperations => new()
    {
        "File.Rename",
        "File.Move",
        "File.SetAttributes",
        "File.SetCreationTime",
        "File.SetLastWriteTime",
        "File.OpenWrite",
        "File.Delete",
        "Directory.Rename",
        "Directory.Move",
        "Directory.SetAttributes",
        "Directory.SetCreationTime",
        "Directory.SetLastWriteTime",
        "Directory.Delete",
        "Directory.DeleteRecursive",
        "Directory.CreateDirectory",
        "Directory.CreateFile",
        "Directory.CreateFileWithContent",
        "Root.Rename",
        "Root.Delete",
        "Root.CreateFile",
    };

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task WriteOperations_ThrowInvalidOperationException(string operation)
    {
        var (root, file, dir) = await CreateEntriesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunWrite(operation, root, file, dir, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task WriteOperations_Cancelled_ThrowOperationCanceledException(string operation)
    {
        var (root, file, dir) = await CreateEntriesAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunWrite(operation, root, file, dir, cts.Token));
    }

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task WriteOperations_AfterDispose_ThrowObjectDisposedException(string operation)
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFolder(RootId, "d");
        var fs = await OpenAsync();
        var file = await fs.Root.GetFileAsync("a.txt");
        var dir = await fs.Root.GetDirectoryAsync("d");

        await fs.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => RunWrite(operation, fs.Root, file, dir, CancellationToken.None));
    }

    [Fact]
    public async Task WriteOperations_ChangeNothing()
    {
        var (root, file, dir) = await CreateEntriesAsync();

        foreach (var operation in WriteOperations.Cast<object[]>().Select(row => (string)row[0]))
            await Assert.ThrowsAsync<InvalidOperationException>(() => RunWrite(operation, root, file, dir, CancellationToken.None));

        Assert.Equal(new[] { "d", "a.txt" }, Names(await root.GetEntriesAsync(ByName())));
        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("/d", dir.Path);
        Assert.Equal("hello", await file.ReadAllTextAsync());
    }

    /// <summary>
    /// Write operations with an invalid argument, and the exception each must throw.
    /// </summary>
    public static TheoryData<string, Type> InvalidWriteArguments => new()
    {
        { "Rename.Null", typeof(ArgumentNullException) },
        { "Rename.Invalid", typeof(ArgumentException) },
        { "Move.Null", typeof(ArgumentNullException) },
        { "CreateDirectory.Null", typeof(ArgumentNullException) },
        { "CreateDirectory.Invalid", typeof(ArgumentException) },
        { "CreateFile.Null", typeof(ArgumentNullException) },
        { "CreateFile.Invalid", typeof(ArgumentException) },
        { "CreateFileWithContent.NullContent", typeof(ArgumentNullException) },
        { "CreateFileWithContent.UnreadableContent", typeof(ArgumentException) },
    };

    // As in the other implementations, arguments are checked before writability.
    [Theory]
    [MemberData(nameof(InvalidWriteArguments))]
    public async Task WriteOperations_InvalidArguments_AreReportedFirst(string operation, Type expected)
    {
        var (root, file, _) = await CreateEntriesAsync();
        using var unreadable = new UnreadableStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Task task = operation switch
        {
            "Rename.Null" => file.RenameAsync(null!, cts.Token),
            "Rename.Invalid" => file.RenameAsync("a/b", cts.Token),
            "Move.Null" => file.MoveToAsync(null!, cts.Token),
            "CreateDirectory.Null" => root.CreateDirectoryAsync(null!, cts.Token),
            "CreateDirectory.Invalid" => root.CreateDirectoryAsync("..", cts.Token),
            "CreateFile.Null" => root.CreateFileAsync(null!, cts.Token),
            "CreateFile.Invalid" => root.CreateFileAsync("", cts.Token),
            "CreateFileWithContent.NullContent" => root.CreateFileAsync("b.txt", null!, cts.Token),
            "CreateFileWithContent.UnreadableContent" => root.CreateFileAsync("b.txt", unreadable, cts.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => task);
        Assert.IsType(expected, ex);
    }

    [Fact]
    public async Task WriteOperations_ExceptionsAreReportedThroughTheTask()
    {
        var (_, file, _) = await CreateEntriesAsync();
        Task? task = null;

        var thrown = Record.Exception(() => { task = file.RenameAsync(null!); });

        Assert.Null(thrown);
        await Assert.ThrowsAsync<ArgumentNullException>(() => task!);
    }

    /// <summary>
    /// Creates a file "a.txt" and a folder "d" in the root, and opens them.
    /// </summary>
    /// <returns>The root, the file and the folder.</returns>
    private async Task<(IDirectory Root, IFile File, IDirectory Dir)> CreateEntriesAsync()
    {
        Source.AddFile(RootId, "a.txt", "hello");
        Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();
        return (root, await root.GetFileAsync("a.txt"), await root.GetDirectoryAsync("d"));
    }

    /// <summary>
    /// Performs a named write operation.
    /// </summary>
    /// <param name="operation">The operation's name, from <see cref="WriteOperations"/>.</param>
    /// <param name="root">The root directory.</param>
    /// <param name="file">A file.</param>
    /// <param name="dir">A directory.</param>
    /// <param name="ct">The cancellation token to pass.</param>
    /// <returns>A task for the operation.</returns>
    private static Task RunWrite(string operation, IDirectory root, IFile file, IDirectory dir, CancellationToken ct)
    {
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return operation switch
        {
            "File.Rename" => file.RenameAsync("b.txt", ct),
            "File.Move" => file.MoveToAsync(dir, ct),
            "File.SetAttributes" => file.SetAttributesAsync(FileAttributes.ReadOnly, ct),
            "File.SetCreationTime" => file.SetCreationTimeUtcAsync(time, ct),
            "File.SetLastWriteTime" => file.SetLastWriteTimeUtcAsync(time, ct),
            "File.OpenWrite" => file.OpenWriteAsync(ct),
            "File.Delete" => file.DeleteAsync(ct),
            "Directory.Rename" => dir.RenameAsync("e", ct),
            "Directory.Move" => dir.MoveToAsync(root, ct),
            "Directory.SetAttributes" => dir.SetAttributesAsync(FileAttributes.Hidden, ct),
            "Directory.SetCreationTime" => dir.SetCreationTimeUtcAsync(time, ct),
            "Directory.SetLastWriteTime" => dir.SetLastWriteTimeUtcAsync(time, ct),
            "Directory.Delete" => dir.DeleteAsync(false, ct),
            "Directory.DeleteRecursive" => dir.DeleteAsync(true, ct),
            "Directory.CreateDirectory" => dir.CreateDirectoryAsync("e", ct),
            "Directory.CreateFile" => dir.CreateFileAsync("b.txt", ct),
            "Directory.CreateFileWithContent" => dir.CreateFileAsync("b.txt", new MemoryStream(new byte[] { 1 }), ct),
            "Root.Rename" => root.RenameAsync("r", ct),
            "Root.Delete" => root.DeleteAsync(true, ct),
            "Root.CreateFile" => root.CreateFileAsync("b.txt", ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    /// <summary>
    /// A stream that reports it can't be read.
    /// </summary>
    private sealed class UnreadableStream : MemoryStream
    {
        /// <inheritdoc/>
        public override bool CanRead => false;
    }
}
