using System;
using System.Collections.Generic;
using System.Text;

namespace BallSortGame.Core
{
    /// <summary>How a design tube is filled.</summary>
    public enum TubePreset
    {
        /// <summary>Every slot is fixed by the designer.</summary>
        Deterministic = 1,
        /// <summary>Every slot is random.</summary>
        Luck = 2,
        /// <summary>Some slots fixed, the rest random (0 in <see cref="TubeSpec.Fixed"/> = random).</summary>
        PartialLuck = 3,
        Empty = 4,
    }

    /// <summary>One tube of a design.</summary>
    [Serializable]
    public sealed class TubeSpec
    {
        public TubePreset Preset = TubePreset.Luck;
        /// <summary>InOnly tubes must start empty, OutOnly tubes with balls.</summary>
        public TubeKind Kind = TubeKind.Normal;
        public int Capacity = 4;
        /// <summary>Bottom-first colours for Deterministic / PartialLuck tubes. 0 or past the end = random slot.</summary>
        public int[] Fixed;
        /// <summary>Heavy balls, bit s = the s-th ball from the bottom. Every bit needs a ball.</summary>
        public int HeavyMask;
        /// <summary>Balls the tube starts with. -1 = full.</summary>
        public int FillCount = -1;

        public int EffectiveFillCount => Preset == TubePreset.Empty ? 0 : (FillCount < 0 ? Capacity : Math.Min(FillCount, Capacity));

        public int FixedAt(int slot) => Fixed != null && slot < Fixed.Length ? Fixed[slot] : 0;

        public static TubeSpec Luck(int capacity = 4) => new TubeSpec { Preset = TubePreset.Luck, Capacity = capacity };
        public static TubeSpec Empty(int capacity = 4) => new TubeSpec { Preset = TubePreset.Empty, Capacity = capacity };
    }

    /// <summary>
    /// The part of a level the designer fixes: tubes, capacity, colour count, empties and hand-placed colours.
    /// Random fills of the luck slots turn it into concrete seeds.
    /// </summary>
    [Serializable]
    public sealed class LevelDesign
    {
        public string Name = "design";
        public int ColorCount;
        public List<TubeSpec> Tubes = new List<TubeSpec>();

        public int TubeCount => Tubes.Count;
        public int Capacity { get { int c = 0; foreach (var t in Tubes) c = Math.Max(c, t.Capacity); return c; } }
        public int EmptyTubeCount { get { int n = 0; foreach (var t in Tubes) if (t.EffectiveFillCount == 0) n++; return n; } }

        /// <summary><paramref name="colors"/> full luck tubes plus <paramref name="empties"/> empty tubes.</summary>
        public static LevelDesign Standard(string name, int colors, int empties, int capacity = 4)
            => Standard(name, colors, empties, capacity, empties);

        /// <summary>
        /// <paramref name="spareTubes"/> tubes beyond the colour count, of which <paramref name="empties"/> start
        /// empty. The free slots of the other spare tubes are spread over the filled tubes.
        /// </summary>
        public static LevelDesign Standard(string name, int colors, int empties, int capacity, int spareTubes)
        {
            spareTubes = Math.Max(spareTubes, empties);
            int filled = colors + spareTubes - empties;
            var d = new LevelDesign { Name = name, ColorCount = colors };
            int freeSlots = filled * capacity - colors * capacity;   // slots left open inside the filled tubes
            for (int i = 0; i < filled; i++)
            {
                var t = TubeSpec.Luck(capacity);
                // spread the free slots round-robin, at most capacity-1 per tube so every filled tube has a ball
                int share = freeSlots / filled + (i < freeSlots % filled ? 1 : 0);
                t.FillCount = Math.Max(1, capacity - Math.Min(share, capacity - 1));
                d.Tubes.Add(t);
            }
            for (int i = 0; i < empties; i++) d.Tubes.Add(TubeSpec.Empty(capacity));
            return d;
        }

        /// <summary>
        /// Covers up to <paramref name="count"/> random filled tubes, never two side by side, so every hidden tube
        /// has a neighbour that can open it. Returns how many were covered.
        /// </summary>
        public int HideTubes(int count, int seed)
        {
            var candidates = new List<int>();
            for (int i = 0; i < Tubes.Count; i++)
                if (Tubes[i].Kind == TubeKind.Normal && Tubes[i].EffectiveFillCount > 0) candidates.Add(i);
            var rng = new Random(seed);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }
            int hidden = 0;
            foreach (int i in candidates)
            {
                if (hidden >= count) break;
                int l = TubeRows.Left(i, Tubes.Count), r = TubeRows.Right(i, Tubes.Count);
                if (l < 0 && r < 0) continue;   // alone in its row: nothing could ever open it
                if (l >= 0 && Tubes[l].Kind == TubeKind.Hidden) continue;
                if (r >= 0 && Tubes[r].Kind == TubeKind.Hidden) continue;
                Tubes[i].Kind = TubeKind.Hidden;
                hidden++;
            }
            return hidden;
        }

        /// <summary>Balls of each colour (index 1..ColorCount) left for the random fill after the fixed slots. Null on error.</summary>
        public int[] RemainingUnits(out int luckSlots, out string error)
        {
            error = null;
            int cap = Capacity;
            var units = new int[ColorCount + 1];
            for (int c = 1; c <= ColorCount; c++) units[c] = cap;
            luckSlots = 0;
            foreach (var t in Tubes)
            {
                if (t.Capacity != cap) { error = "all tubes must share one capacity"; return null; }
                int fill = t.EffectiveFillCount;
                if (t.Kind == TubeKind.InOnly && fill > 0) { error = "an in-only tube must start empty"; return null; }
                if (t.Kind == TubeKind.OutOnly && fill == 0) { error = "an out-only tube must start with balls"; return null; }
                if (t.HeavyMask < 0 || (t.HeavyMask >> fill) != 0) { error = "a heavy ball needs a ball: the weight sits above the tube's balls"; return null; }
                for (int s = 0; s < fill; s++)
                {
                    int f = t.FixedAt(s);
                    if (f < 0 || f > ColorCount) { error = $"fixed colour {f} out of range 1..{ColorCount}"; return null; }
                    if (f == 0) luckSlots++;
                    else if (--units[f] < 0) { error = $"colour {f} placed more than {cap} times"; return null; }
                }
            }
            int remaining = 0;
            for (int c = 1; c <= ColorCount; c++) remaining += units[c];
            if (remaining != luckSlots) { error = $"{luckSlots} hidden (?) slots but {remaining} balls left to place (every colour needs exactly {cap} balls)"; return null; }
            return units;
        }

        public bool Validate(out string error)
        {
            if (ColorCount <= 0) { error = "ColorCount must be > 0"; return false; }
            if (Tubes.Count == 0) { error = "no tubes"; return false; }
            if (Tubes.Count > BoardState.MaxTubes) { error = $"more than {BoardState.MaxTubes} tubes"; return false; }
            if (Capacity > BoardState.MaxDepth) { error = $"capacity above {BoardState.MaxDepth}"; return false; }
            if (ColorCount > 255) { error = "too many colours"; return false; }
            RemainingUnits(out _, out error);
            return error == null;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Name).Append(": ").Append(ColorCount).Append(" colours, ").Append(TubeCount).Append(" tubes (");
            sb.Append(EmptyTubeCount).Append(" empty), capacity ").Append(Capacity);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Target board size and difficulty per level. Boards grow from 1 colour to 11 over the first ~200 levels,
    /// with 1-2 empty tubes. Levels ending in 4 are hard, in 9 super-hard.
    /// </summary>
    public static class ProgressionCurve
    {
        public struct Slot
        {
            public int Level;
            public int Colors;
            public int Empties;
            public int SpareTubes;
            public int Capacity;
            public bool IsHard;
            public bool IsSuperHard;
            /// <summary>Wanted difficulty bucket (1..7).</summary>
            public int TargetBucket;
        }

        /// <summary>Tiny early boards cannot be made hard, so the first hard level is 14 and the first super-hard 19.</summary>
        public static bool IsHardLevel(int level) => level >= 14 && level % 10 == 4;
        public static bool IsSuperHardLevel(int level) => level >= 19 && level % 10 == 9;

        /// <summary>Colours for a regular level. Hard/super-hard slots use the same board size as their neighbours.</summary>
        public static int ColorsFor(int level)
        {
            if (level <= 1) return 1;
            if (level <= 2) return 2;
            if (level <= 4) return 3;
            if (level <= 10) return 4 + (level - 5) / 3;          // 4..5
            if (level <= 20) return 7;
            if (level <= 60) return 8;
            if (level <= 160) return 9;
            if (level <= 200) return 10;
            return 11;
        }

        public static int EmptiesFor(int level)
        {
            if (level <= 1) return 1;
            if (level <= 20) return 2;
            return level % 10 == 1 || level % 10 == 6 ? 1 : 2;     // about 1.6 empties per level on average
        }

        /// <summary>Tubes beyond the colour count: 2, or 3 for about a third of the late levels.</summary>
        public static int SpareTubesFor(int level)
        {
            if (level <= 20) return 2;
            if (level > 160 && level % 3 == 0) return 3;
            return 2;
        }

        /// <summary>1 (levels 1-20), 1-2 (21-90), 2-3 (91-130), 3-4 after. Hard 4, super-hard 5.</summary>
        public static int TargetBucketFor(int level)
        {
            if (IsSuperHardLevel(level)) return 5;
            if (IsHardLevel(level)) return 4;
            if (level <= 20) return 1;
            if (level <= 60) return level % 2 == 0 ? 1 : 2;
            if (level <= 90) return 2;
            if (level <= 130) return level % 2 == 0 ? 2 : 3;
            // boards with a third spare tube have more slack and rarely rate above 3
            return SpareTubesFor(level) == 3 ? 3 : (level % 2 == 0 ? 4 : 3);
        }

        public static Slot For(int level, int capacity = 4)
        {
            return new Slot
            {
                Level = level,
                Colors = ColorsFor(level),
                Empties = EmptiesFor(level),
                SpareTubes = SpareTubesFor(level),
                Capacity = capacity,
                IsHard = IsHardLevel(level),
                IsSuperHard = IsSuperHardLevel(level),
                TargetBucket = TargetBucketFor(level),
            };
        }

    }
}
