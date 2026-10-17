using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Tests for listings, lookups and search patterns, and for how the options filter and order them.
/// </summary>
public class DirectoryTests : AndroidSafTestBase
{
    [Fact]
    public async Task GetEntriesAsync_ListsDirectoriesBeforeFiles()
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFolder(RootId, "z");
        Source.AddFile(RootId, "b.txt");
        Source.AddFolder(RootId, "y");
        var root = await OpenRootAsync();

        var entries = (await root.GetEntriesAsync()).ToList();

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
    [InlineData(SortOrder.Ascending, new[] { "a", "B", "c", "a.png", "B.png", "c.png" })]
    [InlineData(SortOrder.Descending, new[] { "c", "B", "a", "c.png", "B.png", "a.png" })]
    public async Task GetEntriesAsync_SortsByNameIgnoringCase(SortOrder sortOrder, string[] expected)
    {
        foreach (var name in new[] { "c", "a", "B" })
        {
            Source.AddFolder(RootId, name);
            Source.AddFile(RootId, name + ".png");
        }
        var root = await OpenRootAsync(new FileSystemOptions { SortField = SortField.FileName, SortOrder = sortOrder });

        Assert.Equal(expected, Names(await root.GetEntriesAsync()));
    }

    [Theory]
    [InlineData(SortOrder.Ascending, new[] { "old", "new", "old.png", "new.png" })]
    [InlineData(SortOrder.Descending, new[] { "new", "old", "new.png", "old.png" })]
    public async Task GetEntriesAsync_SortsByDate(SortOrder sortOrder, string[] expected)
    {
        Source.AddFolder(RootId, "new", lastModified: 1_690_000_000_000);
        Source.AddFolder(RootId, "old", lastModified: 1_620_000_000_000);
        Source.AddFile(RootId, "new.png", lastModified: 1_690_000_000_000);
        Source.AddFile(RootId, "old.png", lastModified: 1_620_000_000_000);
        var root = await OpenRootAsync(new FileSystemOptions { SortField = SortField.FileDate, SortOrder = sortOrder });

        Assert.Equal(expected, Names(await root.GetEntriesAsync()));
    }

    [Fact]
    public async Task GetEntriesAsync_ExcludesHiddenEntriesByDefault()
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFile(RootId, ".a.txt");
        Source.AddFolder(RootId, "d");
        Source.AddFolder(RootId, ".d");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { "d", "a.txt" }, Names(await root.GetEntriesAsync()));
    }

    [Theory]
    [InlineData(true, false, new[] { "d", ".a.txt", "a.txt" })]
    [InlineData(false, true, new[] { ".d", "d", "a.txt" })]
    [InlineData(true, true, new[] { ".d", "d", ".a.txt", "a.txt" })]
    public async Task GetEntriesAsync_IncludesHiddenEntriesWhenAsked(bool files, bool folders, string[] expected)
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFile(RootId, ".a.txt");
        Source.AddFolder(RootId, "d");
        Source.AddFolder(RootId, ".d");
        var root = await OpenRootAsync(new FileSystemOptions { IncludeHiddenFiles = files, IncludeHiddenFolders = folders });

        Assert.Equal(expected, Names(await root.GetEntriesAsync()));
    }

    [Fact]
    public async Task GetEntriesAsync_FileTypes_FilterFilesButNotDirectories()
    {
        Source.AddFile(RootId, "a.jpeg");
        Source.AddFile(RootId, "b.JPG");
        Source.AddFile(RootId, "c.png");
        Source.AddFile(RootId, "README");
        Source.AddFolder(RootId, "photos");
        var root = await OpenRootAsync(new FileSystemOptions { FileTypes = new[] { "jpeg" } });

        Assert.Equal(new[] { "photos", "a.jpeg", "b.JPG" }, Names(await root.GetEntriesAsync()));
    }

    [Fact]
    public async Task GetEntriesAsync_FileTypesInUpperCase_StillMatchJpg()
    {
        Source.AddFile(RootId, "a.jpg");
        var root = await OpenRootAsync(new FileSystemOptions { FileTypes = new[] { "JPEG" } });

        Assert.Equal(new[] { "a.jpg" }, Names(await root.GetEntriesAsync()));
    }

    // Regression: no file types used to select no files at all.
    [Fact]
    public async Task GetEntriesAsync_NoFileTypes_IncludesEveryFileIncludingNamesWithoutAnExtension()
    {
        Source.AddFile(RootId, "a.png");
        Source.AddFile(RootId, "README");
        Source.AddFile(RootId, "b.");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { "a.png", "b.", "README" }, Names(await root.GetEntriesAsync()));
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
        var root = await OpenRootAsync(ShowHidden());

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

        var entries = (await root.GetEntriesAsync()).ToList();

        Assert.Equal(new[] { "D", "A.txt" }, Names(entries));
        Assert.Equal("first", await ((IFile)entries[1]).ReadAllTextAsync());
    }

    [Fact]
    public async Task GetEntriesAsync_FileAndDirectoryWithTheSameName_BothAppear()
    {
        Source.AddFile(RootId, "a");
        Source.AddFolder(RootId, "a");
        var root = await OpenRootAsync();

        var entries = (await root.GetEntriesAsync()).ToList();

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

    // The folder can disappear between the existence check and the listing.
    [Fact]
    public async Task GetEntriesAsync_ListingReportsTheFolderMissing_ThrowsDirectoryNotFoundExceptionForThePath()
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => root.GetEntriesAsync(cts.Token));
    }

    [Fact]
    public async Task GetEntriesAsync_ListsAgainEachTime()
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
    public async Task GetFileAsync_HiddenFile_IsFoundOnlyWhenIncluded()
    {
        Source.AddFile(RootId, ".a.txt");

        var hiding = await OpenRootAsync();
        var showing = await OpenRootAsync(new FileSystemOptions { IncludeHiddenFiles = true });

        await Assert.ThrowsAsync<FileNotFoundException>(() => hiding.GetFileAsync(".a.txt"));
        Assert.Equal("/.a.txt", (await showing.GetFileAsync(".a.txt")).Path);
    }

    [Fact]
    public async Task GetFileAsync_FileOfAnExcludedType_ThrowsFileNotFoundException()
    {
        Source.AddFile(RootId, "a.gif");
        var root = await OpenRootAsync(new FileSystemOptions { FileTypes = new[] { "png" } });

        await Assert.ThrowsAsync<FileNotFoundException>(() => root.GetFileAsync("a.gif"));
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

    [Fact]
    public async Task GetDirectoryAsync_HiddenDirectory_IsFoundOnlyWhenIncluded()
    {
        Source.AddFolder(RootId, ".d");

        var hiding = await OpenRootAsync();
        var showing = await OpenRootAsync(new FileSystemOptions { IncludeHiddenFolders = true });

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => hiding.GetDirectoryAsync(".d"));
        Assert.Equal("/.d", (await showing.GetDirectoryAsync(".d")).Path);
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

        Assert.Equal(expected, Names(await root.EnumerateFilesAsync(pattern)));
    }

    [Fact]
    public async Task EnumerateFilesAsync_DefaultPattern_ListsEveryIncludedFile()
    {
        Source.AddFile(RootId, "a.txt");
        Source.AddFile(RootId, ".b.txt");
        Source.AddFolder(RootId, "d");
        var root = await OpenRootAsync();

        Assert.Equal(new[] { "a.txt" }, Names(await root.EnumerateFilesAsync()));
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

        Assert.Equal(expected, Names(await root.EnumerateDirectoriesAsync(pattern)));
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
