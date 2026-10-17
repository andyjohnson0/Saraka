using Saraka.Abstractions;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// Gives each test its own in-memory document provider, and opens file systems over it.
/// </summary>
public abstract class AndroidSafTestBase : IAsyncLifetime
{
    private readonly List<FileSystem> _opened = new();

    internal FakeDocumentSource Source { get; } = new();

    internal static string RootId => FakeDocumentSource.RootId;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var fileSystem in _opened)
            await fileSystem.DisposeAsync();
    }

    internal async Task<FileSystem> OpenAsync(FileSystemOptions? options = null, CancellationToken cancellationToken = default)
    {
        var fileSystem = await FileSystem.OpenAsync(Source, options, cancellationToken);
        _opened.Add(fileSystem);
        return fileSystem;
    }

    internal async Task<IDirectory> OpenRootAsync(FileSystemOptions? options = null)
    {
        return (await OpenAsync(options)).Root;
    }

    internal static FileSystemOptions ShowHidden() => new() { IncludeHiddenFiles = true, IncludeHiddenFolders = true };

    internal static string[] Names(IEnumerable<IFileSystemEntry> entries) => entries.Select(e => e.Name).ToArray();
}
