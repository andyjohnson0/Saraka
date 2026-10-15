# Saraka

Saraka provides a small set of async interfaces for browsing and editing a file system.

The `Saraka.Abstractions` package defines the interfaces only. Implementations decide what the
file system is backed by and how it is opened. `Saraka.SystemIO` is a reference implementation
over a local directory.

Saraka means "directory" or "index" in Swahili.

## Packages

| Package               | Target framework  | Version | Contents |
|-----------------------|-------------------|---------|----------|
| `Saraka.Abstractions` | .NET Standard 2.1 | 0.1.0   | The interfaces. |
| `Saraka.SystemIO`     | .NET Standard 2.1 | 0.1.0   | An implementation backed by a local directory, using System.IO. |

Because they target .NET Standard 2.1, they work on .NET Core 3.0+ and .NET 5 and later. They do
not support .NET Framework, which only implements .NET Standard up to 2.0.

The solution also contains `Saraka.SystemIO.Tests`, an xUnit test suite for `Saraka.SystemIO`
that targets .NET 10. See [Testing](#testing).

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

Dispose the file system with `await using` or `DisposeAsync` when you've finished with it. After
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

## Building

```bash
dotnet build Saraka.slnx
```

The `.slnx` solution format needs the .NET 9 SDK (9.0.200 or later) or Visual Studio 2022 17.13
or later. The test project targets .NET 10, so building the whole solution needs the .NET 10 SDK.

## Testing

The `Saraka.SystemIO.Tests` project holds the unit tests for `Saraka.SystemIO`. They use xUnit
and run against real temporary directories. Run them with:

```bash
dotnet test Saraka.slnx
```

## Author

**Andrew Johnson** ([andy@andyjohnson.uk](mailto:andy@andyjohnson.uk), [andyjohnson.uk](https://andyjohnson.uk))

## Licence

Saraka is released under the [MIT Licence](LICENSE).
