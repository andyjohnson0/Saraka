using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Android.Provider;

using Saraka.Abstractions;


namespace Saraka.AndroidSaf
{
    public sealed partial class FileSystem
    {
        private sealed class DirectoryEntry : Entry, IDirectory
        {
            public DirectoryEntry(FileSystem owner, DirectoryEntry? parent, string name, string documentId)
                : base(owner, parent, name, documentId)
            {
            }

            protected override bool IsDirectory => true;

            public Task<IEnumerable<IFileSystemEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    EnsureExists();
                    return (IEnumerable<IFileSystemEntry>)ListChildren(
                        files: true, directories: true, _ => true, cancellationToken);
                }, cancellationToken);
            }

            public Task<IDirectory> GetDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidateName(name, nameof(name));
                    EnsureExists();
                    var match = ListChildren(files: false, directories: true, NameIs(name), cancellationToken)
                        .FirstOrDefault();
                    return (IDirectory)(match ?? throw new DirectoryNotFoundException($"'{Path}' has no subdirectory named '{name}'."));
                }, cancellationToken);
            }

            public Task<IFile> GetFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidateName(name, nameof(name));
                    EnsureExists();
                    var match = ListChildren(files: true, directories: false, NameIs(name), cancellationToken)
                        .FirstOrDefault();
                    return (IFile)(match ?? throw new FileNotFoundException($"'{Path}' has no file named '{name}'."));
                }, cancellationToken);
            }

            public Task<IEnumerable<IFile>> EnumerateFilesAsync(string searchPattern = "*", CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidatePattern(searchPattern, nameof(searchPattern));
                    EnsureExists();
                    return (IEnumerable<IFile>)ListChildren(
                            files: true, directories: false, Matches(searchPattern), cancellationToken)
                        .Cast<IFile>()
                        .ToList();
                }, cancellationToken);
            }

            public Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidatePattern(searchPattern, nameof(searchPattern));
                    EnsureExists();
                    return (IEnumerable<IDirectory>)ListChildren(
                            files: false, directories: true, Matches(searchPattern), cancellationToken)
                        .Cast<IDirectory>()
                        .ToList();
                }, cancellationToken);
            }

            public Task<IDirectory> CreateDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync<IDirectory>(cancellationToken, () => ValidateName(name, nameof(name)));
            }

            public Task<IFile> CreateFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync<IFile>(cancellationToken, () => ValidateName(name, nameof(name)));
            }

            public Task<IFile> CreateFileAsync(string name, Stream content, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync<IFile>(cancellationToken, () =>
                {
                    ValidateName(name, nameof(name));
                    ArgumentNullException.ThrowIfNull(content);
                    if (!content.CanRead)
                        throw new ArgumentException("The content stream can't be read.", nameof(content));
                });
            }

            public Task DeleteAsync(bool recursive = false, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            protected override IOException NotFound(Exception? innerException) =>
                new DirectoryNotFoundException($"The directory '{Path}' does not exist.", innerException);

            // Lists the direct children that the options allow, in the order the options give.
            // When two children of the same kind have names that differ only in case, the first
            // is kept. That happens before matching, so lookups and listings agree on which one
            // is used.
            private List<Entry> ListChildren(
                bool files, bool directories, Func<string, bool> match, CancellationToken cancellationToken)
            {
                var collection = Owner.ListDocuments(DocumentId);
                var children = new List<Entry>();

                if (directories)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < collection.FolderCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = collection.FolderDisplayNameAt(i);
                        if (IsValidName(name) && seen.Add(name) && match(name))
                            children.Add(new DirectoryEntry(Owner, this, name, collection.FolderDocumentIdAt(i)));
                    }
                }

                if (files)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < collection.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = collection.DisplayNameAt(i);
                        if (IsValidName(name) && seen.Add(name) && match(name))
                            children.Add(new FileEntry(Owner, this, name, DocumentsContract.GetDocumentId(collection.UriAt(i))!));
                    }
                }

                return children;
            }

            private static Func<string, bool> NameIs(string name)
            {
                return candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase);
            }

            private static Func<string, bool> Matches(string searchPattern)
            {
                return name => FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase: true);
            }
        }
    }
}
