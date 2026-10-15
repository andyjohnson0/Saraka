using System.Security.Cryptography;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

/// <summary>
/// Tests for a zip file opened with canWrite set to false.
/// </summary>
public class ReadOnlyFileSystemTests : ZipTestBase
{
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
        "Root.SetAttributes",
        "Root.SetLastWriteTime",
        "Root.CreateDirectory",
        "Root.CreateFile",
        "Root.CreateFileWithContent",
    };

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task WriteOperations_ThrowInvalidOperationExceptionAndChangeNothing(string operation)
    {
        await UseZipAsync(("a.txt", "hello"), ("d/", null), ("target/", null));
        await ReopenAsync(canWrite: false);
        var before = Hash();
        var file = await Root.GetFileAsync("a.txt");
        var dir = await Root.GetDirectoryAsync("d");
        var target = await Root.GetDirectoryAsync("target");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(operation, Root, file, dir, target));
        await Fs.DisposeAsync();

        Assert.Equal(before, Hash());
        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("/d", dir.Path);
    }

    [Fact]
    public async Task ReadOperations_Work()
    {
        await UseZipAsync(("a.txt", "hello"), ("d/", null));
        await ReopenAsync(canWrite: false);

        Assert.False(Fs.CanWrite);
        Assert.Equal(2, (await Root.GetEntriesAsync()).Count());
        var file = Assert.Single(await Root.EnumerateFilesAsync());
        Assert.Equal("hello", await file.ReadAllTextAsync());
        Assert.Equal(5, (await file.ReadAllBytesAsync()).Length);
        Assert.Equal(5, await file.GetLengthAsync());
        await using (var stream = await file.OpenReadAsync())
            Assert.True(stream.CanRead);
        await file.GetAttributesAsync();
        await file.GetCreationTimeUtcAsync();
        await file.GetLastWriteTimeUtcAsync();
        Assert.Equal("/d", (await Root.GetDirectoryAsync("d")).Path);
        Assert.Single(await Root.EnumerateDirectoriesAsync());
    }

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
            case "Root.SetAttributes": return root.SetAttributesAsync(FileAttributes.Hidden);
            case "Root.SetLastWriteTime": return root.SetLastWriteTimeUtcAsync(DateTime.UtcNow);
            case "Root.CreateDirectory": return root.CreateDirectoryAsync("new");
            case "Root.CreateFile": return root.CreateFileAsync("new.txt");
            case "Root.CreateFileWithContent": return root.CreateFileAsync("new.txt", new MemoryStream(new byte[] { 1 }));
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ZipPath)));
}
