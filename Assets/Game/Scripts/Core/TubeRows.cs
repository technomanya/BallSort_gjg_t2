using System;

namespace BallSortGame.Core
{
    /// <summary>
    /// How tubes are laid out in rows (mirrors GameManager.ComputeTubePos): <c>n</c> tubes fill
    /// <c>ceil(n / maxPerRow)</c> rows of <c>ceil(n / rows)</c> tubes each, in list order. Two tubes are physically next
    /// to each other only when they sit side by side in the same row; the last tube of a row and the first of the next
    /// are neighbours in the list but not on the board. Hidden tubes open from physical neighbours only.
    /// </summary>
    public static class TubeRows
    {
        /// <summary>Tubes per row before the board wraps (GameManager.MaxTubesPerRow defaults to it).</summary>
        public const int DefaultMaxPerRow = 7;

        public static int Rows(int tubeCount, int maxPerRow = DefaultMaxPerRow)
        {
            if (tubeCount <= 0) return 0;
            maxPerRow = Math.Max(1, maxPerRow);
            return (tubeCount + maxPerRow - 1) / maxPerRow;
        }

        /// <summary>Tubes in every row but the last (the last may be shorter).</summary>
        public static int PerRow(int tubeCount, int maxPerRow = DefaultMaxPerRow)
        {
            int rows = Rows(tubeCount, maxPerRow);
            return rows == 0 ? 0 : (tubeCount + rows - 1) / rows;
        }

        public static int RowOf(int tube, int tubeCount, int maxPerRow = DefaultMaxPerRow)
        {
            int perRow = PerRow(tubeCount, maxPerRow);
            return perRow == 0 ? 0 : tube / perRow;
        }

        /// <summary>True when the two tubes stand side by side in the same row.</summary>
        public static bool SideBySide(int a, int b, int tubeCount, int maxPerRow = DefaultMaxPerRow)
        {
            if (a < 0 || b < 0 || a >= tubeCount || b >= tubeCount || Math.Abs(a - b) != 1) return false;
            int perRow = PerRow(tubeCount, maxPerRow);
            return perRow > 0 && a / perRow == b / perRow;
        }

        /// <summary>The tube to the left in the same row, or -1.</summary>
        public static int Left(int tube, int tubeCount, int maxPerRow = DefaultMaxPerRow) =>
            SideBySide(tube, tube - 1, tubeCount, maxPerRow) ? tube - 1 : -1;

        /// <summary>The tube to the right in the same row, or -1.</summary>
        public static int Right(int tube, int tubeCount, int maxPerRow = DefaultMaxPerRow) =>
            SideBySide(tube, tube + 1, tubeCount, maxPerRow) ? tube + 1 : -1;
    }
}
