using System;

namespace BallSortGame.Core
{
    /// <summary>
    /// Row layout of the board, matching the game's tube placement: n tubes fill ceil(n / maxPerRow) rows of
    /// ceil(n / rows) tubes, in list order. The last tube of a row and the first of the next are not neighbours,
    /// which matters because hidden tubes open only from a tube beside them.
    /// </summary>
    public static class TubeRows
    {
        /// <summary>Tubes per row before the board wraps.</summary>
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
