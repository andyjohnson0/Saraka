using Saraka.Abstractions;

namespace Saraka.SystemIO.Tests;

/// <summary>
/// Tests for a file system opened with canWrite set to false.
/// </summary>
public class ReadOnlyFileSystemTests : FileSystemTestBase
{
    private static readonly DateTime OriginalTime = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
        "Root.CreateDirectory",
        "Root.CreateFile",
        "Root.CreateFileWithContent",
    };

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task WriteOperations_ThrowInvalidOperationExceptionAndChangeNothing(string operation)
    {
        File.WriteAllText(HostPath("a.txt"), "hello");
        Directory.CreateDirectory(HostPath("d"));
        Directory.CreateDirectory(HostPath("target"));
        File.SetLastWriteTimeUtc(HostPath("a.txt"), OriginalTime);
        var before = Snapshot();

        await using var fs = await OpenReadOnlyAsync();
        var file = await fs.Root.GetFileAsync("a.txt");
        var dir = await fs.Root.GetDirectoryAsync("d");
        var target = await fs.Root.GetDirectoryAsync("target");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(operation, fs.Root, file, dir, target));

        Assert.Equal(before, Snapshot());
        Assert.Equal("hello", File.ReadAllText(HostPath("a.txt")));
        Assert.Equal(OriginalTime, File.GetLastWriteTimeUtc(HostPath("a.txt")));
        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("/d", dir.Path);
    }

    [Fact]
    public async Task ReadOperations_Work()
    {
        File.WriteAllText(HostPath("a.txt"), "hello");
        Directory.CreateDirectory(HostPath("d"));

        await using var fs = await OpenReadOnlyAsync();

        Assert.False(fs.CanWrite);
        Assert.Equal(2, (await fs.Root.GetEntriesAsync()).Count());
        var file = Assert.Single(await fs.Root.EnumerateFilesAsync());
        Assert.Equal("hello", await file.ReadAllTextAsync());
        Assert.Equal(5, (await file.ReadAllBytesAsync()).Length);
        Assert.Equal(5, await file.GetLengthAsync());
        await using (var stream = await file.OpenReadAsync())
            Assert.True(stream.CanRead);
        await file.GetAttributesAsync();
        await file.GetCreationTimeUtcAsync();
        await file.GetLastWriteTimeUtcAsync();
        Assert.Equal("/d", (await fs.Root.GetDirectoryAsync("d")).Path);
        Assert.Single(await fs.Root.EnumerateDirectoriesAsync());
    }

    /// <summary>
    /// Performs a named write operation.
    /// </summary>
    /// <param name="operation">The operation's name, from <c>WriteOperations</c>.</param>
    /// <returns>A task for the operation.</returns>
    private static Task Run(string operation, IDirectory root, IFile file, IDirectory dir, IDirectory target)
    {
        switch (operation)
        {
            case "File.Rename": return file.RenameAsync("b.txt");
            case "File.Move": return file.MoveToAsync(target);
            case "File.SetAttributes": return file.SetAttributesAsync(FileAttributes.ReadOnly);
            case "File.SetCreationTime": return file.SetCreationTimeUtcAsync(DateTime.UtcNow);
            case "File.SetLastWriteTime": return file.SetLastWriteTimeUtcAsync(DateTime.UtcNow);
            case "File.OpenWrite": return file.OpenWriteAsync();
            case "File.Delete": return file.DeleteAsync();
            case "Directory.Rename": return dir.RenameAsync("e");
            case "Directory.Move": return dir.MoveToAsync(target);
            case "Directory.SetAttributes": return dir.SetAttributesAsync(FileAttributes.ReadOnly);
            case "Directory.SetCreationTime": return dir.SetCreationTimeUtcAsync(DateTime.UtcNow);
            case "Directory.SetLastWriteTime": return dir.SetLastWriteTimeUtcAsync(DateTime.UtcNow);
            case "Directory.Delete": return dir.DeleteAsync();
            case "Directory.DeleteRecursive": return dir.DeleteAsync(recursive: true);
            case "Root.CreateDirectory": return root.CreateDirectoryAsync("new");
            case "Root.CreateFile": return root.CreateFileAsync("new.txt");
            case "Root.CreateFileWithContent": return root.CreateFileAsync("new.txt", new MemoryStream(new byte[] { 1 }));
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>
    /// Describes every file and directory under the root, to check that nothing changed.
    /// </summary>
    /// <returns>The description.</returns>
    private string Snapshot()
    {
        var entries = Directory.EnumerateFileSystemEntries(RootPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RootPath, path) + ":" + File.GetAttributes(path))
            .OrderBy(entry => entry, StringComparer.Ordinal);
        return string.Join("|", entries);
    }
}
