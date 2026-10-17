using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Gives each test its own in-memory document provider, and opens file systems over it.
/// </summary>
public abstract class AndroidSafTestBase : IAsyncLifetime
{
    private readonly List<FileSystem> _opened = new();

    /// <summary>
    /// The in-memory document provider for this test.
    /// </summary>
    internal FakeDocumentSource Source { get; } = new();

    /// <summary>
    /// The document ID of the provider's root folder.
    /// </summary>
    internal static string RootId => FakeDocumentSource.RootId;

    /// <inheritdoc/>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        foreach (var fileSystem in _opened)
            await fileSystem.DisposeAsync();
    }

    /// <summary>
    /// Opens a file system over <see cref="Source"/>. It's disposed when the test ends.
    /// </summary>
    /// <param name="options">The options, or null for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file system.</returns>
    internal async Task<FileSystem> OpenAsync(FileSystemOptions? options = null, CancellationToken cancellationToken = default)
    {
        var fileSystem = await FileSystem.OpenAsync(Source, options, cancellationToken);
        _opened.Add(fileSystem);
        return fileSystem;
    }

    /// <summary>
    /// Opens a file system over <see cref="Source"/> and returns its root.
    /// </summary>
    /// <param name="options">The options, or null for the defaults.</param>
    /// <returns>The root directory.</returns>
    internal async Task<IDirectory> OpenRootAsync(FileSystemOptions? options = null)
    {
        return (await OpenAsync(options)).Root;
    }

    /// <summary>
    /// Creates options that include hidden files and folders.
    /// </summary>
    /// <returns>The options.</returns>
    internal static FileSystemOptions ShowHidden() => new() { IncludeHiddenFiles = true, IncludeHiddenFolders = true };

    /// <summary>
    /// Gets the names of entries, in order.
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <returns>Their names.</returns>
    internal static string[] Names(IEnumerable<IFileSystemEntry> entries) => entries.Select(e => e.Name).ToArray();
}
