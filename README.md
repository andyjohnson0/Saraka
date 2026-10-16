# Saraka

Saraka provides a set of async interfaces for browsing and editing an abstract file system.

The `Saraka.Abstractions` package defines the interfaces only. Implementations decide what the
file system is backed by and how it is opened. `Saraka.SystemIO` is a reference implementation
over a local filesystem directory hierarchy, `Saraka.Zip` presents a zip file as a file system, and
`Saraka.AndroidSaf` presents a folder from the Android Storage Access Framework as a read-only file
system.

Saraka means "directory" or "index" in Swahili.

## Packages

| Package               | Target framework  | Version | Contents |
|-----------------------|-------------------|---------|----------|
| `Saraka.Abstractions` | .NET Standard 2.1 | 0.1.0   | The interfaces. |
| `Saraka.SystemIO`     | .NET Standard 2.1 | 0.1.0   | An implementation backed by a local directory, using System.IO. |
| `Saraka.Zip`          | .NET Standard 2.1 | 0.1.0   | An implementation backed by a zip file, using System.IO.Compression. |
| `Saraka.AndroidSaf`   | .NET 10 for Android | 0.1.0   | A read-only implementation backed by an Android Storage Access Framework document tree. |

`Saraka.AndroidSaf` needs Android 13 (API level 33) or later.

The solution also contains `Saraka.SystemIO.Tests`, `Saraka.Zip.Tests` and
`Saraka.AndroidSaf.Tests`, xUnit test suites for the three implementations that target .NET 10.
See [Testing](#testing).

## Interfaces

All interfaces live in the `Saraka.Abstractions` namespace. Every operation is asynchronous and
takes an optional `CancellationToken`.

### `IFileSystem : IAsyncDisposable`

An open file system. Everything else is reached from its root directory.

- `Root`: the root `IDirectory`. Its `Path` is `/`, its `Name` is the empty string and its
  `Parent` is `null`.
- `CanWrite`: `true` if the file system can be written to. This can change while the file system
  is open, so check it when you need it, not once up front. Write operations attempted while it is
  `false` throw `InvalidOperationException`.

Always dispose the file system with `await using` or `DisposeAsync` when you've finished with
it. Some implementations, such as `Saraka.Zip`, only write changes to storage at that point. After
that, operations on it and its entries throw `ObjectDisposedException`. Whether streams that are
already open stay usable depends on the implementation.

### `IFileSystemEntry`

The base for anything in the file system. Each entry is either an `IDirectory` or an `IFile`;
tell them apart with a type check or pattern match.

- `Name`: the entry's name, without any path. The root's name is the empty string.
- `Path`: the full path within the file system, rooted at `/` (for example `/data/file.txt`). The
  root directory's path is `/`.
- `Parent`: the containing directory, or `null` for the root. Implementations may return a new
  instance each time, so compare entries by `Path`, not by reference.
- `GetAttributesAsync` / `SetAttributesAsync`: read or set the `FileAttributes`.
- `GetCreationTimeUtcAsync` / `SetCreationTimeUtcAsync`
- `GetLastWriteTimeUtcAsync` / `SetLastWriteTimeUtcAsync`
- `RenameAsync(newName)`: renames the entry within its current directory.
- `MoveToAsync(destination)`: moves the entry to another directory, keeping its name.

### `IDirectory : IFileSystemEntry`

- `GetEntriesAsync()`: lists the files and subdirectories directly inside this directory.
- `GetDirectoryAsync(name)` / `GetFileAsync(name)`: get a direct child by name.
- `EnumerateFilesAsync(searchPattern = "*")` / `EnumerateDirectoriesAsync(searchPattern = "*")`:
  list direct children whose names match a pattern.
- `CreateDirectoryAsync(name)`: creates an empty subdirectory.
- `CreateFileAsync(name)`: creates an empty file.
- `CreateFileAsync(name, Stream content)`: creates a file, copying `content` from its current
  position to its end. If the copy fails or is cancelled, implementations should delete the partly
  written file.
- `DeleteAsync(recursive = false)`: deletes the directory. Deleting a non-empty directory fails
  unless `recursive` is `true`.

`CreateDirectoryAsync` and the `CreateFileAsync` overloads fail if an entry with that name already
exists. They never overwrite or truncate an existing file.

Listings return entries in no particular order and include every entry, hidden and system entries
included. In a search pattern, `*` matches zero or more characters and `?` matches exactly one.
Whether matching is case-sensitive depends on the file system.

### `IFile : IFileSystemEntry`

- `GetLengthAsync()`: the file's size in bytes.
- `ReadAllBytesAsync()` / `ReadAllTextAsync()`: read the whole contents, as bytes or as UTF-8 text.
  `ReadAllTextAsync` skips a leading UTF-8 byte order mark.
- `OpenReadAsync()`: opens a read-only stream, so large files can be read a piece at a time.
- `OpenWriteAsync()`: opens a writable stream, truncating any existing content.
- `DeleteAsync()`: deletes the file.

The caller is responsible for disposing streams returned by `OpenReadAsync` and `OpenWriteAsync`.

## Behaviour

**Paths and names.** Paths always use `/` as the separator, whatever the host operating system.
Methods take bare names, never paths. A name is invalid if it is empty, contains `/`, is `.` or
`..`, or is otherwise not allowed by the file system. For example, an implementation backed by
Windows storage rejects `\` and other characters Windows doesn't allow in file names.

**Exceptions.** Exceptions are reported through the returned task. Implementations follow these
conventions:

| Exception | When |
|-----------|------|
| `ArgumentNullException` | A required argument is null. |
| `ArgumentException` | A name passed to `RenameAsync`, `GetFileAsync`, `GetDirectoryAsync`, `CreateFileAsync` or `CreateDirectoryAsync` is invalid (see above); a search pattern contains `/` or is otherwise invalid; or the `content` stream passed to `CreateFileAsync` can't be read. |
| `InvalidOperationException` | A write operation (create, delete, rename, move, set attributes or times, `OpenWriteAsync`) is attempted on a file system that is not writable. |
| `InvalidOperationException` | The operation is otherwise invalid: renaming, moving or deleting the root directory; creating, renaming or moving to a name that is already taken; moving a directory into itself or one of its descendants; moving into a directory from a different file system; deleting a non-empty directory without `recursive`. |
| `FileNotFoundException` / `DirectoryNotFoundException` | `GetFileAsync` / `GetDirectoryAsync` finds no entry with that name, or the file or directory an entry refers to no longer exists. |
| `ObjectDisposedException` | The file system has been disposed. |
| `OperationCanceledException` | The operation was cancelled. |
| `ArgumentOutOfRangeException` | A time passed to `SetCreationTimeUtcAsync` or `SetLastWriteTimeUtcAsync` is outside the range the file system can store. |
| `IOException` / `UnauthorizedAccessException` | The underlying storage failed or denied access. |

**Entries are locations, not handles.** An entry doesn't lock anything. Other code can change or
remove the file or directory it refers to, and later operations on it then throw
`FileNotFoundException` or `DirectoryNotFoundException`.

**Rename and move update the instance in place.** After a successful `RenameAsync` or
`MoveToAsync`, that instance's `Name`, `Path` and `Parent` show the new location. Any other
`IFileSystemEntry` obtained separately for the old path is not updated. Renaming an entry to its
current name, or moving it into the directory it's already in, succeeds and does nothing.

**Times.** Times are UTC. Values returned have `DateTimeKind.Utc`. When you set a time, a value
with `DateTimeKind.Unspecified` is treated as UTC and a `DateTimeKind.Local` value is converted.
Creating a file or writing to it updates the last-write time automatically.
`SetLastWriteTimeUtcAsync` is for setting a specific value.

**What a file system can store.** Not every file system can store every value. An implementation
may store times with less precision or a smaller range, store only some attributes, or ignore
values it can't store at all.
- File systems that don't record creation times may ignore `SetCreationTimeUtcAsync`.
  `GetCreationTimeUtcAsync` then returns the closest value they have.
- `SetAttributesAsync` ignores attributes the file system can't store.
- Each implementation's section below says what it does.

## Example

How you obtain the root `IDirectory` depends on the implementation. Once you have it:

```csharp
using Saraka.Abstractions;

async Task DumpAsync(IDirectory dir, CancellationToken ct = default)
{
    foreach (var entry in await dir.GetEntriesAsync(ct))
    {
        switch (entry)
        {
            case IDirectory sub:
                Console.WriteLine($"[dir]  {sub.Path}");
                await DumpAsync(sub, ct);
                break;
            case IFile file:
                Console.WriteLine($"[file] {file.Path} ({await file.GetLengthAsync(ct)} bytes)");
                break;
        }
    }
}

// Writing (the file system must be writable):
var data = await root.CreateDirectoryAsync("data");
using (var content = new MemoryStream(Encoding.UTF8.GetBytes("hello")))
{
    var file = await data.CreateFileAsync("hello.txt", content);
    await file.RenameAsync("greeting.txt");   // file.Path is now /data/greeting.txt
}
```

## `Saraka.SystemIO`

`Saraka.SystemIO.FileSystem` implements the interfaces over a directory on the local machine. Open
it with `OpenAsync`, giving the directory to use as the root:

```csharp
using Saraka.SystemIO;

await using var fs = await FileSystem.OpenAsync(@"C:\Photos", canWrite: true);
await DumpAsync(fs.Root);
```

- The directory must already exist; otherwise `OpenAsync` throws `DirectoryNotFoundException`. A
  relative path is resolved against the current directory.
- `canWrite: false` opens the file system read-only. `CanWrite` is fixed at the value passed to
  `OpenAsync`.
- Saraka paths use `/`. They're mapped to the host's own separator, so `/data/file.txt` is
  `C:\Photos\data\file.txt` on Windows. Entries outside the root directory can't be reached.
- Names may not contain `\` or `/` on any platform, nor any character the host OS doesn't allow
  in file names.
- Search patterns use simple matching, where `*` and `?` mean only what the contract says. That
  avoids the Windows quirk where `*.htm` also matches `.html` files. Case sensitivity follows the
  host file system.
- Metadata operations, such as listings, attributes, times, create, delete and move, run
  synchronously and return completed tasks. Stream reads, writes and copies are truly asynchronous.
- Instances aren't thread-safe.

Some behaviour depends on the host platform:

- On case-insensitive file systems, an entry looked up as `Foo` has the path `/Foo` even if the
  name on disk is `foo`.
- On Windows, deleting a read-only file, or recursively deleting a directory that contains one,
  throws `UnauthorizedAccessException`.
- Linux and Android don't reliably support setting the creation time.
- Windows silently drops trailing dots and spaces from names, so `a.` and `a` refer to the same
  file.
- Symbolic links and junctions inside the root are followed, even if they point outside it.
- `ReadAllTextAsync` only checks for cancellation before and after reading, because .NET Standard
  2.1 has no cancellable `ReadToEndAsync`.

## `Saraka.Zip`

`Saraka.Zip.FileSystem` presents a zip file as a file system, using the zip support in
System.IO.Compression. Open it with `OpenAsync`:

```csharp
using Saraka.Zip;

await using (var zip = await FileSystem.OpenAsync(@"C:\Photos\2026.zip", canWrite: true))
{
    var data = await zip.Root.CreateDirectoryAsync("data");
    await data.CreateFileAsync("notes.txt");
}   // Changes are written to the zip file here.
```

- `canWrite: true` opens the zip file for updating and creates it if it doesn't exist. While it's
  open, the file is locked against all other access. `canWrite: false` opens an existing zip file
  read-only and allows other readers.
- Changes are kept in memory and written to the zip file when the file system is disposed. If it
  isn't disposed, the changes are lost.
- Opening something that isn't a zip file throws `InvalidDataException`.

**How entries map to paths.** The entry `data/notes.txt` is the file `/data/notes.txt`.
- A directory exists if it has its own entry (`data/`) or if any entry is inside it.
- Creating a directory adds an entry for it.
- Removing the last thing from a directory adds an entry for that directory, so it doesn't
  disappear.
- Names are case-sensitive, so `a.txt` and `A.txt` are different files.
- `ArgumentException` is thrown for a name that is empty, `.` or `..`, or contains `/`, `\` or a
  control character. Other characters are allowed, including ones Windows doesn't allow in file
  names, such as `:` and `?`. A zip file made that way may not extract cleanly on Windows.
- Entries whose names can't be Saraka paths are ignored. These are names that start with `/`,
  contain `.`, `..` or empty segments, or contain `\`.
- If a zip file holds the same name twice, only the first entry is used.
- If a zip file holds both a file `a` and entries inside a directory `a/`, both appear in
  listings. `GetFileAsync("a")` finds the file and `GetDirectoryAsync("a")` finds the directory.

**Where zip falls short of the contract.**
- *Creation time.* Zip records only a last-write time. `GetCreationTimeUtcAsync` returns the
  last-write time, and `SetCreationTimeUtcAsync` does nothing.
- *Time precision and range.* Times are stored in MS-DOS format: local time, in two-second steps,
  from 1980 to 2107. A time read back after reopening can be up to two seconds earlier than the
  one set. Setting a last-write time outside that range throws `ArgumentOutOfRangeException`.
- *Directory times and attributes.* A directory without its own entry reports the latest time of
  anything inside it. Setting its time or attributes gives it an entry. The root has no entry, so
  setting its time or attributes does nothing.
- *Attributes.* Only the MS-DOS attributes (read-only, hidden, system, archive) are stored. Unix
  permission bits already in an entry are preserved.
- *Rename and move.* Zip has no rename, so these copy every affected entry to its new name and
  delete the original. That costs time in proportion to the size of the data.
- *Streams.* On a read-only file system, `OpenReadAsync` streams straight from the zip file. On a
  writable one, it returns a copy of the file's contents held in memory.
- *Concurrent streams.* On a writable file system, while a file's write stream is open, anything
  else that reads that file's data throws `IOException`. That includes reading it, opening it
  again, renaming it and moving it.
- *Disposal.* Streams still open when the file system is disposed can no longer be used.
- *Asynchrony.* The methods are async, but most of the work behind them is synchronous. .NET
  Standard 2.1 only exposes the synchronous zip APIs. So listing, lookup, attributes, times,
  creating, deleting, renaming and moving all run synchronously and return completed tasks.
  - Copying data to and from streams is truly asynchronous.
  - On .NET 10 and later, disposal uses the archive's asynchronous `DisposeAsync`, so saving
    changes is asynchronous too.
  - Renaming or moving large files, and reading through `OpenReadAsync` on a writable file
    system, can block the calling thread. To avoid that on a UI thread, call them through
    `Task.Run`.

## `Saraka.AndroidSaf`

`Saraka.AndroidSaf.FileSystem` presents a document tree from the Android Storage Access Framework
(SAF) as a read-only file system. Open it with the tree URI returned by the system folder picker
(`Intent.ActionOpenDocumentTree`). The app must hold permission to read the tree.

```csharp
using Saraka.AndroidSaf;

var options = new FileSystemOptions
{
    FileTypes = new[] { "jpeg", "png" },
    SortField = SortField.FileDate,
    SortOrder = SortOrder.Descending,
};
await using var fs = await FileSystem.OpenAsync(context, treeUri, options);
await DumpAsync(fs.Root);
```

- The tree's top-level folder is the root directory. Paths are built from the documents' display
  names, so the document `DCIM/Camera/img1.jpg` in the tree is `/DCIM/Camera/img1.jpg`.
- `CanWrite` is always `false`. Write operations throw `InvalidOperationException`.
- `OpenAsync` throws `ArgumentException` if the URI isn't a tree URI, `DirectoryNotFoundException`
  if the tree's folder doesn't exist, and `UnauthorizedAccessException` if the app can't read it.
- Calls to the document provider block, so every operation runs on the thread pool. That keeps
  them off the UI thread.

**Options.** `FileSystemOptions` is read once, when the file system is opened.
- `SortField` and `SortOrder` set the order of listings: by name (ignoring case) or by
  last-modified time, ascending or descending. Directories are listed before files.
- `FileTypes` limits files to the given extensions, without the period (for example `"jpeg"`).
  Matching ignores case, and `"jpeg"` also matches `.jpg` files. `null`, the default, includes
  every file. Directories aren't affected.
- `IncludeHiddenFiles` and `IncludeHiddenFolders` include entries whose names start with a
  period. Both are `false` by default.

**Where the SAF falls short of the contract.**
- *Filtered listings.* The contract says listings include every entry. Here, entries the options
  exclude don't appear in listings, and `GetFileAsync` and `GetDirectoryAsync` can't find them. By
  default that includes hidden files and directories.
- *Names.* Names are matched case-insensitively, in lookups and in search patterns. If a directory
  has two files, or two directories, whose names differ only in case, only the first in listing
  order is used. Entries whose display names can't be Saraka names, such as names containing `/`,
  are ignored.
- *Entries and document IDs.* An entry refers to its document by the provider's document ID. For
  the device's own storage, the ID is derived from the document's location, so an entry behaves as
  the contract says. Other providers, such as cloud storage, may keep the same ID when a document
  is renamed or moved, and an entry then goes on referring to that document at its new location.
- *Times.* The SAF records only a last-modified time. `GetCreationTimeUtcAsync` returns the
  last-modified time. A document whose provider doesn't record a time reports the Unix epoch
  (1970-01-01).
- *Attributes.* An entry is `Directory` or `Normal`, plus `Hidden` if its name starts with a
  period.
- *Length.* If the provider doesn't report a file's size, `GetLengthAsync` reads the whole file to
  find it.
- *Errors.* Android and Java exceptions are always wrapped in .NET exceptions, with the original
  as the inner exception. That applies to operations and to reading streams returned by
  `OpenReadAsync`.
  - An Android `SecurityException`, for example when permission has been revoked, becomes
    `UnauthorizedAccessException`.
  - A missing document gives `FileNotFoundException` or `DirectoryNotFoundException`.
  - A cancellation by the provider gives `OperationCanceledException`.
  - Any other provider failure gives `IOException`.
- *Disposal.* Streams that are open when the file system is disposed stay usable.
- Instances aren't thread-safe.

## Building

```bash
dotnet build Saraka.slnx
```

The `.slnx` solution format needs the .NET 9 SDK (9.0.200 or later) or Visual Studio 2022 17.13
or later. The test projects target .NET 10, so building the whole solution needs the .NET 10 SDK.
`Saraka.AndroidSaf` also needs the .NET `android` workload (`dotnet workload install android`).

## Testing

`Saraka.SystemIO.Tests` and `Saraka.Zip.Tests` hold the unit tests for the SystemIO and Zip
implementations. They use xUnit and run against real temporary directories and zip files.

`Saraka.AndroidSaf.Tests` runs on Windows, not Android. All of `Saraka.AndroidSaf` except its
`Platform` folder is plain C#, and the test project compiles those files in directly. Instead of the
content resolver, the tests use an in-memory document provider. The code in `Platform`, which calls
the Android document APIs, isn't covered by these tests.

Run all the tests with:

```bash
dotnet test Saraka.slnx
```

## Author

**Andrew Johnson** ([andy@andyjohnson.uk](mailto:andy@andyjohnson.uk), [andyjohnson.uk](https://andyjohnson.uk))

Parts of this solution, and in particular the tests, were implemented using Claude Code.

## Licence

Saraka is released under the [MIT Licence](LICENSE.md).
