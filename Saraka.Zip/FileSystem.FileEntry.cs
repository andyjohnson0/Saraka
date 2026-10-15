using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.Zip
{
    public sealed partial class FileSystem
    {
        private sealed class FileEntry : Entry, IFile
        {
            // StreamReader skips a leading byte order mark only if it matches this encoding's
            // preamble, so the encoding must have one. Byte order mark detection stays off, so a
            // UTF-16 or UTF-32 mark never changes the decoding.
            private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

            public FileEntry(FileSystem owner, string path)
                : base(owner, path)
            {
            }

            public Task<long> GetLengthAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    var entry = GetZipEntry();
                    try
                    {
                        return entry.Length;
                    }
                    catch (InvalidOperationException)
                    {
                        // In update mode, Length isn't available once an entry has been opened.
                        using (var stream = entry.Open())
                        {
                            return stream.Length;
                        }
                    }
                });
            }

            public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
            {
                PrepareRead(cancellationToken);
                using (var stream = GetZipEntry().Open())
                using (var buffer = new MemoryStream())
                {
                    await stream.CopyToAsync(buffer, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                    return buffer.ToArray();
                }
            }

            public async Task<string> ReadAllTextAsync(CancellationToken cancellationToken = default)
            {
                PrepareRead(cancellationToken);
                using (var stream = GetZipEntry().Open())
                using (var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false))
                {
                    // StreamReader.ReadToEndAsync has no cancellable overload in .NET Standard 2.1.
                    var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return text;
                }
            }

            public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    var entry = GetZipEntry();
                    if (Owner.IsReadMode)
                        return entry.Open();

                    // In update mode, an entry's stream is writable and the entry can only be
                    // opened once at a time, so return a read-only copy of its contents.
                    using (var stream = entry.Open())
                    {
                        var copy = new MemoryStream();
                        stream.CopyTo(copy, CopyBufferSize);
                        return (Stream)new MemoryStream(copy.GetBuffer(), 0, (int)copy.Length, writable: false);
                    }
                });
            }

            public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    var entry = GetZipEntry();
                    var stream = entry.Open();
                    stream.SetLength(0);
                    entry.LastWriteTime = DateTimeOffset.Now;
                    return stream;
                });
            }

            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    GetZipEntry().Delete();
                    Owner.KeepDirectory(GetParentName(EntryName));
                });
            }

            protected override bool Exists() => Owner.FileExists(EntryName);

            protected override IOException NotFound() =>
                new FileNotFoundException($"The file '{Path}' does not exist.");

            protected override FileAttributes GetAttributesCore() =>
                FromExternalAttributes(GetZipEntry().ExternalAttributes, isDirectory: false);

            protected override void SetAttributesCore(FileAttributes attributes)
            {
                var entry = GetZipEntry();
                entry.ExternalAttributes = ToExternalAttributes(entry.ExternalAttributes, attributes, isDirectory: false);
            }

            protected override DateTime GetLastWriteTimeCore() => FromZipTime(GetZipEntry().LastWriteTime);

            protected override void SetLastWriteTimeCore(DateTimeOffset lastWriteTime) =>
                GetZipEntry().LastWriteTime = lastWriteTime;

            private ZipArchiveEntry GetZipEntry() => Owner.FindFileEntry(EntryName);
        }
    }
}
