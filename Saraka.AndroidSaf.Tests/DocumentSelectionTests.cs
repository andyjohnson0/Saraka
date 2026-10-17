namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Tests for the filtering and sorting of a directory listing. Adapted from Galeria's
/// DocumentSelectionTests.
/// </summary>
public class DocumentSelectionTests
{
    private static DocumentEntry File(string name, long lastModified = 0)
    {
        return new DocumentEntry("id:" + name, name, lastModified, IsDirectory: false);
    }

    private static DocumentEntry Folder(string name, long lastModified = 0)
    {
        return new DocumentEntry("id:" + name, name, lastModified, IsDirectory: true);
    }

    private static string[] Names(IEnumerable<DocumentEntry> entries)
    {
        return entries.Select(e => e.DisplayName).ToArray();
    }

    [Fact]
    public void ExpandFileTypes_AddsJpgForJpeg()
    {
        Assert.Equal(new[] { "jpeg", "png", "jpg" }, DocumentSelection.ExpandFileTypes(new[] { "jpeg", "png" }));
    }

    [Fact]
    public void ExpandFileTypes_AddsJpgForJpegInAnyCase()
    {
        Assert.Equal(new[] { "JPEG", "jpg" }, DocumentSelection.ExpandFileTypes(new[] { "JPEG" }));
    }

    // Regression: null used to become an empty list, which selected no files.
    [Fact]
    public void ExpandFileTypes_Null_ReturnsNull()
    {
        Assert.Null(DocumentSelection.ExpandFileTypes(null));
    }

    [Fact]
    public void ExpandFileTypes_CopiesTheTypes()
    {
        var fileTypes = new[] { "png" };

        var expanded = DocumentSelection.ExpandFileTypes(fileTypes);
        fileTypes[0] = "gif";

        Assert.Equal(new[] { "png" }, expanded);
    }

    [Fact]
    public void Select_ExcludesDirectoriesHiddenFilesAndOtherTypes()
    {
        var entries = new[]
        {
            File("a.jpeg"),
            Folder("dir.jpeg"),
            File(".hidden.jpeg"),
            File("b.JPG"),
            File("c.png"),
            File("d.txt"),
        };

        var selected = DocumentSelection.Select(entries, new[] { "jpeg", "jpg", "png" }, SortField.FileName, SortOrder.Ascending);

        Assert.Equal(new[] { "a.jpeg", "b.JPG", "c.png" }, Names(selected));
    }

    [Fact]
    public void Select_IncludeHidden_IncludesHiddenFilesOfTheRightType()
    {
        var entries = new[] { File("a.png"), File(".hidden.png"), File(".hidden.txt") };

        var selected = DocumentSelection.Select(entries, new[] { "png" }, SortField.FileName, SortOrder.Ascending, includeHidden: true);

        Assert.Equal(new[] { ".hidden.png", "a.png" }, Names(selected));
    }

    [Fact]
    public void Select_NullFileTypes_IncludesAllFiles()
    {
        var entries = new[] { File("a.jpeg"), File("b.txt"), File("README") };

        var selected = DocumentSelection.Select(entries, null, SortField.FileName, SortOrder.Ascending);

        Assert.Equal(new[] { "a.jpeg", "b.txt", "README" }, Names(selected));
    }

    [Fact]
    public void Select_FileTypes_ExcludesNamesWithoutAnExtension()
    {
        var entries = new[] { File("a.png"), File("png"), File("README"), File("b.") };

        var selected = DocumentSelection.Select(entries, new[] { "png" }, SortField.FileName, SortOrder.Ascending);

        Assert.Equal(new[] { "a.png" }, Names(selected));
    }

    [Theory]
    [InlineData(SortOrder.Ascending, new[] { "a.png", "B.png", "c.png" })]
    [InlineData(SortOrder.Descending, new[] { "c.png", "B.png", "a.png" })]
    public void Select_SortsByNameIgnoringCase(SortOrder sortOrder, string[] expected)
    {
        var entries = new[] { File("c.png"), File("a.png"), File("B.png") };

        var selected = DocumentSelection.Select(entries, new[] { "png" }, SortField.FileName, sortOrder);

        Assert.Equal(expected, Names(selected));
    }

    [Theory]
    [InlineData(SortOrder.Ascending, new[] { "old.png", "mid.png", "new.png" })]
    [InlineData(SortOrder.Descending, new[] { "new.png", "mid.png", "old.png" })]
    public void Select_SortsByDate(SortOrder sortOrder, string[] expected)
    {
        var entries = new[] { File("mid.png", 200), File("new.png", 300), File("old.png", 100) };

        var selected = DocumentSelection.Select(entries, new[] { "png" }, SortField.FileDate, sortOrder);

        Assert.Equal(expected, Names(selected));
    }

    [Fact]
    public void Select_SortsByDate_WithMillisecondTimestamps()
    {
        // COLUMN_LAST_MODIFIED is milliseconds since the epoch, which doesn't fit in an int.
        var entries = new[]
        {
            File("2023.png", 1_690_000_000_000),
            File("2021.png", 1_620_000_000_000),
            File("2022.png", 1_650_000_000_000),
        };

        var selected = DocumentSelection.Select(entries, new[] { "png" }, SortField.FileDate, SortOrder.Ascending);

        Assert.Equal(new[] { "2021.png", "2022.png", "2023.png" }, Names(selected));
    }

    [Fact]
    public void Select_EqualNames_KeepProviderOrder()
    {
        var entries = new[] { new DocumentEntry("id1", "A.png", 0, false), new DocumentEntry("id2", "a.png", 0, false) };

        var selected = DocumentSelection.Select(entries, null, SortField.FileName, SortOrder.Ascending);

        Assert.Equal(new[] { "id1", "id2" }, selected.Select(e => e.DocumentId));
    }

    [Fact]
    public void SelectFolders_ExcludesFilesAndHiddenFolders()
    {
        var entries = new[]
        {
            Folder("b"),
            File("a.jpeg"),
            Folder(".hidden"),
            Folder("a.jpeg.folder"),
        };

        var selected = DocumentSelection.SelectFolders(entries, SortField.FileName, SortOrder.Ascending);

        Assert.Equal(new[] { "a.jpeg.folder", "b" }, Names(selected));
    }

    [Fact]
    public void SelectFolders_IncludeHidden_IncludesHiddenFolders()
    {
        var entries = new[] { Folder("b"), Folder(".hidden"), File(".hidden.png") };

        var selected = DocumentSelection.SelectFolders(entries, SortField.FileName, SortOrder.Ascending, includeHidden: true);

        Assert.Equal(new[] { ".hidden", "b" }, Names(selected));
    }

    [Theory]
    [InlineData(SortOrder.Ascending, new[] { "a", "B", "c" })]
    [InlineData(SortOrder.Descending, new[] { "c", "B", "a" })]
    public void SelectFolders_SortsByNameIgnoringCase(SortOrder sortOrder, string[] expected)
    {
        var entries = new[] { Folder("c"), Folder("a"), Folder("B") };

        var selected = DocumentSelection.SelectFolders(entries, SortField.FileName, sortOrder);

        Assert.Equal(expected, Names(selected));
    }

    [Theory]
    [InlineData(SortOrder.Ascending, new[] { "old", "mid", "new" })]
    [InlineData(SortOrder.Descending, new[] { "new", "mid", "old" })]
    public void SelectFolders_SortsByDate(SortOrder sortOrder, string[] expected)
    {
        var entries = new[] { Folder("mid", 1_650_000_000_000), Folder("new", 1_690_000_000_000), Folder("old", 1_620_000_000_000) };

        var selected = DocumentSelection.SelectFolders(entries, SortField.FileDate, sortOrder);

        Assert.Equal(expected, Names(selected));
    }
}
