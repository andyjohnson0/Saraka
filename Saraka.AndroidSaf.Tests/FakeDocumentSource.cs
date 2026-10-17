using System.Text;

namespace Saraka.AndroidSaf.Tests;

/// <summary>
/// An in-memory document provider. Documents get IDs that never change, so it behaves like a
/// provider whose IDs survive a rename. Children are listed in the order they were added, which
/// stands in for the provider's own order.
/// </summary>
internal sealed class FakeDocumentSource : IDocumentSource
{
    public const string RootId = "root";

    private readonly Dictionary<string, Node> _nodes = new();
    private int _nextId;

    /// <summary>
    /// Creates a provider holding only an empty root folder.
    /// </summary>
    public FakeDocumentSource()
    {
        _nodes[RootId] = new Node(RootId, parentId: null, string.Empty, isDirectory: true, lastModified: 0);
    }

    /// <inheritdoc/>
    public string RootDocumentId => RootId;

    /// <summary>
    /// Called with the method name and document ID before each call. A non-null result is thrown.
    /// </summary>
    public Func<string, string, Exception?>? Fail { get; set; }

    /// <summary>
    /// If set, streams from <see cref="OpenRead"/> throw this when they are read.
    /// </summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>
    /// The number of streams opened by <see cref="OpenRead"/>.
    /// </summary>
    public int OpenReadCount { get; private set; }

    /// <summary>
    /// Adds a folder.
    /// </summary>
    /// <param name="parentId">The parent folder's ID.</param>
    /// <param name="name">The display name.</param>
    /// <param name="lastModified">The last-modified time, or null if unknown.</param>
    /// <returns>The new folder's ID.</returns>
    public string AddFolder(string parentId, string name, long? lastModified = 0)
    {
        return Add(parentId, name, isDirectory: true, lastModified, content: Array.Empty<byte>(), reportSize: true);
    }

    /// <summary>
    /// Adds a file holding UTF-8 text.
    /// </summary>
    /// <param name="parentId">The parent folder's ID.</param>
    /// <param name="name">The display name.</param>
    /// <param name="content">The file's text.</param>
    /// <param name="lastModified">The last-modified time, or null if unknown.</param>
    /// <param name="reportSize">False if the provider doesn't report the size.</param>
    /// <returns>The new file's ID.</returns>
    public string AddFile(string parentId, string name, string content = "", long? lastModified = 0, bool reportSize = true)
    {
        return AddFile(parentId, name, Encoding.UTF8.GetBytes(content), lastModified, reportSize);
    }

    /// <summary>
    /// Adds a file holding bytes.
    /// </summary>
    /// <param name="parentId">The parent folder's ID.</param>
    /// <param name="name">The display name.</param>
    /// <param name="content">The file's bytes.</param>
    /// <param name="lastModified">The last-modified time, or null if unknown.</param>
    /// <param name="reportSize">False if the provider doesn't report the size.</param>
    /// <returns>The new file's ID.</returns>
    public string AddFile(string parentId, string name, byte[] content, long? lastModified = 0, bool reportSize = true)
    {
        return Add(parentId, name, isDirectory: false, lastModified, content, reportSize);
    }

    /// <summary>
    /// Removes a document.
    /// </summary>
    /// <param name="id">The document's ID.</param>
    public void Remove(string id)
    {
        var node = _nodes[id];
        if (node.ParentId != null)
            _nodes[node.ParentId].Children.Remove(id);
        _nodes.Remove(id);
    }

    /// <summary>
    /// Renames a document, keeping its ID.
    /// </summary>
    /// <param name="id">The document's ID.</param>
    /// <param name="newName">The new display name.</param>
    public void Rename(string id, string newName)
    {
        _nodes[id].Name = newName;
    }

    /// <summary>
    /// Changes a document between a file and a folder, keeping its ID.
    /// </summary>
    /// <param name="id">The document's ID.</param>
    /// <param name="isDirectory">True to make it a folder.</param>
    public void SetIsDirectory(string id, bool isDirectory)
    {
        _nodes[id].IsDirectory = isDirectory;
    }

    /// <inheritdoc/>
    public DocumentDetails? QueryDocument(string documentId)
    {
        ThrowIfFailing(nameof(QueryDocument), documentId);
        if (!_nodes.TryGetValue(documentId, out var node))
            return null;
        return new DocumentDetails(
            node.IsDirectory,
            node.LastModified,
            node.IsDirectory || !node.ReportSize ? null : node.Content.Length);
    }

    /// <inheritdoc/>
    public IReadOnlyList<DocumentEntry> ListChildren(string directoryDocumentId)
    {
        ThrowIfFailing(nameof(ListChildren), directoryDocumentId);
        if (!_nodes.TryGetValue(directoryDocumentId, out var node) || !node.IsDirectory)
            throw new FileNotFoundException($"No folder '{directoryDocumentId}'.");

        // Like a cursor, an unknown time reads as zero.
        return node.Children
            .Select(id => _nodes[id])
            .Select(child => new DocumentEntry(child.Id, child.Name, child.LastModified ?? 0, child.IsDirectory))
            .ToList();
    }

    /// <inheritdoc/>
    public Stream OpenRead(string documentId)
    {
        ThrowIfFailing(nameof(OpenRead), documentId);
        if (!_nodes.TryGetValue(documentId, out var node) || node.IsDirectory)
            throw new FileNotFoundException($"No file '{documentId}'.");

        OpenReadCount++;
        return ReadFailure == null
            ? new MemoryStream(node.Content, writable: false)
            : new FailingStream(ReadFailure);
    }

    /// <summary>
    /// Adds a document to a folder.
    /// </summary>
    /// <returns>The new document's ID.</returns>
    private string Add(string parentId, string name, bool isDirectory, long? lastModified, byte[] content, bool reportSize)
    {
        var id = "doc" + ++_nextId;
        _nodes[id] = new Node(id, parentId, name, isDirectory, lastModified)
        {
            Content = content,
            ReportSize = reportSize,
        };
        _nodes[parentId].Children.Add(id);
        return id;
    }

    /// <summary>
    /// Throws the exception that <see cref="Fail"/> returns for a call, if any.
    /// </summary>
    /// <param name="method">The method being called.</param>
    /// <param name="documentId">The document ID it was called with.</param>
    private void ThrowIfFailing(string method, string documentId)
    {
        var exception = Fail?.Invoke(method, documentId);
        if (exception != null)
            throw exception;
    }

    /// <summary>
    /// A document held by the fake provider.
    /// </summary>
    private sealed class Node
    {
        /// <summary>
        /// Creates a document.
        /// </summary>
        /// <param name="id">The document ID.</param>
        /// <param name="parentId">The parent folder's ID, or null for the root.</param>
        /// <param name="name">The display name.</param>
        /// <param name="isDirectory">True for a folder.</param>
        /// <param name="lastModified">The last-modified time, or null if unknown.</param>
        public Node(string id, string? parentId, string name, bool isDirectory, long? lastModified)
        {
            Id = id;
            ParentId = parentId;
            Name = name;
            IsDirectory = isDirectory;
            LastModified = lastModified;
        }

        /// <summary>
        /// The document ID.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The parent folder's ID, or null for the root.
        /// </summary>
        public string? ParentId { get; }

        /// <summary>
        /// The display name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// True for a folder.
        /// </summary>
        public bool IsDirectory { get; set; }

        /// <summary>
        /// The last-modified time, or null if unknown.
        /// </summary>
        public long? LastModified { get; }

        /// <summary>
        /// The file's contents.
        /// </summary>
        public byte[] Content { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// False if the provider doesn't report this file's size.
        /// </summary>
        public bool ReportSize { get; init; } = true;

        /// <summary>
        /// The IDs of the folder's children, in the provider's order.
        /// </summary>
        public List<string> Children { get; } = new();
    }

    /// <summary>
    /// A readable stream whose reads always throw a given exception.
    /// </summary>
    private sealed class FailingStream : Stream
    {
        private readonly Exception _failure;

        /// <summary>
        /// Creates the stream.
        /// </summary>
        /// <param name="failure">The exception that reads throw.</param>
        public FailingStream(Exception failure)
        {
            _failure = failure;
        }

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
        public override int Read(byte[] buffer, int offset, int count) => throw _failure;

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
