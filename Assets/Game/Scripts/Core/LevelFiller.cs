using System;
using System.Collections.Generic;

namespace BallSortGame.Core
{
    /// <summary>
    /// Random fill of a design: fixed cells are copied, the remaining balls go into the luck slots at random.
    /// The same (design, seed) always gives the same board, so a seed number names a level.
    /// </summary>
    public static class LevelFiller
    {
        /// <summary>
        /// Null with <paramref name="error"/> when the design is invalid. <paramref name="rejectSolvedTubes"/>
        /// re-rolls (up to <paramref name="maxRerolls"/> times) while a random tube starts completed or the board starts won.
        /// </summary>
        public static BoardState Fill(LevelDesign design, int seed, out string error, bool rejectSolvedTubes = true, int maxRerolls = 50)
        {
            var units = design.RemainingUnits(out int luckSlots, out error);
            if (units == null) return null;
            var rng = new Random(seed);
            int cap = design.Capacity;

            for (int attempt = 0; ; attempt++)
            {
                // bag of remaining balls, shuffled
                var bag = new List<byte>(luckSlots);
                for (int c = 1; c <= design.ColorCount; c++)
                    for (int k = 0; k < units[c]; k++) bag.Add((byte)c);
                for (int i = bag.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }

                var board = new BoardState(design.TubeCount, cap);
                int next = 0;
                for (int t = 0; t < design.TubeCount; t++)
                {
                    var spec = design.Tubes[t];
                    board.SetKind(t, spec.Kind);
                    int fill = spec.EffectiveFillCount;
                    for (int s = 0; s < fill; s++)
                    {
                        int f = spec.FixedAt(s);
                        board.Push(t, f != 0 ? (byte)f : bag[next++]);
                    }
                    board.SetHeavyMask(t, spec.HeavyMask);
                }
                board.OpenHiddenTubes();

                if (!rejectSolvedTubes || attempt >= maxRerolls) return board;
                bool bad = board.IsWin();
                if (!bad)
                {
                    for (int t = 0; t < design.TubeCount && !bad; t++)
                    {
                        var spec = design.Tubes[t];
                        if (spec.Preset == TubePreset.Deterministic || spec.EffectiveFillCount == 0) continue;
                        if (board.IsCompleted(t)) bad = true;
                    }
                }
                if (!bad) return board;
            }
        }

        /// <summary>Tubes listed top-first with 0 for empty slots, as in the level pack.</summary>
        public static List<List<int>> ToTopFirst(BoardState board)
        {
            var tubes = new List<List<int>>(board.TubeCount);
            for (int t = 0; t < board.TubeCount; t++)
            {
                var tube = new List<int>(board.Capacity);
                for (int s = board.Capacity - 1; s >= 0; s--) tube.Add(board.ColorAt(t, s));
                tubes.Add(tube);
            }
            return tubes;
        }
    }
}
