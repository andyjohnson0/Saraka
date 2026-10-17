using System;
using System.Collections.Generic;

namespace Saraka.AndroidSaf
{
    /// <summary>
    /// Options that control which entries a <see cref="FileSystem"/> presents and the order its
    /// listings are returned in.
    /// </summary>
    /// <remarks>
    /// The options are read when the file system is opened, so changing them afterwards has no
    /// effect on it. Entries excluded by the options are treated as if they don't exist: they are
    /// left out of listings and can't be found by name.
    /// </remarks>
    public sealed class FileSystemOptions
    {
        /// <summary>
        /// The field that listings are sorted on. The default is <see cref="SortField.FileName"/>.
        /// </summary>
        public SortField SortField { get; set; } = SortField.FileName;

        /// <summary>
        /// The direction that listings are sorted in. The default is <see cref="SortOrder.Ascending"/>.
        /// </summary>
        public SortOrder SortOrder { get; set; } = SortOrder.Ascending;

        /// <summary>
        /// The file types to include, as file name extensions without the leading period (e.g.
        /// "jpeg"). Matching ignores case, and "jpeg" also matches "jpg". Null, the default,
        /// includes every file. Directories are not affected.
        /// </summary>
        public IEnumerable<string>? FileTypes { get; set; }

        /// <summary>
        /// True to include hidden files, whose names start with a period. The default is false.
        /// </summary>
        public bool IncludeHiddenFiles { get; set; }

        /// <summary>
        /// True to include hidden directories, whose names start with a period. The default is false.
        /// </summary>
        public bool IncludeHiddenFolders { get; set; }
    }
}
