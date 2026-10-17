using System.Text;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

public class DirectoryTests : ZipTestBase
{
    /// <summary>
    /// Names that every implementation must reject.
    /// </summary>
    public static TheoryData<string> InvalidNames => EntryTests.InvalidNames;

    // Listing

    [Fact]
    public async Task GetEntriesAsync_EmptyZip_ReturnsNothing()
    {
        Assert.Empty(await Root.GetEntriesAsync());
    }

    [Fact]
    public async Task GetEntriesAsync_ReturnsDirectChildrenWithTheRightTypes()
    {
        await UseZipAsync(("a.txt", "x"), ("d/", null), ("d/nested.txt", "y"), ("e/f/g.txt", "z"));

        var entries = (await Root.GetEntriesAsync()).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "/a.txt", "/d", "/e" }, entries.Select(e => e.Path));
        Assert.IsAssignableFrom<IFile>(entries[0]);
        Assert.IsAssignableFrom<IDirectory>(entries[1]);
        Assert.IsAssignableFrom<IDirectory>(entries[2]);
    }

    [Fact]
    public async Task GetEntriesAsync_ImplicitDirectories_AreListedOnce()
    {
        await UseZipAsync(("e/f/g.txt", "z"), ("e/f/h.txt", "z"), ("e/i.txt", "z"));

        var e = await Root.GetDirectoryAsync("e");

        Assert.Equal("/e", Assert.Single(await Root.GetEntriesAsync()).Path);
        Assert.Equal(new[] { "/e/f", "/e/i.txt" }, (await e.GetEntriesAsync()).Select(x => x.Path).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetEntriesAsync_DuplicateEntries_AreListedOnce()
    {
        await UseZipAsync(("a.txt", "first"), ("a.txt", "second"));

        var file = Assert.Single(await Root.GetEntriesAsync());

        Assert.Equal("first", await ((IFile)file).ReadAllTextAsync());
    }

    [Fact]
    public async Task GetEntriesAsync_IgnoresEntriesThatArentValidPaths()
    {
        await UseZipAsync(("ok.txt", "x"), ("/absolute.txt", "x"), ("../escape.txt", "x"), ("./dot.txt", "x"), ("back\\slash.txt", "x"));

        Assert.Equal("/ok.txt", Assert.Single(await Root.GetEntriesAsync()).Path);
    }

    [Fact]
    public async Task GetEntriesAsync_FileAndDirectoryWithTheSameName_ListsBoth()
    {
        await UseZipAsync(("a", "file"), ("a/b.txt", "inside"));

        var entries = await Root.GetEntriesAsync();

        Assert.Equal(2, entries.Count());
        Assert.Equal("file", await (await Root.GetFileAsync("a")).ReadAllTextAsync());
        Assert.Equal("/a/b.txt", Assert.Single(await (await Root.GetDirectoryAsync("a")).GetEntriesAsync()).Path);
    }

    [Fact]
    public async Task Names_MayContainCharactersWindowsDisallows()
    {
        var file = await CreateFileAsync(Root, "a:b?.txt", "x");

        Assert.Equal("/a:b?.txt", file.Path);
        Assert.Equal("x", (await ReadZipAsync())["a:b?.txt"]);
    }

    [Fact]
    public async Task GetEntriesAsync_IncludesHiddenEntries()
    {
        var file = await Root.CreateFileAsync(".hidden");
        await file.SetAttributesAsync(FileAttributes.Hidden | FileAttributes.System);

        Assert.Equal(".hidden", Assert.Single(await Root.GetEntriesAsync()).Name);
    }

    [Fact]
    public async Task GetEntriesAsync_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await (await Root.GetDirectoryAsync("d")).DeleteAsync();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetEntriesAsync());
    }

    // Lookup

    [Fact]
    public async Task GetFileAsync_ExistingFile_ReturnsIt()
    {
        await UseZipAsync(("d/a.txt", "hello"));

        var file = await (await Root.GetDirectoryAsync("d")).GetFileAsync("a.txt");

        Assert.Equal("/d/a.txt", file.Path);
        Assert.Equal("hello", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task GetDirectoryAsync_FindsExplicitAndImplicitDirectories()
    {
        await UseZipAsync(("explicit/", null), ("implicit/a.txt", "x"));

        Assert.Equal("/explicit", (await Root.GetDirectoryAsync("explicit")).Path);
        Assert.Equal("/implicit", (await Root.GetDirectoryAsync("implicit")).Path);
    }

    [Fact]
    public async Task Lookups_WrongTypeOrMissing_ThrowNotFound()
    {
        await UseZipAsync(("a.txt", "x"), ("d/", null));

        await Assert.ThrowsAsync<FileNotFoundException>(() => Root.GetFileAsync("missing"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Root.GetFileAsync("d"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Root.GetDirectoryAsync("missing"));
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
        await (await Root.GetDirectoryAsync("d")).DeleteAsync(recursive: true);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetFileAsync("a.txt"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetDirectoryAsync("x"));
    }

    // Enumeration

    [Fact]
    public async Task EnumerateFilesAsync_DefaultPattern_ReturnsOnlyFiles()
    {
        await UseZipAsync(("a.txt", "x"), ("b.dat", "x"), ("d/c.txt", "x"));

        var paths = (await Root.EnumerateFilesAsync()).Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal);

        Assert.Equal(new[] { "/a.txt", "/b.dat" }, paths);
    }

    [Fact]
    public async Task EnumerateDirectoriesAsync_DefaultPattern_ReturnsOnlyDirectories()
    {
        await UseZipAsync(("a.txt", "x"), ("d1/", null), ("d2/x.txt", "x"));

        var paths = (await Root.EnumerateDirectoriesAsync()).Select(d => d.Path).OrderBy(p => p, StringComparer.Ordinal);

        Assert.Equal(new[] { "/d1", "/d2" }, paths);
    }

    [Fact]
    public async Task EnumerateFilesAsync_StarPattern_MatchesSuffix()
    {
        await UseZipAsync(("page.htm", "x"), ("page.html", "x"));

        Assert.Equal("page.htm", Assert.Single(await Root.EnumerateFilesAsync("*.htm")).Name);
    }

    [Fact]
    public async Task EnumerateFilesAsync_QuestionMark_MatchesExactlyOneCharacter()
    {
        await UseZipAsync(("a.txt", "x"), ("ab.txt", "x"), (".txt", "x"));

        Assert.Equal("a.txt", Assert.Single(await Root.EnumerateFilesAsync("?.txt")).Name);
    }

    [Fact]
    public async Task EnumerateFilesAsync_IsCaseSensitive()
    {
        await UseZipAsync(("a.txt", "x"), ("b.TXT", "x"));

        Assert.Equal("a.txt", Assert.Single(await Root.EnumerateFilesAsync("*.txt")).Name);
    }

    [Fact]
    public async Task EnumerateDirectoriesAsync_Pattern_FiltersByName()
    {
        await UseZipAsync(("data1/", null), ("data2/x", "x"), ("other/", null));

        var names = (await Root.EnumerateDirectoriesAsync("data*")).Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(new[] { "data1", "data2" }, names);
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
    public async Task Enumeration_PatternWithSeparator_ThrowsArgumentException(string pattern)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Root.EnumerateFilesAsync(pattern));
        await Assert.ThrowsAsync<ArgumentException>(() => Root.EnumerateDirectoriesAsync(pattern));
    }

    [Fact]
    public async Task Enumeration_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await (await Root.GetDirectoryAsync("d")).DeleteAsync();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.EnumerateFilesAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.EnumerateDirectoriesAsync());
    }

    // Creation

    [Fact]
    public async Task CreateDirectoryAsync_AddsADirectoryEntry()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var sub = await dir.CreateDirectoryAsync("e");

        Assert.Equal("/d/e", sub.Path);
        Assert.Empty(await sub.GetEntriesAsync());
        Assert.Equal(new[] { "d/", "d/e/" }, (await ReadZipAsync()).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task CreateFileAsync_AddsAnEmptyFileEntry()
    {
        var file = await Root.CreateFileAsync("a.txt");

        Assert.Equal(0, await file.GetLengthAsync());
        Assert.Equal("", (await ReadZipAsync())["a.txt"]);
    }

    [Fact]
    public async Task CreateFileAsync_InImplicitDirectory_Works()
    {
        await UseZipAsync(("d/a.txt", "a"));

        await CreateFileAsync(await Root.GetDirectoryAsync("d"), "b.txt", "b");

        Assert.Equal("b", (await ReadZipAsync())["d/b.txt"]);
    }

    [Fact]
    public async Task CreateFileAsync_WithContent_CopiesFromCurrentPositionToEnd()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello world"));
        content.Position = 6;

        var file = await Root.CreateFileAsync("a.txt", content);

        Assert.Equal("world", await file.ReadAllTextAsync());
        Assert.Equal("world", (await ReadZipAsync())["a.txt"]);
    }

    [Fact]
    public async Task CreateFileAsync_WithContent_HandlesLargeContent()
    {
        var data = new byte[1024 * 1024 + 17];
        new Random(42).NextBytes(data);

        await Root.CreateFileAsync("big.bin", new MemoryStream(data));
        await ReopenAsync();

        var file = await Root.GetFileAsync("big.bin");
        Assert.Equal(data, await file.ReadAllBytesAsync());
        Assert.Equal(data.Length, await file.GetLengthAsync());
    }

    [Fact]
    public async Task CreateMethods_NameTaken_ThrowInvalidOperationExceptionAndLeaveEntryAlone()
    {
        await CreateFileAsync(Root, "a.txt", "original");
        await Root.CreateDirectoryAsync("d");

        foreach (var name in new[] { "a.txt", "d" })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Root.CreateFileAsync(name));
            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateFileAsync(Root, name, "new"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Root.CreateDirectoryAsync(name));
        }

        var entries = await ReadZipAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("original", entries["a.txt"]);
    }

    [Fact]
    public async Task CreateMethods_NameTakenByImplicitDirectory_ThrowInvalidOperationException()
    {
        await UseZipAsync(("d/a.txt", "a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.CreateFileAsync("d"));
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

        Assert.Empty(await ReadZipAsync());
    }

    [Fact]
    public async Task CreateFileAsync_BadContent_ThrowsAndAddsNothing()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Root.CreateFileAsync("a.txt", null!));
        await Assert.ThrowsAsync<ArgumentException>(() => Root.CreateFileAsync("b.txt", new UnreadableStream()));
        await Assert.ThrowsAsync<IOException>(() => Root.CreateFileAsync("c.txt", new FailingStream()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Root.CreateFileAsync("d.txt", new MemoryStream(new byte[10]), new CancellationToken(canceled: true)));

        Assert.Empty(await Root.GetEntriesAsync());
        Assert.Empty(await ReadZipAsync());
    }

    [Fact]
    public async Task CreateMethods_MissingDirectory_ThrowDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await (await Root.GetDirectoryAsync("d")).DeleteAsync();

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

        Assert.Empty(await Root.GetEntriesAsync());
        Assert.Empty(await ReadZipAsync());
    }

    [Fact]
    public async Task DeleteAsync_NonEmptyWithoutRecursive_ThrowsInvalidOperationException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await dir.CreateDirectoryAsync("e");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dir.DeleteAsync());

        Assert.Equal(new[] { "d/", "d/e/" }, (await ReadZipAsync()).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task DeleteAsync_Recursive_DeletesEverythingInside()
    {
        await UseZipAsync(("keep.txt", "k"), ("d/", null), ("d/a.txt", "a"), ("d/e/b.txt", "b"), ("dd/c.txt", "c"));

        await (await Root.GetDirectoryAsync("d")).DeleteAsync(recursive: true);

        Assert.Equal(new[] { "dd/c.txt", "keep.txt" }, (await ReadZipAsync()).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task DeleteAsync_LastChild_KeepsTheParentDirectory()
    {
        await UseZipAsync(("p/d/", null));

        await (await (await Root.GetDirectoryAsync("p")).GetDirectoryAsync("d")).DeleteAsync();

        Assert.Empty(await (await Root.GetDirectoryAsync("p")).GetEntriesAsync());
        Assert.Equal(new[] { "p/" }, (await ReadZipAsync()).Keys);
    }

    [Fact]
    public async Task DeleteAsync_Root_ThrowsInvalidOperationException()
    {
        await Root.CreateFileAsync("a.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.DeleteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.DeleteAsync(recursive: true));

        Assert.Single(await Root.GetEntriesAsync());
    }

    [Fact]
    public async Task DeleteAsync_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await (await Root.GetDirectoryAsync("d")).DeleteAsync();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.DeleteAsync());
    }
}
