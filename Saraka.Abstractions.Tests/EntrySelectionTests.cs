namespace Saraka.Abstractions.Tests;

/// <summary>
/// Tests for <see cref="EntrySelection.Apply{T}"/>. Some cases are adapted from Galeria's
/// DocumentSelectionTests.
/// </summary>
public class EntrySelectionTests
{
    /// <summary>
    /// An entry for the tests.
    /// </summary>
    /// <param name="Name">The entry's name.</param>
    /// <param name="IsDirectory">True for a directory.</param>
    /// <param name="IsHidden">True if the entry is hidden.</param>
    /// <param name="Time">The entry's last-write time, as a number of hours after 2026-01-01.</param>
    private sealed record Item(string Name, bool IsDirectory = false, bool IsHidden = false, int Time = 0);

    /// <summary>
    /// Applies options to items, recording which items the hidden and time functions were asked about.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <param name="options">The options.</param>
    /// <param name="hiddenAsked">Receives the names whose hidden flag was asked for.</param>
    /// <param name="timeAsked">Receives the names whose time was asked for.</param>
    /// <returns>The names of the selected items, in order.</returns>
    private static string[] Apply(Item[] items, EntryOptions? options, List<string>? hiddenAsked = null, List<string>? timeAsked = null)
    {
        return EntrySelection.Apply(
                items,
                options,
                item => item.Name,
                item => item.IsDirectory,
                item =>
                {
                    hiddenAsked?.Add(item.Name);
                    return item.IsHidden;
                },
                item =>
                {
                    timeAsked?.Add(item.Name);
                    return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(item.Time);
                })
            .Select(item => item.Name)
            .ToArray();
    }

    [Fact]
    public void NullOptions_ReturnEverythingInTheGivenOrder()
    {
        var items = new[] { new Item("b"), new Item("d", IsDirectory: true), new Item(".h", IsHidden: true), new Item("a") };

        Assert.Equal(new[] { "b", "d", ".h", "a" }, Apply(items, null));
    }

    [Fact]
    public void DefaultOptions_ReturnEverythingInTheGivenOrder()
    {
        var items = new[] { new Item("b"), new Item("d", IsDirectory: true), new Item(".h", IsHidden: true), new Item("a") };

        Assert.Equal(new[] { "b", "d", ".h", "a" }, Apply(items, new EntryOptions()));
    }

    [Fact]
    public void FileTypes_IncludeMatchingFilesIgnoringCase()
    {
        var items = new[] { new Item("a.jpeg"), new Item("b.JPG"), new Item("c.png"), new Item("d.txt") };

        Assert.Equal(new[] { "a.jpeg", "b.JPG", "c.png" }, Apply(items, new EntryOptions { FileTypes = new[] { "jpeg", "jpg", "PNG" } }));
    }

    [Fact]
    public void FileTypes_AddNoSynonyms()
    {
        var items = new[] { new Item("a.jpeg"), new Item("b.jpg") };

        Assert.Equal(new[] { "a.jpeg" }, Apply(items, new EntryOptions { FileTypes = new[] { "jpeg" } }));
    }

    [Fact]
    public void FileTypes_NeedAPeriodBeforeTheType()
    {
        var items = new[] { new Item("a.png"), new Item("png"), new Item("apng"), new Item("README"), new Item("b.") };

        Assert.Equal(new[] { "a.png" }, Apply(items, new EntryOptions { FileTypes = new[] { "png" } }));
    }

    [Fact]
    public void FileTypes_DontApplyToDirectories()
    {
        var items = new[] { new Item("photos", IsDirectory: true), new Item("a.png"), new Item("b.txt") };

        Assert.Equal(new[] { "photos", "a.png" }, Apply(items, new EntryOptions { FileTypes = new[] { "png" } }));
    }

    [Fact]
    public void FileTypes_EmptyIncludesNoFiles()
    {
        var items = new[] { new Item("d", IsDirectory: true), new Item("a.png") };

        Assert.Equal(new[] { "d" }, Apply(items, new EntryOptions { FileTypes = Array.Empty<string>() }));
    }

    [Theory]
    [InlineData(true, true, new[] { ".d", "d", ".f", "f" })]
    [InlineData(false, true, new[] { ".d", "d", "f" })]
    [InlineData(true, false, new[] { "d", ".f", "f" })]
    [InlineData(false, false, new[] { "d", "f" })]
    public void HiddenEntries_AreIncludedOrLeftOutByKind(bool files, bool directories, string[] expected)
    {
        var items = new[]
        {
            new Item(".d", IsDirectory: true, IsHidden: true),
            new Item("d", IsDirectory: true),
            new Item(".f", IsHidden: true),
            new Item("f"),
        };
        var options = new EntryOptions { IncludeHiddenFiles = files, IncludeHiddenDirectories = directories };

        Assert.Equal(expected, Apply(items, options));
    }

    [Fact]
    public void HiddenFunction_IsCalledOnlyWhenHiddenEntriesAreLeftOut()
    {
        var items = new[] { new Item("d", IsDirectory: true), new Item("f") };
        var asked = new List<string>();

        Apply(items, new EntryOptions { SortField = EntrySortField.Name }, hiddenAsked: asked);
        Assert.Empty(asked);

        Apply(items, new EntryOptions { IncludeHiddenFiles = false }, hiddenAsked: asked);
        Assert.Equal(new[] { "f" }, asked);
    }

    [Theory]
    [InlineData(EntrySortDirection.Ascending, new[] { "a", "B", "c", "a.png", "B.png", "c.png" })]
    [InlineData(EntrySortDirection.Descending, new[] { "c", "B", "a", "c.png", "B.png", "a.png" })]
    public void SortByName_IgnoresCaseAndPutsDirectoriesFirst(EntrySortDirection direction, string[] expected)
    {
        var items = new[]
        {
            new Item("c.png"), new Item("c", IsDirectory: true), new Item("a.png"),
            new Item("B", IsDirectory: true), new Item("B.png"), new Item("a", IsDirectory: true),
        };

        Assert.Equal(expected, Apply(items, new EntryOptions { SortField = EntrySortField.Name, SortDirection = direction }));
    }

    [Theory]
    [InlineData(EntrySortDirection.Ascending, new[] { "A", "a" })]
    [InlineData(EntrySortDirection.Descending, new[] { "a", "A" })]
    public void SortByName_BreaksTiesWithCase(EntrySortDirection direction, string[] expected)
    {
        var items = new[] { new Item("a"), new Item("A") };

        Assert.Equal(expected, Apply(items, new EntryOptions { SortField = EntrySortField.Name, SortDirection = direction }));
    }

    [Theory]
    [InlineData(EntrySortDirection.Ascending, new[] { "old-d", "new-d", "old", "tie-a", "tie-B", "new" })]
    [InlineData(EntrySortDirection.Descending, new[] { "new-d", "old-d", "new", "tie-B", "tie-a", "old" })]
    public void SortByTime_BreaksTiesByNameAndPutsDirectoriesFirst(EntrySortDirection direction, string[] expected)
    {
        var items = new[]
        {
            new Item("new", Time: 30), new Item("tie-B", Time: 20), new Item("old", Time: 10),
            new Item("tie-a", Time: 20), new Item("new-d", IsDirectory: true, Time: 5),
            new Item("old-d", IsDirectory: true, Time: 1),
        };

        Assert.Equal(expected, Apply(items, new EntryOptions { SortField = EntrySortField.LastWriteTime, SortDirection = direction }));
    }

    [Fact]
    public void TimeFunction_IsCalledOnlyWhenSortingByTimeAndOnlyForKeptEntries()
    {
        var items = new[] { new Item("a.png"), new Item("b.txt") };
        var asked = new List<string>();

        Apply(items, new EntryOptions { SortField = EntrySortField.Name }, timeAsked: asked);
        Assert.Empty(asked);

        Apply(items, new EntryOptions { SortField = EntrySortField.LastWriteTime, FileTypes = new[] { "png" } }, timeAsked: asked);
        Assert.Equal(new[] { "a.png" }, asked);
    }

    [Fact]
    public void Options_CombineFiltersAndSort()
    {
        var items = new[]
        {
            new Item("b.jpg", Time: 2), new Item(".c.jpg", IsHidden: true, Time: 1),
            new Item("a.jpg", Time: 3), new Item("d.txt", Time: 0), new Item("e", IsDirectory: true),
        };
        var options = new EntryOptions
        {
            FileTypes = new[] { "jpg" },
            IncludeHiddenFiles = false,
            SortField = EntrySortField.LastWriteTime,
            SortDirection = EntrySortDirection.Descending,
        };

        Assert.Equal(new[] { "e", "a.jpg", "b.jpg" }, Apply(items, options));
    }

    public static TheoryData<string> InvalidOptions => new()
    {
        "SortField",
        "SortDirection",
        "NullFileType",
        "EmptyFileType",
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void InvalidOptions_ThrowArgumentException(string invalid)
    {
        var options = invalid switch
        {
            "SortField" => new EntryOptions { SortField = (EntrySortField)99 },
            "SortDirection" => new EntryOptions { SortDirection = (EntrySortDirection)99 },
            "NullFileType" => new EntryOptions { FileTypes = new[] { "png", null! } },
            "EmptyFileType" => new EntryOptions { FileTypes = new[] { "png", "" } },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };

        var ex = Assert.Throws<ArgumentException>(() => Apply(new[] { new Item("a") }, options));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        var items = new[] { new Item("a") };
        Func<Item, string> name = item => item.Name;
        Func<Item, bool> flag = _ => false;
        Func<Item, DateTime> time = _ => DateTime.UnixEpoch;

        Assert.Throws<ArgumentNullException>(() => EntrySelection.Apply<Item>(null!, null, name, flag, flag, time));
        Assert.Throws<ArgumentNullException>(() => EntrySelection.Apply(items, null, null!, flag, flag, time));
        Assert.Throws<ArgumentNullException>(() => EntrySelection.Apply(items, null, name, null!, flag, time));
        Assert.Throws<ArgumentNullException>(() => EntrySelection.Apply(items, null, name, flag, null!, time));
        Assert.Throws<ArgumentNullException>(() => EntrySelection.Apply(items, null, name, flag, flag, null!));
    }
}
