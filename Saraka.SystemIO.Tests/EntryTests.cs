using Saraka.Abstractions;

namespace Saraka.SystemIO.Tests;

/// <summary>
/// Tests for the members every entry shares: names, paths, attributes, times, rename and move.
/// </summary>
public class EntryTests : FileSystemTestBase
{
    public static TheoryData<string> InvalidNames => new()
    {
        "",
        ".",
        "..",
        "a/b",
        "a\\b",
        "a\0b",
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
        Assert.Equal("/a/b", file.Parent.Path);
        Assert.Equal("b", file.Parent.Name);
        Assert.Equal("/a", file.Parent.Parent.Path);
        Assert.Equal("/", file.Parent.Parent.Parent.Path);
        Assert.Null(file.Parent.Parent.Parent.Parent);
    }

    [Fact]
    public async Task Parent_ReturnsANewInstanceEachTime()
    {
        var file = await Root.CreateFileAsync("a.txt");

        Assert.NotSame(file.Parent, file.Parent);
        Assert.Equal(file.Parent.Path, file.Parent.Path);
    }

    [Fact]
    public async Task Parent_IsAUsableDirectory()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var file = await dir.CreateFileAsync("a.txt");

        var entries = await file.Parent.GetEntriesAsync();

        Assert.Equal("/d/a.txt", Assert.Single(entries).Path);
    }

    // Attributes

    [Fact]
    public async Task GetAttributesAsync_DistinguishesFilesAndDirectories()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");

        Assert.False((await file.GetAttributesAsync()).HasFlag(FileAttributes.Directory));
        Assert.True((await dir.GetAttributesAsync()).HasFlag(FileAttributes.Directory));
    }

    [Fact]
    public async Task SetAttributesAsync_SetsTheAttributes()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await file.SetAttributesAsync(FileAttributes.ReadOnly);

        Assert.True((await file.GetAttributesAsync()).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.GetAttributes(HostPath("a.txt")).HasFlag(FileAttributes.ReadOnly));

        await file.SetAttributesAsync(FileAttributes.Normal);
        Assert.False((await file.GetAttributesAsync()).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public async Task AttributeMethods_MissingFile_ThrowFileNotFoundException()
    {
        var file = await Root.CreateFileAsync("a.txt");
        File.Delete(HostPath("a.txt"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetAttributesAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.SetAttributesAsync(FileAttributes.Normal));
    }

    [Fact]
    public async Task AttributeMethods_MissingDirectory_ThrowDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        Directory.Delete(HostPath("d"));

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
    public async Task LastWriteTime_WorksForDirectories()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var time = new DateTime(2019, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        await dir.SetLastWriteTimeUtcAsync(time);

        Assert.Equal(time, await dir.GetLastWriteTimeUtcAsync());
    }

    [Fact]
    public async Task CreationTime_IsUtc()
    {
        var file = await Root.CreateFileAsync("a.txt");

        var result = await file.GetCreationTimeUtcAsync();

        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public async Task CreationTime_RoundTrips()
    {
        // Creation time can't reliably be set on Linux or Android.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return;

        var file = await Root.CreateFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");
        var time = new DateTime(2018, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        await file.SetCreationTimeUtcAsync(time);
        await dir.SetCreationTimeUtcAsync(time);

        Assert.Equal(time, await file.GetCreationTimeUtcAsync());
        Assert.Equal(time, await dir.GetCreationTimeUtcAsync());
    }

    [Fact]
    public async Task TimeMethods_MissingFile_ThrowFileNotFoundException()
    {
        var file = await Root.CreateFileAsync("a.txt");
        File.Delete(HostPath("a.txt"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetCreationTimeUtcAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetLastWriteTimeUtcAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.SetCreationTimeUtcAsync(DateTime.UtcNow));
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.SetLastWriteTimeUtcAsync(DateTime.UtcNow));
    }

    [Fact]
    public async Task TimeMethods_MissingDirectory_ThrowDirectoryNotFoundException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        Directory.Delete(HostPath("d"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetCreationTimeUtcAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetLastWriteTimeUtcAsync());
    }

    // Rename

    [Fact]
    public async Task RenameAsync_File_RenamesOnDiskAndUpdatesInstance()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");

        await file.RenameAsync("b.txt");

        Assert.Equal("b.txt", file.Name);
        Assert.Equal("/b.txt", file.Path);
        Assert.False(File.Exists(HostPath("a.txt")));
        Assert.Equal("hello", File.ReadAllText(HostPath("b.txt")));
        Assert.Equal("hello", await file.ReadAllTextAsync());
    }

    [Fact]
    public async Task RenameAsync_Directory_RenamesOnDiskWithContents()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        await dir.CreateFileAsync("a.txt");

        await dir.RenameAsync("e");

        Assert.Equal("/e", dir.Path);
        Assert.False(Directory.Exists(HostPath("d")));
        Assert.True(File.Exists(HostPath("e", "a.txt")));
        Assert.Equal("/e/a.txt", Assert.Single(await dir.GetEntriesAsync()).Path);
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
        Assert.Equal("hello", File.ReadAllText(HostPath("a.txt")));
    }

    [Fact]
    public async Task RenameAsync_CaseOnlyChange_Renames()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await file.RenameAsync("A.txt");

        Assert.Equal("/A.txt", file.Path);
        var names = (await Root.GetEntriesAsync()).Select(e => e.Name).ToList();
        Assert.Equal(new[] { "A.txt" }, names);
    }

    [Fact]
    public async Task RenameAsync_NameTakenByFile_ThrowsInvalidOperationException()
    {
        var file = await CreateFileAsync(Root, "a.txt", "a");
        await CreateFileAsync(Root, "b.txt", "b");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.RenameAsync("b.txt"));

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("b", File.ReadAllText(HostPath("b.txt")));
    }

    [Fact]
    public async Task RenameAsync_NameTakenByDirectory_ThrowsInvalidOperationException()
    {
        var file = await Root.CreateFileAsync("a.txt");
        await Root.CreateDirectoryAsync("d");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.RenameAsync("d"));
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
        Assert.True(File.Exists(HostPath("a.txt")));
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
        File.Delete(HostPath("a.txt"));
        Directory.Delete(HostPath("d"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.RenameAsync("b.txt"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.RenameAsync("e"));
    }

    // Move

    [Fact]
    public async Task MoveToAsync_File_MovesOnDiskAndUpdatesInstance()
    {
        var file = await CreateFileAsync(Root, "a.txt", "hello");
        var dir = await Root.CreateDirectoryAsync("d");

        await file.MoveToAsync(dir);

        Assert.Equal("a.txt", file.Name);
        Assert.Equal("/d/a.txt", file.Path);
        Assert.Equal("/d", file.Parent.Path);
        Assert.False(File.Exists(HostPath("a.txt")));
        Assert.Equal("hello", File.ReadAllText(HostPath("d", "a.txt")));
    }

    [Fact]
    public async Task MoveToAsync_Directory_MovesWithContents()
    {
        var source = await Root.CreateDirectoryAsync("src");
        await source.CreateFileAsync("a.txt");
        var target = await Root.CreateDirectoryAsync("dst");

        await source.MoveToAsync(target);

        Assert.Equal("/dst/src", source.Path);
        Assert.True(File.Exists(HostPath("dst", "src", "a.txt")));
        Assert.False(Directory.Exists(HostPath("src")));
    }

    [Fact]
    public async Task MoveToAsync_DoesNotUpdateOtherInstances()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var other = await Root.GetFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");

        await file.MoveToAsync(dir);

        Assert.Equal("/a.txt", other.Path);
    }

    [Fact]
    public async Task MoveToAsync_CurrentParent_DoesNothing()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var file = await dir.CreateFileAsync("a.txt");

        await file.MoveToAsync(await Root.GetDirectoryAsync("d"));

        Assert.Equal("/d/a.txt", file.Path);
        Assert.True(File.Exists(HostPath("d", "a.txt")));
    }

    [Fact]
    public async Task MoveToAsync_NameTaken_ThrowsInvalidOperationException()
    {
        var file = await CreateFileAsync(Root, "a.txt", "root");
        var dir = await Root.CreateDirectoryAsync("d");
        await CreateFileAsync(dir, "a.txt", "nested");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.MoveToAsync(dir));

        Assert.Equal("/a.txt", file.Path);
        Assert.Equal("nested", File.ReadAllText(HostPath("d", "a.txt")));
    }

    [Fact]
    public async Task MoveToAsync_NameTakenByDirectory_ThrowsInvalidOperationException()
    {
        var file = await Root.CreateFileAsync("a");
        var dir = await Root.CreateDirectoryAsync("d");
        await dir.CreateDirectoryAsync("a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.MoveToAsync(dir));
    }

    [Fact]
    public async Task MoveToAsync_IntoItself_ThrowsInvalidOperationException()
    {
        var dir = await Root.CreateDirectoryAsync("d");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dir.MoveToAsync(dir));
    }

    [Fact]
    public async Task MoveToAsync_IntoDescendant_ThrowsInvalidOperationException()
    {
        var dir = await Root.CreateDirectoryAsync("d");
        var child = await dir.CreateDirectoryAsync("child");
        var grandchild = await child.CreateDirectoryAsync("grandchild");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dir.MoveToAsync(grandchild));
        Assert.True(Directory.Exists(HostPath("d", "child", "grandchild")));
    }

    [Fact]
    public async Task MoveToAsync_IntoSiblingWithSharedPrefix_Moves()
    {
        // "/d" is not an ancestor of "/dd", even though the paths share a prefix.
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
        await using var other = await FileSystem.OpenAsync(RootPath, canWrite: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.MoveToAsync(other.Root));
    }

    [Fact]
    public async Task MoveToAsync_DestinationFromAnotherImplementation_ThrowsInvalidOperationException()
    {
        var file = await Root.CreateFileAsync("a.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.MoveToAsync(ForeignDirectory.Create()));
    }

    [Fact]
    public async Task MoveToAsync_MissingDestination_ThrowsDirectoryNotFoundException()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var dir = await Root.CreateDirectoryAsync("d");
        Directory.Delete(HostPath("d"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => file.MoveToAsync(dir));
        Assert.True(File.Exists(HostPath("a.txt")));
    }

    [Fact]
    public async Task MoveToAsync_MissingEntry_ThrowsNotFound()
    {
        var file = await Root.CreateFileAsync("a.txt");
        var source = await Root.CreateDirectoryAsync("src");
        var target = await Root.CreateDirectoryAsync("dst");
        File.Delete(HostPath("a.txt"));
        Directory.Delete(HostPath("src"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.MoveToAsync(target));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => source.MoveToAsync(target));
    }
}
