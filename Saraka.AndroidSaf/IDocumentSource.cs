using System;
using System.Collections.Generic;
using System.IO;

namespace Saraka.AndroidSaf
{
    /// <summary>
    /// The document provider operations that <see cref="FileSystem"/> needs. Keeping them behind
    /// this interface keeps the Android code in one place, so that the rest can be tested off
    /// the device.
    /// </summary>
    /// <remarks>
    /// The methods block. Failures are reported as .NET exceptions, never as Android or Java ones:
    /// <see cref="UnauthorizedAccessException"/> when access is denied,
    /// <see cref="FileNotFoundException"/> when a document doesn't exist,
    /// <see cref="OperationCanceledException"/> when the provider cancels, and
    /// <see cref="IOException"/> for any other failure.
    /// </remarks>
    internal interface IDocumentSource
    {
        /// <summary>
        /// The document ID of the tree's top-level folder.
        /// </summary>
        string RootDocumentId { get; }

        /// <summary>
        /// Gets the details of a document.
        /// </summary>
        /// <param name="documentId">The document ID.</param>
        /// <returns>The document's details, or null if it doesn't exist.</returns>
        DocumentDetails? QueryDocument(string documentId);

        /// <summary>
        /// Lists the documents directly inside a folder, unfiltered and in the provider's order.
        /// </summary>
        /// <param name="directoryDocumentId">The folder's document ID.</param>
        /// <returns>The folder's children.</returns>
        /// <exception cref="FileNotFoundException">The folder doesn't exist.</exception>
        IReadOnlyList<DocumentEntry> ListChildren(string directoryDocumentId);

        /// <summary>
        /// Opens a read-only stream over a document's contents. Errors while reading the stream are
        /// reported in the same way as errors from the other methods.
        /// </summary>
        /// <param name="documentId">The document ID.</param>
        /// <returns>The stream, which the caller disposes.</returns>
        /// <exception cref="FileNotFoundException">The document doesn't exist.</exception>
        Stream OpenRead(string documentId);
    }
}
