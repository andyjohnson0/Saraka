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

    public FakeDocumentSource()
    {
        _nodes[RootId] = new Node(RootId, parentId: null, string.Empty, isDirectory: true, lastModified: 0);
    }

    public string RootDocumentId => RootId;

    /// <summary>
    /// Called with the method name and document ID before each call. A non-null result is thrown.
    /// </summary>
    public Func<string, string, Exception?>? Fail { get; set; }

    /// <summary>
    /// If set, streams from <see cref="OpenRead"/> throw this when they are read.
    /// </summary>
    public Exception? ReadFailure { get; set; }

    public int OpenReadCount { get; private set; }

    public string AddFolder(string parentId, string name, long? lastModified = 0)
    {
        return Add(parentId, name, isDirectory: true, lastModified, content: Array.Empty<byte>(), reportSize: true);
    }

    public string AddFile(string parentId, string name, string content = "", long? lastModified = 0, bool reportSize = true)
    {
        return AddFile(parentId, name, Encoding.UTF8.GetBytes(content), lastModified, reportSize);
    }

    public string AddFile(string parentId, string name, byte[] content, long? lastModified = 0, bool reportSize = true)
    {
        return Add(parentId, name, isDirectory: false, lastModified, content, reportSize);
    }

    public void Remove(string id)
    {
        var node = _nodes[id];
        if (node.ParentId != null)
            _nodes[node.ParentId].Children.Remove(id);
        _nodes.Remove(id);
    }

    public void Rename(string id, string newName)
    {
        _nodes[id].Name = newName;
    }

    public void SetIsDirectory(string id, bool isDirectory)
    {
        _nodes[id].IsDirectory = isDirectory;
    }

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

    private void ThrowIfFailing(string method, string documentId)
    {
        var exception = Fail?.Invoke(method, documentId);
        if (exception != null)
            throw exception;
    }

    private sealed class Node
    {
        public Node(string id, string? parentId, string name, bool isDirectory, long? lastModified)
        {
            Id = id;
            ParentId = parentId;
            Name = name;
            IsDirectory = isDirectory;
            LastModified = lastModified;
        }

        public string Id { get; }

        public string? ParentId { get; }

        public string Name { get; set; }

        public bool IsDirectory { get; set; }

        public long? LastModified { get; }

        public byte[] Content { get; init; } = Array.Empty<byte>();

        public bool ReportSize { get; init; } = true;

        public List<string> Children { get; } = new();
    }

    private sealed class FailingStream : Stream
    {
        private readonly Exception _failure;

        public FailingStream(Exception failure)
        {
            _failure = failure;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw _failure;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
