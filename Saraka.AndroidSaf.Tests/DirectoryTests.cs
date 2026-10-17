using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Tests for a directory's contents, lookups and search patterns, and for how
/// <see cref="EntryOptions"/> filter and sort the contents.
/// </summary>
public class DirectoryTests : AndroidSafTestBase
{
    [Fact]
    public async Task GetEntriesAsync_NoOptions_ReturnsEveryEntryInTheProvidersOrder()
    {
        Source.AddFile(RootId, "b.txt");
        Source.AddFolder(RootId, "z");
        Source.AddFile(RootId, ".hidden");
        Source.AddFolder(RootId, "a");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { "b.txt", "z", ".hidden", "a" }, Names(await root.GetEntriesAsync()));
    }

    [Fact]
    public async Task GetEntriesAsync_Sorted_ListsDirectoriesBeforeFiles()
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFolder(RootId, "z");
        Source.AddFile(RootId, "b.txt");
        Source.AddFolder(RootId, "y");
        var root = await OpenRootAsync();

        var entries = (await root.GetEntriesAsync(ByName())).ToList();

        Assert.Equal(new[] { "y", "z", "a.txt", "b.txt" }, Names(entries));
        Assert.All(entries.Take(2), e => Assert.IsAssignableFrom<IDirectory>(e));
        Assert.All(entries.Skip(2), e => Assert.IsAssignableFrom<IFile>(e));
    }

    [Fact]
    public async Task GetEntriesAsync_EntriesHavePathsAndParent()
    {
        var photos = Source.AddFolder(RootId, "Photos");
        var year = Source.AddFolder(photos, "2026");
        Source.AddFile(year, "img1.jpg");
        var root = await OpenRootAsync();

        var photosDir = await root.GetDirectoryAsync("Photos");
        var yearDir = (IDirectory)(await photosDir.GetEntriesAsync()).Single();
        var file = (await yearDir.GetEntriesAsync()).Single();

        Assert.Equal("/Photos/2026", yearDir.Path);
        Assert.Equal("/Photos/2026/img1.jpg", file.Path);
        Assert.Equal("img1.jpg", file.Name);
        Assert.Equal("/Photos/2026", file.Parent!.Path);
        Assert.Equal("/Photos", file.Parent!.Parent!.Path);
        Assert.Equal("/", file.Parent!.Parent!.Parent!.Path);
    }

    [Fact]
    public async Task GetEntriesAsync_EmptyDirectory_ReturnsNothing()
    {
        var root = await OpenRootAsync();

        Assert.Empty(await root.GetEntriesAsync());
    }

    [Theory]
    [InlineData(EntrySortDirection.Ascending, new[] { "a", "B", "c", "a.png", "B.png", "c.png" })]
    [InlineData(EntrySortDirection.Descending, new[] { "c", "B", "a", "c.png", "B.png", "a.png" })]
    public async Task GetEntriesAsync_SortsByNameIgnoringCase(EntrySortDirection direction, string[] expected)
    {
        foreach (var name in new[] { "c", "a", "B" })
        {
            Source.AddFolder(RootId, name);
            Source.AddFile(RootId, name + ".png");
        }
        var root = await OpenRootAsync();
        var options = new EntryOptions { SortField = EntrySortField.Name, SortDirection = direction };

        Assert.Equal(expected, Names(await root.GetEntriesAsync(options)));
    }

    [Theory]
    [InlineData(EntrySortDirection.Ascending, new[] { "old", "new", "old.png", "same1.png", "same2.png", "new.png" })]
    [InlineData(EntrySortDirection.Descending, new[] { "new", "old", "new.png", "same2.png", "same1.png", "old.png" })]
    public async Task GetEntriesAsync_SortsByLastWriteTimeThenName(EntrySortDirection direction, string[] expected)
    {
        Source.AddFolder(RootId, "new", lastModified: 1_690_000_000_000);
        Source.AddFolder(RootId, "old", lastModified: 1_620_000_000_000);
        Source.AddFile(RootId, "new.png", lastModified: 1_690_000_000_000);
        Source.AddFile(RootId, "same2.png", lastModified: 1_650_000_000_000);
        Source.AddFile(RootId, "old.png", lastModified: 1_620_000_000_000);
        Source.AddFile(RootId, "same1.png", lastModified: 1_650_000_000_000);
        var root = await OpenRootAsync();
        var options = new EntryOptions { SortField = EntrySortField.LastWriteTime, SortDirection = direction };

        Assert.Equal(expected, Names(await root.GetEntriesAsync(options)));
    }

    [Fact]
    public async Task GetEntriesAsync_IncludesHiddenEntriesByDefault()
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFile(RootId, ".a.txt");
        Source.AddFolder(RootId, "d");
        Source.AddFolder(RootId, ".d");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { ".d", "d", ".a.txt", "a.txt" }, Names(await root.GetEntriesAsync(ByName())));
    }

    [Theory]
    [InlineData(true, false, new[] { "d", ".a.txt", "a.txt" })]
    [InlineData(false, true, new[] { ".d", "d", "a.txt" })]
    [InlineData(false, false, new[] { "d", "a.txt" })]
    public async Task GetEntriesAsync_HiddenEntries_AreNamesStartingWithAPeriod(bool files, bool directories, string[] expected)
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFile(RootId, ".a.txt");
        Source.AddFolder(RootId, "d");
        Source.AddFolder(RootId, ".d");
        var root = await OpenRootAsync();
        var options = ByName();
        options.IncludeHiddenFiles = files;
        options.IncludeHiddenDirectories = directories;

        Assert.Equal(expected, Names(await root.GetEntriesAsync(options)));
    }

    [Fact]
    public async Task GetEntriesAsync_FileTypes_FilterFilesButNotDirectories()
    {
        Source.AddFile(RootId, "a.jpeg");
        Source.AddFile(RootId, "b.JPG");
        Source.AddFile(RootId, "c.png");
        Source.AddFile(RootId, "README");
        Source.AddFolder(RootId, "photos");
        var root = await OpenRootAsync();
        var options = ByName();
        options.FileTypes = new[] { "jpeg", "jpg" };

        Assert.Equal(new[] { "photos", "a.jpeg", "b.JPG" }, Names(await root.GetEntriesAsync(options)));
    }

    [Fact]
    public async Task GetEntriesAsync_FileTypes_AddNoSynonyms()
    {
        Source.AddFile(RootId, "a.jpg");
        Source.AddFile(RootId, "b.JPEG");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { "b.JPEG" }, Names(await root.GetEntriesAsync(new EntryOptions { FileTypes = new[] { "jpeg" } })));
    }

    [Fact]
    public async Task GetEntriesAsync_NoFileTypes_IncludesEveryFileIncludingNamesWithoutAnExtension()
    {
        Source.AddFile(RootId, "a.png");
        Source.AddFile(RootId, "README");
        Source.AddFile(RootId, "b.");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { "a.png", "b.", "README" }, Names(await root.GetEntriesAsync(ByName())));
    }

    [Fact]
    public async Task GetEntriesAsync_InvalidOptions_ThrowArgumentException()
    {
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => root.GetEntriesAsync(new EntryOptions { SortField = (EntrySortField)99 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    public async Task GetEntriesAsync_IgnoresInvalidDisplayNames(string name)
    {
        Source.AddFile(RootId, name);
        Source.AddFolder(RootId, name);
        Source.AddFile(RootId, "ok.txt");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { "ok.txt" }, Names(await root.GetEntriesAsync()));
    }

    [Fact]
    public async Task GetEntriesAsync_NamesDifferingOnlyInCase_KeepsTheFirstOfEachKind()
    {
        Source.AddFile(RootId, "A.txt", "first");
        Source.AddFile(RootId, "a.txt", "second");
        Source.AddFolder(RootId, "D");
        Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();

        var entries = (await root.GetEntriesAsync(ByName())).ToList();

        Assert.Equal(new[] { "D", "A.txt" }, Names(entries));
        Assert.Equal("first", await ((IFile)entries[1]).ReadAllTextAsync());
    }

    // The first in the provider's order is kept, whatever the sort order, so that every call
    // and every lookup agree.
    [Theory]
    [InlineData(EntrySortDirection.Ascending)]
    [InlineData(EntrySortDirection.Descending)]
    public async Task GetEntriesAsync_NamesDifferingOnlyInCase_KeepTheSameOneWhateverTheSort(EntrySortDirection direction)
    {
        Source.AddFile(RootId, "b.txt", "first");
        Source.AddFile(RootId, "B.txt", "second");
        var root = await OpenRootAsync();
        var options = new EntryOptions { SortField = EntrySortField.Name, SortDirection = direction };

        Assert.Equal(new[] { "b.txt" }, Names(await root.GetEntriesAsync(options)));
        Assert.Equal("b.txt", (await root.GetFileAsync("B.TXT")).Name);
    }

    [Fact]
    public async Task GetEntriesAsync_FileAndDirectoryWithTheSameName_BothAppear()
    {
        Source.AddFile(RootId, "a");
        Source.AddFolder(RootId, "a");
        var root = await OpenRootAsync();

        var entries = (await root.GetEntriesAsync(ByName())).ToList();

        Assert.Equal(2, entries.Count);
        Assert.IsAssignableFrom<IDirectory>(entries[0]);
        Assert.IsAssignableFrom<IFile>(entries[1]);
    }

    [Fact]
    public async Task GetEntriesAsync_DirectoryRemoved_ThrowsDirectoryNotFoundException()
    {
        var id = Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();
        var dir = await root.GetDirectoryAsync("d");

        Source.Remove(id);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetEntriesAsync());
    }

    [Fact]
    public async Task GetEntriesAsync_DirectoryBecameAFile_ThrowsDirectoryNotFoundException()
    {
        var id = Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();
        var dir = await root.GetDirectoryAsync("d");

        Source.SetIsDirectory(id, false);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetEntriesAsync());
    }

    // The folder can disappear between the existence check and reading its contents.
    [Fact]
    public async Task GetEntriesAsync_ProviderReportsTheFolderMissing_ThrowsDirectoryNotFoundExceptionForThePath()
    {
        Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();
        var dir = await root.GetDirectoryAsync("d");
        var missing = new FileNotFoundException("gone");
        Source.Fail = (method, id) => method == nameof(IDocumentSource.ListChildren) ? missing : null;

        var ex = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetEntriesAsync());

        Assert.Contains("/d", ex.Message);
        Assert.Same(missing, ex.InnerException);
    }

    [Fact]
    public async Task GetEntriesAsync_AccessDenied_ThrowsUnauthorizedAccessException()
    {
        var root = await OpenRootAsync();
        Source.Fail = (method, id) => method == nameof(IDocumentSource.ListChildren) ? new UnauthorizedAccessException() : null;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => root.GetEntriesAsync());
    }

    [Fact]
    public async Task GetEntriesAsync_ProviderFails_ThrowsIOException()
    {
        var root = await OpenRootAsync();
        Source.Fail = (method, id) => method == nameof(IDocumentSource.ListChildren) ? new IOException() : null;

        await Assert.ThrowsAsync<IOException>(() => root.GetEntriesAsync());
    }

    [Fact]
    public async Task GetEntriesAsync_Cancelled_ThrowsOperationCanceledException()
    {
        var root = await OpenRootAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => root.GetEntriesAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task GetEntriesAsync_ReadsTheFolderAgainEachTime()
    {
        var root = await OpenRootAsync();
        Assert.Empty(await root.GetEntriesAsync());

        Source.AddFile(RootId, "a.txt");

        Assert.Equal(new[] { "a.txt" }, Names(await root.GetEntriesAsync()));
    }

    [Fact]
    public async Task GetFileAsync_IgnoresCaseAndUsesTheDisplayName()
    {
        Source.AddFile(RootId, "Photo.JPG", "data");
        var root = await OpenRootAsync();

        var file = await root.GetFileAsync("photo.jpg");

        Assert.Equal("Photo.JPG", file.Name);
        Assert.Equal("/Photo.JPG", file.Path);
        Assert.Equal("data", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task GetFileAsync_NamesDifferingOnlyInCase_FindsTheOneListed()
    {
        Source.AddFile(RootId, "A.txt", "first");
        Source.AddFile(RootId, "a.txt", "second");
        var root = await OpenRootAsync();

        var file = await root.GetFileAsync("a.txt");

        Assert.Equal("A.txt", file.Name);
        Assert.Equal("first", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task GetFileAsync_Missing_ThrowsFileNotFoundException()
    {
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<FileNotFoundException>(() => root.GetFileAsync("a.txt"));
    }

    [Fact]
    public async Task GetFileAsync_NameOfADirectory_ThrowsFileNotFoundException()
    {
        Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<FileNotFoundException>(() => root.GetFileAsync("d"));
    }

    [Fact]
    public async Task Lookups_FindHiddenEntries()
    {
        Source.AddFile(RootId, ".a.txt");
        Source.AddFolder(RootId, ".d");
        var root = await OpenRootAsync();

        Assert.Equal("/.a.txt", (await root.GetFileAsync(".a.txt")).Path);
        Assert.Equal("/.d", (await root.GetDirectoryAsync(".d")).Path);
    }

    [Fact]
    public async Task GetFileAsync_DirectoryRemoved_ThrowsDirectoryNotFoundException()
    {
        var id = Source.AddFolder(RootId, "d");
        Source.AddFile(id, "a.txt");
        var root = await OpenRootAsync();
        var dir = await root.GetDirectoryAsync("d");

        Source.Remove(id);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetFileAsync("a.txt"));
    }

    [Fact]
    public async Task GetDirectoryAsync_IgnoresCaseAndUsesTheDisplayName()
    {
        Source.AddFolder(RootId, "Photos");
        var root = await OpenRootAsync();

        var dir = await root.GetDirectoryAsync("PHOTOS");

        Assert.Equal("Photos", dir.Name);
        Assert.Equal("/Photos", dir.Path);
    }

    [Fact]
    public async Task GetDirectoryAsync_Missing_ThrowsDirectoryNotFoundException()
    {
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => root.GetDirectoryAsync("d"));
    }

    [Fact]
    public async Task GetDirectoryAsync_NameOfAFile_ThrowsDirectoryNotFoundException()
    {
        Source.AddFile(RootId, "a.txt");
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => root.GetDirectoryAsync("a.txt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    public async Task Lookups_InvalidName_ThrowArgumentException(string name)
    {
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => root.GetFileAsync(name));
        await Assert.ThrowsAsync<ArgumentException>(() => root.GetDirectoryAsync(name));
    }

    [Fact]
    public async Task Lookups_NullName_ThrowArgumentNullException()
    {
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => root.GetFileAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => root.GetDirectoryAsync(null!));
    }

    [Fact]
    public async Task Lookups_ExceptionsAreReportedThroughTheTask()
    {
        var root = await OpenRootAsync();
        Task? task = null;

        var thrown = Record.Exception(() => { task = root.GetFileAsync(null!); });

        Assert.Null(thrown);
        await Assert.ThrowsAsync<ArgumentNullException>(() => task!);
    }

    [Theory]
    [InlineData("*", new[] { "a.jpg", "B.JPG", "c.png" })]
    [InlineData("*.jpg", new[] { "a.jpg", "B.JPG" })]
    [InlineData("?.PNG", new[] { "c.png" })]
    [InlineData("b*", new[] { "B.JPG" })]
    [InlineData("x*", new string[0])]
    public async Task EnumerateFilesAsync_MatchesPatternsIgnoringCase(string pattern, string[] expected)
    {
        Source.AddFile(RootId, "c.png");
        Source.AddFile(RootId, "B.JPG");
        Source.AddFile(RootId, "a.jpg");
        Source.AddFolder(RootId, "a.jpg.d");
        var root = await OpenRootAsync();

        Assert.Equal(expected, Names(await root.EnumerateFilesAsync(pattern, ByName())));
    }

    [Fact]
    public async Task EnumerateFilesAsync_DefaultPattern_ListsEveryFile()
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFile(RootId, ".b.txt");
        Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { ".b.txt", "a.txt" }, Names(await root.EnumerateFilesAsync(options: ByName())));
    }

    [Fact]
    public async Task EnumerateFilesAsync_AppliesPatternAndOptionsTogether()
    {
        Source.AddFile(RootId, "a1.jpg", lastModified: 300);
        Source.AddFile(RootId, "a2.jpg", lastModified: 100);
        Source.AddFile(RootId, ".a3.jpg", lastModified: 200);
        Source.AddFile(RootId, "a4.png", lastModified: 50);
        Source.AddFile(RootId, "b1.jpg", lastModified: 10);
        var root = await OpenRootAsync();
        var options = new EntryOptions
        {
            FileTypes = new[] { "jpg" },
            IncludeHiddenFiles = false,
            SortField = EntrySortField.LastWriteTime,
        };

        Assert.Equal(new[] { "a2.jpg", "a1.jpg" }, Names(await root.EnumerateFilesAsync("a*", options)));
    }

    [Theory]
    [InlineData("*", new[] { "2025", "2026", "Archive" })]
    [InlineData("202?", new[] { "2025", "2026" })]
    [InlineData("arch*", new[] { "Archive" })]
    public async Task EnumerateDirectoriesAsync_MatchesPatternsIgnoringCase(string pattern, string[] expected)
    {
        Source.AddFolder(RootId, "2026");
        Source.AddFolder(RootId, "Archive");
        Source.AddFolder(RootId, "2025");
        Source.AddFile(RootId, "2024");
        var root = await OpenRootAsync();

        Assert.Equal(expected, Names(await root.EnumerateDirectoriesAsync(pattern, ByName())));
    }

    [Fact]
    public async Task EnumerateDirectoriesAsync_FileTypesDontApply()
    {
        Source.AddFolder(RootId, "photos");
        Source.AddFolder(RootId, ".cache");
        var root = await OpenRootAsync();
        var options = new EntryOptions { FileTypes = new[] { "jpg" }, IncludeHiddenDirectories = false };

        Assert.Equal(new[] { "photos" }, Names(await root.EnumerateDirectoriesAsync(options: options)));
    }

    [Fact]
    public async Task EnumerateFilesAsync_DuplicateCheckComesBeforeThePattern()
    {
        Source.AddFile(RootId, "A.txt", "first");
        Source.AddFile(RootId, "a.txt", "second");
        var root = await OpenRootAsync();

        var files = (await root.EnumerateFilesAsync("a.txt")).ToList();

        Assert.Equal(new[] { "A.txt" }, Names(files));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task Enumerate_PatternWithASeparator_ThrowsArgumentException(string pattern)
    {
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => root.EnumerateFilesAsync(pattern));
        await Assert.ThrowsAsync<ArgumentException>(() => root.EnumerateDirectoriesAsync(pattern));
    }

    [Fact]
    public async Task Enumerate_NullPattern_ThrowsArgumentNullException()
    {
        var root = await OpenRootAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => root.EnumerateFilesAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => root.EnumerateDirectoriesAsync(null!));
    }

    [Fact]
    public async Task Enumerate_DirectoryRemoved_ThrowsDirectoryNotFoundException()
    {
        var id = Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();
        var dir = await root.GetDirectoryAsync("d");

        Source.Remove(id);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.EnumerateFilesAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.EnumerateDirectoriesAsync());
    }
}
