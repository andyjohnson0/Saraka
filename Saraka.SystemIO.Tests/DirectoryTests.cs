using System.Text;
using Saraka.Abstractions;

namespace Saraka.SystemIO.Tests;

public class DirectoryTests : FileSystemTestBase
{
    /// <summary>
    /// Names that every implementation must reject.
    /// </summary>
    public static TheoryData<string> InvalidNames => EntryTests.InvalidNames;

    // Listing

    [Fact]
    public async Task GetEntriesAsync_EmptyDirectory_ReturnsNothing()
    {
        Assert.Empty(await Root.GetEntriesAsync());
    }

    // Only Linux and macOS allow "\" in a file name, so on Windows there's nothing to test.
    [Fact]
    public async Task Enumerations_LeaveOutNamesThatCantBeLookedUp()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.WriteAllText(HostPath("a\\b.txt"), "x");
        Directory.CreateDirectory(HostPath("c\\d"));
        File.WriteAllText(HostPath("ok.txt"), "x");

        Assert.Equal(new[] { "ok.txt" }, (await Root.GetEntriesAsync()).Select(e => e.Name));
        Assert.Equal(new[] { "ok.txt" }, (await Root.EnumerateFilesAsync()).Select(e => e.Name));
        Assert.Empty(await Root.EnumerateDirectoriesAsync());
    }

    [Fact]
    public async Task GetEntriesAsync_ReturnsFilesAndDirectoriesWithTheRightTypes()
    {
        File.WriteAllText(HostPath("a.txt"), "x");
        Directory.CreateDirectory(HostPath("d"));
        Directory.CreateDirectory(HostPath("d", "nested"));

        var entries = (await Root.GetEntriesAsync()).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(2, entries.Count);
        Assert.IsAssignableFrom<IFile>(entries[0]);
        Assert.Equal("/a.txt", entries[0].Path);
        Assert.IsAssignableFrom<IDirectory>(entries[1]);
        Assert.Equal("/d", entries[1].Path);
    }

    [Fact]
    public async Task GetEntriesAsync_IncludesHiddenEntries()
    {
        CreateHiddenFile(".hidden");

        var entries = await Root.GetEntriesAsync();

        Assert.Equal(".hidden", Assert.Single(entries).Name);
    }

    [Fact]
    public async Task GetEntriesAsync_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        Directory.Delete(HostPath("d"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetEntriesAsync());
    }

    // Lookup

    [Fact]
    public async Task GetFileAsync_ExistingFile_ReturnsIt()
    {
        File.WriteAllText(HostPath("a.txt"), "hello");

        var file = await Root.GetFileAsync("a.txt");

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("hello", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task GetDirectoryAsync_ExistingDirectory_ReturnsIt()
    {
        Directory.CreateDirectory(HostPath("d"));

        var dir = await Root.GetDirectoryAsync("d");

        Assert.Equal("/d", dir.Path);
    }

    [Fact]
    public async Task GetFileAsync_MissingName_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => Root.GetFileAsync("missing"));
    }

    [Fact]
    public async Task GetFileAsync_NameIsADirectory_ThrowsFileNotFoundException()
    {
        Directory.CreateDirectory(HostPath("d"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => Root.GetFileAsync("d"));
    }

    [Fact]
    public async Task GetDirectoryAsync_MissingName_ThrowsDirectoryNotFoundException()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Root.GetDirectoryAsync("missing"));
    }

    [Fact]
    public async Task GetDirectoryAsync_NameIsAFile_ThrowsDirectoryNotFoundException()
    {
        File.WriteAllText(HostPath("a.txt"), "x");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Root.GetDirectoryAsync("a.txt"));
    }

    [Fact]
    public async Task Lookups_NullName_ThrowArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.GetFileAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.GetDirectoryAsync(null!));
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task Lookups_InvalidName_ThrowArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Root.GetFileAsync(name));
        await Assert.ThrowsAsync<ArgumentException>(() => Root.GetDirectoryAsync(name));
    }

    [Fact]
    public async Task Lookups_MissingDirectory_ThrowDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await dir.CreateFileAsync("a.txt");
        Directory.Delete(HostPath("d"), recursive: true);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetFileAsync("a.txt"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetDirectoryAsync("x"));
    }

    // Enumeration

    [Fact]
    public async Task EnumerateFilesAsync_DefaultPattern_ReturnsOnlyFiles()
    {
        File.WriteAllText(HostPath("a.txt"), "x");
        File.WriteAllText(HostPath("b.dat"), "x");
        Directory.CreateDirectory(HostPath("d"));

        var names = (await Root.EnumerateFilesAsync()).Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal);

        Assert.Equal(new[] { "/a.txt", "/b.dat" }, names);
    }

    [Fact]
    public async Task EnumerateDirectoriesAsync_DefaultPattern_ReturnsOnlyDirectories()
    {
        File.WriteAllText(HostPath("a.txt"), "x");
        Directory.CreateDirectory(HostPath("d1"));
        Directory.CreateDirectory(HostPath("d2"));

        var names = (await Root.EnumerateDirectoriesAsync()).Select(d => d.Path).OrderBy(p => p, StringComparer.Ordinal);

        Assert.Equal(new[] { "/d1", "/d2" }, names);
    }

    [Fact]
    public async Task EnumerateFilesAsync_StarPattern_UsesSimpleMatching()
    {
        // Win32 matching would let "*.htm" match "page.html" through its 8.3 name.
        File.WriteAllText(HostPath("page.htm"), "x");
        File.WriteAllText(HostPath("page.html"), "x");

        var files = await Root.EnumerateFilesAsync("*.htm");

        Assert.Equal("page.htm", Assert.Single(files).Name);
    }

    [Fact]
    public async Task EnumerateFilesAsync_QuestionMark_MatchesExactlyOneCharacter()
    {
        File.WriteAllText(HostPath("a.txt"), "x");
        File.WriteAllText(HostPath("ab.txt"), "x");
        File.WriteAllText(HostPath(".txt"), "x");

        var files = await Root.EnumerateFilesAsync("?.txt");

        Assert.Equal("a.txt", Assert.Single(files).Name);
    }

    [Fact]
    public async Task EnumerateDirectoriesAsync_Pattern_FiltersByName()
    {
        Directory.CreateDirectory(HostPath("data1"));
        Directory.CreateDirectory(HostPath("data2"));
        Directory.CreateDirectory(HostPath("other"));

        var dirs = (await Root.EnumerateDirectoriesAsync("data*")).Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(new[] { "data1", "data2" }, dirs);
    }

    [Fact]
    public async Task EnumerateFilesAsync_IncludesHiddenFiles()
    {
        CreateHiddenFile(".hidden");

        var files = await Root.EnumerateFilesAsync();

        Assert.Equal(".hidden", Assert.Single(files).Name);
    }

    [Fact]
    public async Task EnumerateFilesAsync_ReturnsUsableEntries()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await CreateFileAsync(dir, "a.txt", "hello");

        var file = Assert.Single(await dir.EnumerateFilesAsync("*.txt"));

        Assert.Equal("/d/a.txt", file.Path);
        Assert.Equal("hello", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task Enumeration_NullPattern_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.EnumerateFilesAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.EnumerateDirectoriesAsync(null!));
    }

    [Theory]
    [InlineData("d/*")]
    [InlineData("d\\*")]
    [InlineData("../*")]
    public async Task Enumeration_PatternWithSeparator_ThrowsArgumentException(string pattern)
    {
        Directory.CreateDirectory(HostPath("d"));

        await Assert.ThrowsAsync<ArgumentException>(() => Root.EnumerateFilesAsync(pattern));
        await Assert.ThrowsAsync<ArgumentException>(() => Root.EnumerateDirectoriesAsync(pattern));
    }

    [Fact]
    public async Task Enumeration_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        Directory.Delete(HostPath("d"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.EnumerateFilesAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.EnumerateDirectoriesAsync());
    }

    // Creation

    [Fact]
    public async Task CreateDirectoryAsync_CreatesAnEmptyDirectory()
    {
        var dir = await Root.CreateDirectoryAsync("d");

        Assert.Equal("/d", dir.Path);
        Assert.Equal("d", dir.Name);
        Assert.True(Directory.Exists(HostPath("d")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(HostPath("d")));
    }

    [Fact]
    public async Task CreateFileAsync_CreatesAnEmptyFile()
    {
        var file = await Root.CreateFileAsync("a.txt");

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal(0, new FileInfo(HostPath("a.txt")).Length);
    }

    [Fact]
    public async Task CreateFileAsync_WithContent_CopiesFromCurrentPositionToEnd()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello world"));
        content.Position = 6;

        var file = await Root.CreateFileAsync("a.txt", content);

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("world", File.ReadAllText(HostPath("a.txt")));
    }

    [Fact]
    public async Task CreateFileAsync_WithContent_HandlesLargeContent()
    {
        var data = new byte[1024 * 1024 + 17];
        new Random(42).NextBytes(data);

        var file = await Root.CreateFileAsync("big.bin", new MemoryStream(data));

        Assert.Equal(data, File.ReadAllBytes(HostPath("big.bin")));
        Assert.Equal(data.Length, await file.GetLengthAsync());
    }

    [Fact]
    public async Task CreateMethods_NameTakenByFile_ThrowInvalidOperationExceptionAndLeaveFileAlone()
    {
        File.WriteAllText(HostPath("a.txt"), "original");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.CreateFileAsync("a.txt"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateFileAsync(Root, "a.txt", "new"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.CreateDirectoryAsync("a.txt"));

        Assert.Equal("original", File.ReadAllText(HostPath("a.txt")));
    }

    [Fact]
    public async Task CreateMethods_NameTakenByDirectory_ThrowInvalidOperationException()
    {
        Directory.CreateDirectory(HostPath("d"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.CreateFileAsync("d"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateFileAsync(Root, "d", "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.CreateDirectoryAsync("d"));
    }

    [Fact]
    public async Task CreateMethods_NullName_ThrowArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.CreateFileAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.CreateFileAsync(null!, new MemoryStream()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.CreateDirectoryAsync(null!));
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task CreateMethods_InvalidName_ThrowArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Root.CreateFileAsync(name));
        await Assert.ThrowsAsync<ArgumentException>(() => Root.CreateFileAsync(name, new MemoryStream()));
        await Assert.ThrowsAsync<ArgumentException>(() => Root.CreateDirectoryAsync(name));

        Assert.Empty(Directory.EnumerateFileSystemEntries(RootPath));
    }

    [Fact]
    public async Task CreateFileAsync_NullContent_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.CreateFileAsync("a.txt", null!));
        Assert.False(File.Exists(HostPath("a.txt")));
    }

    [Fact]
    public async Task CreateFileAsync_UnreadableContent_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Root.CreateFileAsync("a.txt", new UnreadableStream()));
        Assert.False(File.Exists(HostPath("a.txt")));
    }

    [Fact]
    public async Task CreateFileAsync_ContentReadFails_DeletesThePartFileAndRethrows()
    {
        await Assert.ThrowsAsync<IOException>(() => Root.CreateFileAsync("a.txt", new FailingStream()));
        Assert.False(File.Exists(HostPath("a.txt")));
    }

    [Fact]
    public async Task CreateFileAsync_WithContent_CancelledToken_CreatesNothing()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Root.CreateFileAsync("a.txt", new MemoryStream(new byte[10]), new CancellationToken(canceled: true)));
        Assert.False(File.Exists(HostPath("a.txt")));
    }

    [Fact]
    public async Task CreateMethods_MissingDirectory_ThrowDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        Directory.Delete(HostPath("d"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.CreateFileAsync("a.txt"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => CreateFileAsync(dir, "a.txt", "x"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.CreateDirectoryAsync("e"));
    }

    // Deletion

    [Fact]
    public async Task DeleteAsync_EmptyDirectory_DeletesIt()
    {
        var dir = await Root.CreateDirectoryAsync("d");

        await dir.DeleteAsync();

        Assert.False(Directory.Exists(HostPath("d")));
    }

    [Fact]
    public async Task DeleteAsync_NonEmptyWithoutRecursive_ThrowsInvalidOperationException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await dir.CreateFileAsync("a.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dir.DeleteAsync());

        Assert.True(File.Exists(HostPath("d", "a.txt")));
    }

    [Fact]
    public async Task DeleteAsync_ContainsOnlyAnEmptySubdirectory_IsNotEmpty()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await dir.CreateDirectoryAsync("e");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dir.DeleteAsync(recursive: false));
    }

    [Fact]
    public async Task DeleteAsync_Recursive_DeletesEverything()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var sub = await dir.CreateDirectoryAsync("e");
        await CreateFileAsync(sub, "a.txt", "x");

        await dir.DeleteAsync(recursive: true);

        Assert.False(Directory.Exists(HostPath("d")));
    }

    [Fact]
    public async Task DeleteAsync_Root_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.DeleteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.DeleteAsync(recursive: true));
        Assert.True(Directory.Exists(RootPath));
    }

    [Fact]
    public async Task DeleteAsync_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        Directory.Delete(HostPath("d"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.DeleteAsync());
    }

    /// <summary>
    /// Creates a file on disk and marks it hidden.
    /// </summary>
    /// <param name="name">The file's name, in the root directory.</param>
    private void CreateHiddenFile(string name)
    {
        // A leading dot hides a file on Unix; Windows uses the Hidden attribute.
        var path = HostPath(name);
        File.WriteAllText(path, "x");
        if (OperatingSystem.IsWindows())
            File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
    }
}
