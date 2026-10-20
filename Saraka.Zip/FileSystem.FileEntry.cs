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
        /// <summary>
        /// A file in the archive: an entry whose name doesn't end in "/".
        /// </summary>
        private sealed class FileEntry : Entry, IFile
        {
            /// <summary>
            /// The encoding for <see cref="ReadAllTextAsync"/>. StreamReader skips a leading byte
            /// order mark only if it matches the encoding's preamble, so the encoding must have one.
            /// Byte order mark detection stays off, so a UTF-16 or UTF-32 mark never changes the
            /// decoding.
            /// </summary>
            private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

            /// <summary>
            /// Creates an entry for the file at a Saraka path.
            /// </summary>
            /// <param name="owner">The file system the file belongs to.</param>
            /// <param name="path">The file's Saraka path.</param>
            public FileEntry(FileSystem owner, string path)
                : base(owner, path)
            {
            }

            /// <inheritdoc/>
            public Task<long> GetLengthAsync(CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
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

            /// <inheritdoc/>
            public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
            {
                using (var stream = await OpenReadAsync(cancellationToken).ConfigureAwait(false))
                using (var buffer = new MemoryStream())
                {
                    await stream.CopyToAsync(buffer, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                    return buffer.ToArray();
                }
            }

            /// <inheritdoc/>
            public async Task<string> ReadAllTextAsync(CancellationToken cancellationToken = default)
            {
                using (var stream = await OpenReadAsync(cancellationToken).ConfigureAwait(false))
                using (var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false))
                {
                    // StreamReader.ReadToEndAsync has no cancellable overload in .NET Standard 2.1.
                    var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return text;
                }
            }

            /// <inheritdoc/>
            /// <remarks>
            /// On a read-only file system opened from a path, the stream has its own file handle and
            /// reads straight from the zip file. On a writable one, or one opened from a stream, it's
            /// a read-only copy of the file's contents in memory. Either way it can be read in
            /// parallel with other operations, and it stays usable after the file system is disposed.
            /// </remarks>
            public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    PrepareRead(cancellationToken);
                    if (Owner.IsReadMode && Owner._zipPath != null)
                        return OpenIndependentReadStream(Owner._zipPath, EntryName);

                    // In update mode, an entry's stream is writable and the entry can only be
                    // opened once at a time, and a stream the file system was opened from can't
                    // be shared by parallel reads, so return a read-only copy of its contents.
                    using (var stream = GetZipEntry().Open())
                    {
                        var copy = new MemoryStream();
                        stream.CopyTo(copy, CopyBufferSize);
                        return (Stream)new MemoryStream(copy.GetBuffer(), 0, (int)copy.Length, writable: false);
                    }
                });
            }

            /// <inheritdoc/>
            public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    PrepareWrite(cancellationToken);
                    var entry = GetZipEntry();
                    var stream = entry.Open();
                    stream.SetLength(0);
                    entry.LastWriteTime = DateTimeOffset.Now;
                    return stream;
                });
            }

            /// <inheritdoc/>
            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    PrepareWrite(cancellationToken);
                    GetZipEntry().Delete();
                    Owner.KeepDirectory(GetParentName(EntryName));
                });
            }

            /// <inheritdoc/>
            protected override bool Exists() => Owner.FileExists(EntryName);

            /// <inheritdoc/>
            protected override IOException NotFound() =>
                new FileNotFoundException($"The file '{Path}' does not exist.");

            /// <inheritdoc/>
            protected override FileAttributes GetAttributesCore() =>
                FromExternalAttributes(GetZipEntry().ExternalAttributes, isDirectory: false);

            /// <inheritdoc/>
            protected override void SetAttributesCore(FileAttributes attributes)
            {
                var entry = GetZipEntry();
                entry.ExternalAttributes = ToExternalAttributes(entry.ExternalAttributes, attributes, isDirectory: false);
            }

            /// <inheritdoc/>
            protected override DateTime GetLastWriteTimeCore() => FromZipTime(GetZipEntry().LastWriteTime);

            /// <inheritdoc/>
            protected override void SetLastWriteTimeCore(DateTimeOffset lastWriteTime) =>
                GetZipEntry().LastWriteTime = lastWriteTime;

            /// <summary>
            /// Gets this file's zip entry.
            /// </summary>
            /// <returns>The zip entry.</returns>
            /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
            private ZipArchiveEntry GetZipEntry() => Owner.FindFileEntry(EntryName) ?? throw NotFound();
        }
    }
}
