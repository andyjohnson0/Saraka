using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Tests that a document deleted after it was found gives the not-found exceptions, both from a
/// provider that reports it as missing (a null query result) and from Android's local storage
/// provider, whose tree check fails every call about a missing document with an
/// <see cref="IOException"/>.
/// </summary>
public class MissingDocumentTests : AndroidSafTestBase
{
    /// <summary>
    /// The two ways a provider can report a missing document: false for the local storage
    /// provider's failures, true for a null query result.
    /// </summary>
    public static TheoryData<bool> ProviderModes => new() { false, true };

    /// <summary>
    /// Creates "/d/a.txt" and opens a file system, setting how the provider reports missing
    /// documents.
    /// </summary>
    /// <param name="missingReturnsNull">How the provider reports a missing document.</param>
    /// <returns>The folder's ID, the file's ID, the folder and the file.</returns>
    private async Task<(string DirId, string FileId, IDirectory Dir, IFile File)> CreateAsync(bool missingReturnsNull)
    {
        Source.MissingDocumentsReturnNull = missingReturnsNull;
        var dirId = Source.AddFolder(RootId, "d");
        var fileId = Source.AddFile(dirId, "a.txt", "hello");
        var fs = await OpenAsync();
        var dir = await fs.GetDirectoryAsync("/d");
        return (dirId, fileId, dir, await dir.GetFileAsync("a.txt"));
    }

    [Theory]
    [MemberData(nameof(ProviderModes))]
    public async Task FileRemoved_EveryFileOperationThrowsFileNotFoundException(bool missingReturnsNull)
    {
        var (_, fileId, _, file) = await CreateAsync(missingReturnsNull);

        Source.Remove(fileId);

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetAttributesAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetLastWriteTimeUtcAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetLengthAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.OpenReadAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.ReadAllBytesAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.ReadAllTextAsync());
    }

    [Theory]
    [MemberData(nameof(ProviderModes))]
    public async Task FolderRemoved_EveryFolderOperationThrowsDirectoryNotFoundException(bool missingReturnsNull)
    {
        var (dirId, _, dir, _) = await CreateAsync(missingReturnsNull);

        Source.Remove(dirId);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetEntriesAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.EnumerateFilesAsync());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetFileAsync("a.txt"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => dir.GetAttributesAsync());
    }

    [Theory]
    [MemberData(nameof(ProviderModes))]
    public async Task FolderRemoved_FilesInsideItThrowFileNotFoundException(bool missingReturnsNull)
    {
        var (dirId, _, _, file) = await CreateAsync(missingReturnsNull);

        Source.Remove(dirId);

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.OpenReadAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetAttributesAsync());
    }

    [Theory]
    [MemberData(nameof(ProviderModes))]
    public async Task FileBecameAFolder_ThrowsFileNotFoundException(bool missingReturnsNull)
    {
        var (_, fileId, _, file) = await CreateAsync(missingReturnsNull);

        Source.SetIsDirectory(fileId, true);

        await Assert.ThrowsAsync<FileNotFoundException>(() => file.GetAttributesAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.OpenReadAsync());
    }

    [Fact]
    public async Task NotFoundException_NamesThePathAndKeepsTheProvidersFailure()
    {
        var (_, fileId, _, file) = await CreateAsync(missingReturnsNull: false);
        Source.Remove(fileId);

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => file.OpenReadAsync());

        Assert.Contains("/d/a.txt", ex.Message);
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task FailureOfAnExistingDocument_StaysAnIOException()
    {
        var (_, _, _, file) = await CreateAsync(missingReturnsNull: false);
        var failure = new IOException("device error");
        Source.Fail = (method, id) => method == nameof(IDocumentSource.OpenRead) ? failure : null;

        var ex = await Assert.ThrowsAsync<IOException>(() => file.OpenReadAsync());

        Assert.Same(failure, ex);
    }

    [Fact]
    public async Task FailureWhenTheParentCantBeChecked_StaysAnIOException()
    {
        var (_, _, _, file) = await CreateAsync(missingReturnsNull: false);
        var failure = new IOException("device error");
        Source.Fail = (method, id) => failure;

        var ex = await Assert.ThrowsAsync<IOException>(() => file.OpenReadAsync());

        Assert.Same(failure, ex);
    }

    [Fact]
    public async Task ParentIsCheckedOnlyAfterAFailure()
    {
        var (_, fileId, _, file) = await CreateAsync(missingReturnsNull: false);
        var reads = Source.ListCount;

        using (await file.OpenReadAsync())
        {
        }
        Assert.Equal(reads, Source.ListCount);

        Source.Remove(fileId);
        await Assert.ThrowsAsync<FileNotFoundException>(() => file.OpenReadAsync());
        Assert.Equal(reads + 1, Source.ListCount);
    }
}
