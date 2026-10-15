# Saraka

Saraka provides a small set of async interfaces for browsing and editing a file system.

The `Saraka.Abstractions` package defines the interfaces only. Implementations decide what the
file system is backed by and how it is opened.

Saraka means "directory" or "index" in Swahili.

## Package

| Package               | Target framework  | Version |
|-----------------------|-------------------|---------|
| `Saraka.Abstractions` | .NET Standard 2.1 | 0.1.0   |

Because it targets .NET Standard 2.1, it works on .NET Core 3.0+ and .NET 5 and later. It does not
support .NET Framework, which only implements .NET Standard up to 2.0.

## Interfaces

All interfaces live in the `Saraka.Abstractions` namespace. Every operation is asynchronous and
takes an optional `CancellationToken`.

### `IFileSystem : IAsyncDisposable`

An open file system. Everything else is reached from its root directory.

- `Root`: the root `IDirectory`. Its `Path` is `\` and its `Parent` is `null`.
- `CanWrite`: `true` if the file system can be written to. This can change while the file system
  is open, so check it when you need it, not once up front. Write operations attempted while it is
  `false` throw `InvalidOperationException`.

Dispose the file system with `await using` or `DisposeAsync` when you've finished with it.

### `IFileSystemEntry`

The base for anything in the file system. Each entry is either an `IDirectory` or an `IFile`;
tell them apart with a type check or pattern match.

- `Name`: the entry's name, without any path.
- `Path`: the full path within the file system, rooted at `\` (for example `\data\file.txt`). The
  root directory's path is `\`.
- `Parent`: the containing directory, or `null` for the root.
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
  position to its end.
- `DeleteAsync(recursive = false)`: deletes the directory. Deleting a non-empty directory fails
  unless `recursive` is `true`.

`CreateDirectoryAsync` and the `CreateFileAsync` overloads fail if an entry with that name already
exists. They never overwrite or truncate an existing file.

### `IFile : IFileSystemEntry`

- `GetLengthAsync()`: the file's size in bytes.
- `ReadAllBytesAsync()` / `ReadAllTextAsync()`: read the whole contents, as bytes or as UTF-8 text.
- `OpenReadAsync()`: opens a read-only stream, so large files can be read a piece at a time.
- `OpenWriteAsync()`: opens a writable stream, truncating any existing content.
- `DeleteAsync()`: deletes the file.

The caller is responsible for disposing streams returned by `OpenReadAsync` and `OpenWriteAsync`.

## Behaviour

**Exceptions.** Implementations follow these conventions:

| Exception | When |
|-----------|------|
| `ArgumentNullException` | A required argument is null. |
| `ArgumentException` | A name passed to `RenameAsync`, `GetFileAsync`, `GetDirectoryAsync`, `CreateFileAsync` or `CreateDirectoryAsync` is empty or contains a path separator. |
| `InvalidOperationException` | A write operation (create, delete, rename, move, set attributes or times, `OpenWriteAsync`) is attempted on a file system that is not writable. |
| `InvalidOperationException` | The operation is otherwise invalid: renaming, moving or deleting the root directory; creating, renaming or moving to a name that is already taken; moving a directory into itself or one of its descendants; moving into a directory from a different file system; deleting a non-empty directory without `recursive`. |
| `FileNotFoundException` / `DirectoryNotFoundException` | `GetFileAsync` / `GetDirectoryAsync` finds no entry with that name. |

**Rename and move update the instance in place.** After a successful `RenameAsync` or
`MoveToAsync`, that instance's `Name`, `Path` and `Parent` show the new location. Any other
`IFileSystemEntry` obtained separately for the old path is not updated.

**Last-write time.** Creating a file or writing to it updates the last-write time automatically.
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
    await file.RenameAsync("greeting.txt");   // file.Path is now \data\greeting.txt
}
```

## Building

```bash
dotnet build Saraka.slnx
```

The `.slnx` solution format needs the .NET 9 SDK (9.0.200 or later) or Visual Studio 2022 17.13
or later.

## Author

**Andrew Johnson** ([andy@andyjohnson.uk](mailto:andy@andyjohnson.uk), [andyjohnson.uk](https://andyjohnson.uk))

## Licence

Saraka is released under the [MIT Licence](LICENSE).
