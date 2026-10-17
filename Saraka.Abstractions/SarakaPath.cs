using System;

namespace Saraka.Abstractions
{
    /// <summary>
    /// Helpers for Saraka paths. A path starts with "/", separates names with "/", and "/" alone is
    /// the root directory.
    /// </summary>
    public static class SarakaPath
    {
        /// <summary>
        /// Splits a path into the names of the entries along it.
        /// </summary>
        /// <param name="path">An absolute path, such as "/data/file.txt".</param>
        /// <param name="paramName">The name of the parameter the path was passed as, for exceptions.</param>
        /// <returns>The names, outermost first. The root, "/", gives none.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="path"/> doesn't start with "/", ends with "/" (other than the root), or
        /// contains an empty name, "." or "..".
        /// </exception>
        public static string[] Split(string path, string paramName)
        {
            if (path == null)
                throw new ArgumentNullException(paramName);
            if (path.Length == 0 || path[0] != '/')
                throw new ArgumentException($"'{path}' is not a path: it must start with \"/\".", paramName);
            if (path.Length == 1)
                return Array.Empty<string>();

            var names = path.Substring(1).Split('/');
            foreach (var name in names)
            {
                if (name.Length == 0 || name == "." || name == "..")
                    throw new ArgumentException($"'{path}' is not a valid path: it contains an empty name, \".\" or \"..\".", paramName);
            }
            return names;
        }
    }
}
