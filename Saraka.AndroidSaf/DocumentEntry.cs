using System;
using System.Collections.Generic;
using System.Text;

namespace Saraka.AndroidSaf
{
    /// <summary>
    /// A document in a directory, as returned by the content resolver.
    /// </summary>
    /// <param name="DocumentId">Content resolver document id</param>
    /// <param name="DisplayName">Display name (e.g. "image123.png")</param>
    /// <param name="LastModified">Last modified time, in milliseconds since the epoch</param>
    /// <param name="IsDirectory">True if the document is a directory</param>
    internal readonly record struct DocumentEntry(
        string DocumentId,
        string DisplayName,
        long LastModified,
        bool IsDirectory);
}
