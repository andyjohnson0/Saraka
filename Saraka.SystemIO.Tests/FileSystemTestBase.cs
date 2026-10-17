using System.Reflection;
using System.Text;
using Saraka.Abstractions;

namespace Saraka.SystemIO.Tests;

/// <summary>
/// Gives each test its own empty temporary directory, opened as a writable file system.
/// </summary>
public abstract class FileSystemTestBase : IAsyncLifetime
{
    /// <summary>
    /// Creates the temporary directory for this test.
    /// </summary>
    protected FileSystemTestBase()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "Saraka.SystemIO.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>The host path of the directory that backs the file system.</summary>
    protected string RootPath { get; }

    /// <summary>
    /// The file system under test.
    /// </summary>
    protected FileSystem Fs { get; private set; } = null!;

    /// <summary>
    /// The file system's root directory.
    /// </summary>
    protected IDirectory Root => Fs.Root;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        Fs = await FileSystem.OpenAsync(RootPath, canWrite: true);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await Fs.DisposeAsync();
        DeleteDirectory(RootPath);
    }

    /// <summary>Gets the host path of a location below the root.</summary>
    protected string HostPath(params string[] segments)
    {
        return Path.Combine(new[] { RootPath }.Concat(segments).ToArray());
    }

    /// <summary>
    /// Creates a file holding UTF-8 text.
    /// </summary>
    /// <param name="directory">The directory to create it in.</param>
    /// <param name="name">The file's name.</param>
    /// <param name="content">The file's text.</param>
    /// <returns>The new file.</returns>
    protected static Task<IFile> CreateFileAsync(IDirectory directory, string name, string content)
    {
        return directory.CreateFileAsync(name, new MemoryStream(Encoding.UTF8.GetBytes(content)));
    }

    /// <summary>
    /// Opens a second, read-only file system over the same directory.
    /// </summary>
    /// <returns>The read-only file system.</returns>
    protected Task<FileSystem> OpenReadOnlyAsync()
    {
        return FileSystem.OpenAsync(RootPath, canWrite: false);
    }

    /// <summary>
    /// Deletes a directory tree, clearing read-only attributes first and ignoring failures.
    /// </summary>
    /// <param name="path">The directory to delete.</param>
    private static void DeleteDirectory(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A stream that reports it can't be read.</summary>
internal sealed class UnreadableStream : MemoryStream
{
    /// <inheritdoc/>
    public override bool CanRead => false;
}

/// <summary>A readable stream whose reads always fail.</summary>
internal sealed class FailingStream : Stream
{
    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated read failure.");

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(new IOException("Simulated read failure."));

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromException<int>(new IOException("Simulated read failure."));

    /// <inheritdoc/>
    public override void Flush() { }
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>An <see cref="IDirectory"/> from some other implementation. Every member throws.</summary>
public class ForeignDirectory : DispatchProxy
{
    /// <summary>
    /// Creates a stand-in directory from another implementation.
    /// </summary>
    /// <returns>The stand-in directory.</returns>
    public static IDirectory Create() => Create<IDirectory, ForeignDirectory>();

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new NotSupportedException();
}
