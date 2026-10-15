using System.Reflection;
using System.Text;
using Saraka.Abstractions;

namespace Saraka.SystemIO.Tests;

/// <summary>
/// Gives each test its own empty temporary directory, opened as a writable file system.
/// </summary>
public abstract class FileSystemTestBase : IAsyncLifetime
{
    protected FileSystemTestBase()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "Saraka.SystemIO.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>The host path of the directory that backs the file system.</summary>
    protected string RootPath { get; }

    protected FileSystem Fs { get; private set; } = null!;

    protected IDirectory Root => Fs.Root;

    public async Task InitializeAsync()
    {
        Fs = await FileSystem.OpenAsync(RootPath, canWrite: true);
    }

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

    protected static Task<IFile> CreateFileAsync(IDirectory directory, string name, string content)
    {
        return directory.CreateFileAsync(name, new MemoryStream(Encoding.UTF8.GetBytes(content)));
    }

    protected Task<FileSystem> OpenReadOnlyAsync()
    {
        return FileSystem.OpenAsync(RootPath, canWrite: false);
    }

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
    public override bool CanRead => false;
}

/// <summary>A readable stream whose reads always fail.</summary>
internal sealed class FailingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated read failure.");

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(new IOException("Simulated read failure."));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromException<int>(new IOException("Simulated read failure."));

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>An <see cref="IDirectory"/> from some other implementation. Every member throws.</summary>
public class ForeignDirectory : DispatchProxy
{
    public static IDirectory Create() => Create<IDirectory, ForeignDirectory>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new NotSupportedException();
}
