using System;
using System.Collections.Generic;
using System.Text;

namespace Saraka.AndroidSaf
{
    /// <summary>
    /// Navigation between zero-based positions in a collection of a given length.
    /// </summary>
    internal static class Positions
    {
        /// <summary>
        /// Index value for "no position"
        /// </summary>
        public const int None = -1;


        /// <summary>
        /// Get the position after the supplied position.
        /// </summary>
        /// <param name="position">Position</param>
        /// <param name="length">Number of items in the collection</param>
        /// <returns>Next position, or None if there is no next position</returns>
        public static int Next(int position, int length)
        {
            return position >= 0 && position < length - 1 ? position + 1 : None;
        }


        /// <summary>
        /// Get the position before the supplied position.
        /// </summary>
        /// <param name="position">Position</param>
        /// <param name="length">Number of items in the collection</param>
        /// <returns>Previous position, or None if there is no previous position</returns>
        public static int Previous(int position, int length)
        {
            return position >= 1 && position < length ? position - 1 : None;
        }


        /// <summary>
        /// Get the first position.
        /// </summary>
        /// <param name="length">Number of items in the collection</param>
        /// <returns>First position, or None if the collection is empty</returns>
        public static int First(int length)
        {
            return length > 0 ? 0 : None;
        }


        /// <summary>
        /// Get the last position.
        /// </summary>
        /// <param name="length">Number of items in the collection</param>
        /// <returns>Last position, or None if the collection is empty</returns>
        public static int Last(int length)
        {
            return length > 0 ? length - 1 : None;
        }


        /// <summary>
        /// Get a randomly-selected position.
        /// </summary>
        /// <param name="length">Number of items in the collection</param>
        /// <param name="excludePosition">
        /// If specified and a valid position, does not return this position, unless it is the only one.
        /// Use to prevent a current position from being returned.
        /// </param>
        /// <param name="random">Random number generator</param>
        /// <returns>A randomly-selected position, or None if the collection is empty.</returns>
        public static int Random(
            int length,
            int? excludePosition,
            System.Random random)
        {
            ArgumentNullException.ThrowIfNull(random);

            if (length <= 0)
                return None;
            if (length == 1)
                return 0;

            if (excludePosition is int excluded && excluded >= 0 && excluded < length)
            {
                // Choose from the other length - 1 positions, skipping over the excluded one.
                var randomPosition = random.Next(0, length - 1);  // upper bound is exclusive
                return randomPosition >= excluded ? randomPosition + 1 : randomPosition;
            }
            else
            {
                return random.Next(0, length);  // upper bound is exclusive
            }
        }
    }

}
