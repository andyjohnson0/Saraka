using System.IO.Compression;
using System.Reflection;
using System.Text;
using Saraka.Abstractions;

namespace Saraka.Zip.Tests;

/// <summary>
/// Gives each test its own temporary folder holding test.zip, opened as a writable file system.
/// The zip file doesn't exist until the file system creates it.
/// </summary>
public abstract class ZipTestBase : IAsyncLifetime
{
    /// <summary>
    /// Creates the temporary folder for this test. The zip file is created when the file system opens it.
    /// </summary>
    protected ZipTestBase()
    {
        TempPath = Path.Combine(Path.GetTempPath(), "Saraka.Zip.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempPath);
        ZipPath = Path.Combine(TempPath, "test.zip");
    }

    /// <summary>
    /// The temporary folder that holds the zip file.
    /// </summary>
    protected string TempPath { get; }

    /// <summary>
    /// The path of the zip file.
    /// </summary>
    protected string ZipPath { get; }

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
        Fs = await FileSystem.OpenAsync(ZipPath, canWrite: true);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await Fs.DisposeAsync();
        try
        {
            Directory.Delete(TempPath, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Saves changes by disposing the file system, then opens the zip file again.</summary>
    protected async Task ReopenAsync(bool canWrite = true)
    {
        await Fs.DisposeAsync();
        Fs = await FileSystem.OpenAsync(ZipPath, canWrite);
    }

    /// <summary>
    /// Replaces the zip file with one holding exactly the given entries, written directly with
    /// System.IO.Compression, then opens it. A null content writes no data.
    /// </summary>
    protected async Task UseZipAsync(params (string Name, string? Content)[] entries)
    {
        await Fs.DisposeAsync();
        File.Delete(ZipPath);
        using (var archive = ZipFile.Open(ZipPath, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                if (content != null)
                {
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(content);
                }
            }
        }
        Fs = await FileSystem.OpenAsync(ZipPath, canWrite: true);
    }

    /// <summary>
    /// Saves changes, reads every entry in the zip file directly, then opens it again.
    /// Directory entries map to null.
    /// </summary>
    protected async Task<Dictionary<string, string?>> ReadZipAsync()
    {
        await Fs.DisposeAsync();
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        using (var archive = ZipFile.OpenRead(ZipPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    result[entry.FullName] = null;
                    continue;
                }
                using var reader = new StreamReader(entry.Open());
                result[entry.FullName] = reader.ReadToEnd();
            }
        }
        Fs = await FileSystem.OpenAsync(ZipPath, canWrite: true);
        return result;
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
