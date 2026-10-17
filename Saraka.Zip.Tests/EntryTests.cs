using System.IO.Compression;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

/// <summary>
/// Tests for the members every entry shares: names, paths, attributes, times, rename and move.
/// </summary>
public class EntryTests : ZipTestBase
{
    /// <summary>
    /// Names that every implementation must reject.
    /// </summary>
    public static TheoryData<string> InvalidNames => new()
    {
        "",
        ".",
        "..",
        "a/b",
        "a\\b",
        "a\0b",
        "a\nb",
    };

    // Names, paths and parents

    [Fact]
    public async Task NestedEntry_HasNamePathAndParentChain()
    {
        var a = await Root.CreateDirectoryAsync("a");
        var b = await a.CreateDirectoryAsync("b");
        var file = await b.CreateFileAsync("c.txt");

        Assert.Equal("c.txt", file.Name);
        Assert.Equal("/a/b/c.txt", file.Path);
        Assert.Equal("/a/b", file.Parent!.Path);
        Assert.Equal("b", file.Parent!.Name);
        Assert.Equal("/a", file.Parent!.Parent!.Path);
        Assert.Equal("/", file.Parent!.Parent!.Parent!.Path);
        Assert.Null(file.Parent!.Parent!.Parent!.Parent);
    }

    [Fact]
    public async Task Parent_ReturnsANewUsableInstanceEachTime()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var file = await dir.CreateFileAsync("a.txt");

        Assert.NotSame(file.Parent, file.Parent);
        Assert.Equal("/d/a.txt", Assert.Single(await file.Parent!.GetEntriesAsync()).Path);
    }

    [Fact]
    public async Task Names_AreCaseSensitive()
    {
        await Root.CreateFileAsync("a.txt");
        await Root.CreateFileAsync("A.txt");

        Assert.Equal(2, (await Root.GetEntriesAsync()).Count());
        await Assert.ThrowsAsync<FileNotFoundException>(() => Root.GetFileAsync("A.TXT"));
    }

    // Attributes

    [Fact]
    public async Task GetAttributesAsync_DistinguishesFilesAndDirectories()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");

        Assert.Equal(FileAttributes.Normal, await file.GetAttributesAsync());
        Assert.Equal(FileAttributes.Directory, await dir.GetAttributesAsync());
        Assert.Equal(FileAttributes.Directory, await Root.GetAttributesAsync());
    }

    [Fact]
    public async Task ImplicitDirectory_HasDirectoryAttribute()
    {
        await UseZipAsync(("d/a.txt", "x"));

        var dir = await Root.GetDirectoryAsync("d");

        Assert.Equal(FileAttributes.Directory, await dir.GetAttributesAsync());
    }

    [Fact]
    public async Task SetAttributesAsync_RoundTripsAndSurvivesReopening()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");

        await file.SetAttributesAsync(FileAttributes.ReadOnly | FileAttributes.Hidden);
        await dir.SetAttributesAsync(FileAttributes.Hidden);

        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, await file.GetAttributesAsync());
        Assert.Equal(FileAttributes.Directory | FileAttributes.Hidden, await dir.GetAttributesAsync());

        await ReopenAsync();

        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, await (await Root.GetFileAsync("a.txt")).GetAttributesAsync());
        Assert.Equal(FileAttributes.Directory | FileAttributes.Hidden, await (await Root.GetDirectoryAsync("d")).GetAttributesAsync());
    }

    [Fact]
    public async Task SetAttributesAsync_Normal_ClearsAttributes()
    {
        var file = await Root.CreateFileAsync("a.txt");
        await file.SetAttributesAsync(FileAttributes.ReadOnly);

        await file.SetAttributesAsync(FileAttributes.Normal);

        Assert.Equal(FileAttributes.Normal, await file.GetAttributesAsync());
    }

    [Fact]
    public async Task SetAttributesAsync_PreservesUnixPermissionBits()
    {
        var file = await Root.CreateFileAsync("a.sh");
        await ReopenAsync();
        const int unixMode = 0x81ED << 16; // regular file, rwxr-xr-x
        await Fs.DisposeAsync();
        using (var archive = ZipFile.Open(ZipPath, ZipArchiveMode.Update))
            archive.GetEntry("a.sh")!.ExternalAttributes = unixMode;
        await ReopenAsync();

        await (await Root.GetFileAsync("a.sh")).SetAttributesAsync(FileAttributes.ReadOnly);
        await Fs.DisposeAsync();

        using (var archive = ZipFile.OpenRead(ZipPath))
            Assert.Equal(unixMode | (int)FileAttributes.ReadOnly, archive.GetEntry("a.sh")!.ExternalAttributes);
        await ReopenAsync();
    }

    [Fact]
    public async Task SetAttributesAsync_ImplicitDirectory_GivesItAnEntry()
    {
        await UseZipAsync(("d/a.txt", "x"));
        var dir = await Root.GetDirectoryAsync("d");

        await dir.SetAttributesAsync(FileAttributes.Hidden);

        Assert.Contains("d/", (await ReadZipAsync()).Keys);
        Assert.Equal(FileAttributes.Directory | FileAttributes.Hidden, await (await Root.GetDirectoryAsync("d")).GetAttributesAsync());
    }

    [Fact]
    public async Task SetAttributesAsync_Root_DoesNothing()
    {
        await Root.SetAttributesAsync(FileAttributes.Hidden);

        Assert.Equal(FileAttributes.Directory, await Root.GetAttributesAsync());
        Assert.Empty(await ReadZipAsync());
    }

    [Fact]
    public async Task AttributeMethods_MissingEntries_ThrowNotFound()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");
        await (await Root.GetFileAsync("a.txt")).DeleteAsync();
        await (await Root.GetDirectoryAsync("d")).DeleteAsync();

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetAttributesAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.SetAttributesAsync(FileAttributes.Normal));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetAttributesAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.SetAttributesAsync(FileAttributes.Normal));
    }

    // Times

    [Fact]
    public async Task LastWriteTime_RoundTripsAsUtc()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var time = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        await file.SetLastWriteTimeUtcAsync(time);
        var result = await file.GetLastWriteTimeUtcAsync();

        Assert.Equal(time, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public async Task LastWriteTime_SurvivesReopeningToTwoSecondPrecision()
    {
        var file = await Root.CreateFileAsync("a.txt");
        await file.SetLastWriteTimeUtcAsync(new DateTime(2020, 7, 2, 3, 4, 7, DateTimeKind.Utc));

        await ReopenAsync();

        // Zip stores times in MS-DOS format, which counts in two-second steps.
        Assert.Equal(new DateTime(2020, 7, 2, 3, 4, 6, DateTimeKind.Utc), await (await Root.GetFileAsync("a.txt")).GetLastWriteTimeUtcAsync());
    }

    [Theory]
    [InlineData(1979)]
    [InlineData(2108)]
    public async Task LastWriteTime_OutsideZipRange_ThrowsArgumentOutOfRangeException(int year)
    {
        var file = await Root.CreateFileAsync("a.txt");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => file.SetLastWriteTimeUtcAsync(new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task LastWriteTime_UnspecifiedKind_IsTreatedAsUtc()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await file.SetLastWriteTimeUtcAsync(new DateTime(2021, 6, 1, 12, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(new DateTime(2021, 6, 1, 12, 0, 0, DateTimeKind.Utc), await file.GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task LastWriteTime_LocalKind_IsConvertedToUtc()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var local = new DateTime(2021, 6, 1, 12, 0, 0, DateTimeKind.Local);

        await file.SetLastWriteTimeUtcAsync(local);

        Assert.Equal(local.ToUniversalTime(), await file.GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task LastWriteTime_NewFile_IsRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);

        var file = await Root.CreateFileAsync("a.txt");

        Assert.InRange(await file.GetLastWriteTimeUtcAsync(), before, DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task LastWriteTime_ExplicitDirectory_RoundTrips()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var time = new DateTime(2019, 5, 6, 7, 8, 10, DateTimeKind.Utc);

        await dir.SetLastWriteTimeUtcAsync(time);
        await ReopenAsync();

        Assert.Equal(time, await (await Root.GetDirectoryAsync("d")).GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task LastWriteTime_ImplicitDirectory_IsLatestTimeInside()
    {
        await UseZipAsync(("d/a.txt", "x"), ("d/e/b.txt", "y"));
        var a = await (await Root.GetDirectoryAsync("d")).GetFileAsync("a.txt");
        var b = await (await (await Root.GetDirectoryAsync("d")).GetDirectoryAsync("e")).GetFileAsync("b.txt");
        await a.SetLastWriteTimeUtcAsync(new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await b.SetLastWriteTimeUtcAsync(new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var dir = await Root.GetDirectoryAsync("d");

        Assert.Equal(new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc), await dir.GetLastWriteTimeUtcAsync());
        Assert.Equal(new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc), await Root.GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task LastWriteTime_EmptyRoot_IsTheZipFilesTime()
    {
        await ReopenAsync();

        Assert.Equal(File.GetLastWriteTimeUtc(ZipPath), await Root.GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task LastWriteTime_SetOnImplicitDirectory_GivesItAnEntry()
    {
        await UseZipAsync(("d/a.txt", "x"));
        var time = new DateTime(2012, 2, 2, 2, 2, 2, DateTimeKind.Utc);

        await (await Root.GetDirectoryAsync("d")).SetLastWriteTimeUtcAsync(time);

        Assert.Contains("d/", (await ReadZipAsync()).Keys);
        Assert.Equal(time, await (await Root.GetDirectoryAsync("d")).GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task LastWriteTime_SetOnRoot_DoesNothing()
    {
        await Root.SetLastWriteTimeUtcAsync(new DateTime(2012, 2, 2, 2, 2, 2, DateTimeKind.Utc));

        Assert.Empty(await ReadZipAsync());
    }

    [Fact]
    public async Task CreationTime_IsTheLastWriteTime()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var time = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        await file.SetLastWriteTimeUtcAsync(time);

        var result = await file.GetCreationTimeUtcAsync();

        Assert.Equal(time, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public async Task SetCreationTimeUtcAsync_DoesNothing()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var time = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        await file.SetLastWriteTimeUtcAsync(time);

        await file.SetCreationTimeUtcAsync(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(time, await file.GetCreationTimeUtcAsync());
    }

    [Fact]
    public async Task TimeMethods_MissingEntries_ThrowNotFound()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");
        await (await Root.GetFileAsync("a.txt")).DeleteAsync();
        await (await Root.GetDirectoryAsync("d")).DeleteAsync();

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetCreationTimeUtcAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetLastWriteTimeUtcAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.SetCreationTimeUtcAsync(DateTime.UtcNow));
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.SetLastWriteTimeUtcAsync(DateTime.UtcNow));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetLastWriteTimeUtcAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.SetLastWriteTimeUtcAsync(DateTime.UtcNow));
    }

    // Rename

    [Fact]
    public async Task RenameAsync_File_RenamesEntryAndUpdatesInstance()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");

        await file.RenameAsync("b.txt");

        Assert.Equal("b.txt", file.Name);
        Assert.Equal("/b.txt", file.Path);
        Assert.Equal("hello", await file.ReadAllTextAsync());
        var entries = await ReadZipAsync();
        Assert.Equal(new[] { "b.txt" }, entries.Keys);
        Assert.Equal("hello", entries["b.txt"]);
    }

    [Fact]
    public async Task RenameAsync_KeepsAttributesAndTime()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");
        var time = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        await file.SetLastWriteTimeUtcAsync(time);
        await file.SetAttributesAsync(FileAttributes.ReadOnly);

        await file.RenameAsync("b.txt");

        Assert.Equal(time, await file.GetLastWriteTimeUtcAsync());
        Assert.Equal(FileAttributes.ReadOnly, await file.GetAttributesAsync());
    }

    [Fact]
    public async Task RenameAsync_Directory_RenamesEverythingInside()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await CreateFileAsync(dir, "a.txt", "a");
        var sub = await dir.CreateDirectoryAsync("sub");
        await CreateFileAsync(sub, "b.txt", "b");

        await dir.RenameAsync("e");

        Assert.Equal("/e", dir.Path);
        var entries = await ReadZipAsync();
        Assert.Equal(new[] { "e/", "e/a.txt", "e/sub/", "e/sub/b.txt" }, entries.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("b", entries["e/sub/b.txt"]);
    }

    [Fact]
    public async Task RenameAsync_ImplicitDirectory_RenamesEverythingInside()
    {
        await UseZipAsync(("d/a.txt", "a"), ("d/sub/b.txt", "b"));

        await (await Root.GetDirectoryAsync("d")).RenameAsync("e");

        Assert.Equal(new[] { "e/a.txt", "e/sub/b.txt" }, (await ReadZipAsync()).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task RenameAsync_DoesNotUpdateOtherInstances()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var other = await Root.GetFileAsync("a.txt");

        await file.RenameAsync("b.txt");

        Assert.Equal("/a.txt", other.Path);
        await Assert.ThrowsAsync<FileNotFoundException>(() => other.GetLengthAsync());
    }

    [Fact]
    public async Task RenameAsync_SameName_DoesNothing()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");

        await file.RenameAsync("a.txt");

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("hello", (await ReadZipAsync())["a.txt"]);
    }

    [Fact]
    public async Task RenameAsync_CaseOnlyChange_Renames()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await file.RenameAsync("A.txt");

        Assert.Equal("/A.txt", file.Path);
        Assert.Equal(new[] { "A.txt" }, (await ReadZipAsync()).Keys);
    }

    [Fact]
    public async Task RenameAsync_NameTaken_ThrowsInvalidOperationException()
    {
        var file = await CreateFileAsync(Root, "a.txt", "a");
        await CreateFileAsync(Root, "b.txt", "b");
        await Root.CreateDirectoryAsync("d");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.RenameAsync("b.txt"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => file.RenameAsync("d"));

        Assert.Equal("/a.txt", file.Path);
        var entries = await ReadZipAsync();
        Assert.Equal("a", entries["a.txt"]);
        Assert.Equal("b", entries["b.txt"]);
    }

    [Fact]
    public async Task RenameAsync_NameTakenByImplicitDirectory_ThrowsInvalidOperationException()
    {
        await UseZipAsync(("a.txt", "a"), ("d/b.txt", "b"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await (await Root.GetFileAsync("a.txt")).RenameAsync("d"));
    }

    [Fact]
    public async Task RenameAsync_NullName_ThrowsArgumentNullException()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await Assert.ThrowsAsync<ArgumentNullException>(() => file.RenameAsync(null!));
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task RenameAsync_InvalidName_ThrowsArgumentException(string name)
    {
        var file = await Root.CreateFileAsync("a.txt");

        await Assert.ThrowsAsync<ArgumentException>(() => file.RenameAsync(name));
        Assert.Equal("/a.txt", file.Path);
    }

    [Fact]
    public async Task RenameAsync_Root_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.RenameAsync("r"));
    }

    [Fact]
    public async Task RenameAsync_MissingEntry_ThrowsNotFound()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");
        await (await Root.GetFileAsync("a.txt")).DeleteAsync();
        await (await Root.GetDirectoryAsync("d")).DeleteAsync();

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.RenameAsync("b.txt"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.RenameAsync("e"));
    }

    // Move

    [Fact]
    public async Task MoveToAsync_File_MovesEntryAndUpdatesInstance()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");
        var dir = await Root.CreateDirectoryAsync("d");

        await file.MoveToAsync(dir);

        Assert.Equal("/d/a.txt", file.Path);
        Assert.Equal("/d", file.Parent!.Path);
        var entries = await ReadZipAsync();
        Assert.Equal(new[] { "d/", "d/a.txt" }, entries.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("hello", entries["d/a.txt"]);
    }

    [Fact]
    public async Task MoveToAsync_Directory_MovesEverythingInside()
    {
        var source = await Root.CreateDirectoryAsync("src");
        await CreateFileAsync(source, "a.txt", "a");
        var target = await Root.CreateDirectoryAsync("dst");

        await source.MoveToAsync(target);

        Assert.Equal("/dst/src", source.Path);
        Assert.Equal(new[] { "dst/", "dst/src/", "dst/src/a.txt" }, (await ReadZipAsync()).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task MoveToAsync_LastEntryOutOfImplicitDirectory_KeepsTheDirectory()
    {
        await UseZipAsync(("d/a.txt", "a"));
        var file = await (await Root.GetDirectoryAsync("d")).GetFileAsync("a.txt");

        await file.MoveToAsync(Root);

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("/d", Assert.Single(await Root.EnumerateDirectoriesAsync()).Path);
        Assert.Empty(await (await Root.GetDirectoryAsync("d")).GetEntriesAsync());
    }

    [Fact]
    public async Task MoveToAsync_DoesNotUpdateOtherInstances()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var other = await Root.GetFileAsync("a.txt");

        await file.MoveToAsync(await Root.CreateDirectoryAsync("d"));

        Assert.Equal("/a.txt", other.Path);
    }

    [Fact]
    public async Task MoveToAsync_CurrentParent_DoesNothing()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var file = await dir.CreateFileAsync("a.txt");

        await file.MoveToAsync(await Root.GetDirectoryAsync("d"));

        Assert.Equal("/d/a.txt", file.Path);
    }

    [Fact]
    public async Task MoveToAsync_NameTaken_ThrowsInvalidOperationException()
    {
        var file = await CreateFileAsync(Root, "a.txt", "root");
        var dir = await Root.CreateDirectoryAsync("d");
        await CreateFileAsync(dir, "a.txt", "nested");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.MoveToAsync(dir));

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("nested", (await ReadZipAsync())["d/a.txt"]);
    }

    [Fact]
    public async Task MoveToAsync_IntoItselfOrADescendant_ThrowsInvalidOperationException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var grandchild = await (await dir.CreateDirectoryAsync("child")).CreateDirectoryAsync("grandchild");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dir.MoveToAsync(dir));
        await Assert.ThrowsAsync<InvalidOperationException>(() => dir.MoveToAsync(grandchild));
    }

    [Fact]
    public async Task MoveToAsync_IntoSiblingWithSharedPrefix_Moves()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var sibling = await Root.CreateDirectoryAsync("dd");

        await dir.MoveToAsync(sibling);

        Assert.Equal("/dd/d", dir.Path);
    }

    [Fact]
    public async Task MoveToAsync_Root_ThrowsInvalidOperationException()
    {
        var dir = await Root.CreateDirectoryAsync("d");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Root.MoveToAsync(dir));
    }

    [Fact]
    public async Task MoveToAsync_NullDestination_ThrowsArgumentNullException()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await Assert.ThrowsAsync<ArgumentNullException>(() => file.MoveToAsync(null!));
    }

    [Fact]
    public async Task MoveToAsync_DestinationInAnotherFileSystem_ThrowsInvalidOperationException()
    {
        var file = await Root.CreateFileAsync("a.txt");
        await using var other = await FileSystem.OpenAsync(Path.Combine(TempPath, "other.zip"), canWrite: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.MoveToAsync(other.Root));
    }

    [Fact]
    public async Task MoveToAsync_DestinationFromAnotherImplementation_ThrowsInvalidOperationException()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.MoveToAsync(ForeignDirectory.Create()));
    }

    [Fact]
    public async Task MoveToAsync_MissingDestinationOrEntry_ThrowsNotFound()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var gone = await Root.CreateDirectoryAsync("gone");
        var target = await Root.CreateDirectoryAsync("dst");
        await (await Root.GetDirectoryAsync("gone")).DeleteAsync();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => file.MoveToAsync(gone));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => gone.MoveToAsync(target));
        await (await Root.GetFileAsync("a.txt")).DeleteAsync();
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.MoveToAsync(target));
    }
}
