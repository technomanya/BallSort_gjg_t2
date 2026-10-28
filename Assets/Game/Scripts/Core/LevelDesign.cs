using System;
using System.Collections.Generic;
using System.Text;

namespace BallSortGame.Core
{
    /// <summary>How a tube of a design is filled, mirroring the editor presets of the Magic Sort pipeline.</summary>
    public enum TubePreset
    {
        /// <summary>Every slot is fixed by the designer.</summary>
        Deterministic = 1,
        /// <summary>Every slot is filled at random.</summary>
        Luck = 2,
        /// <summary>Some slots fixed, the rest random (0 in <see cref="TubeSpec.Fixed"/> marks a random slot).</summary>
        PartialLuck = 3,
        /// <summary>Starts empty.</summary>
        Empty = 4,
    }

    /// <summary>One tube of a design. <see cref="Fixed"/> is bottom-first; 0 = slot filled by the random fill.</summary>
    [Serializable]
    public sealed class TubeSpec
    {
        public TubePreset Preset = TubePreset.Luck;
        public int Capacity = 4;
        /// <summary>Bottom-first colours for Deterministic / PartialLuck tubes; 0 = luck slot. Null or shorter than Capacity = remaining slots are luck slots.</summary>
        public int[] Fixed;
        /// <summary>Slots this tube has balls in. Luck/Partial tubes are full unless <see cref="FillCount"/> says otherwise.</summary>
        public int FillCount = -1;

        public int EffectiveFillCount => Preset == TubePreset.Empty ? 0 : (FillCount < 0 ? Capacity : Math.Min(FillCount, Capacity));

        public int FixedAt(int slot) => Fixed != null && slot < Fixed.Length ? Fixed[slot] : 0;

        public static TubeSpec Luck(int capacity = 4) => new TubeSpec { Preset = TubePreset.Luck, Capacity = capacity };
        public static TubeSpec Empty(int capacity = 4) => new TubeSpec { Preset = TubePreset.Empty, Capacity = capacity };
        public static TubeSpec Deterministic(int capacity, params int[] bottomFirst) =>
            new TubeSpec { Preset = TubePreset.Deterministic, Capacity = capacity, Fixed = bottomFirst, FillCount = bottomFirst.Length };
        public static TubeSpec Partial(int capacity, params int[] bottomFirstWithZeros) =>
            new TubeSpec { Preset = TubePreset.PartialLuck, Capacity = capacity, Fixed = bottomFirstWithZeros };
    }

    /// <summary>
    /// A level design: the part of a level the designer fixes (tube count, capacity, colour count, empties,
    /// hand-placed colours). The random fill of the luck slots produces the concrete "seeds" of the design.
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

        /// <summary>Standard design: <paramref name="colors"/> full luck tubes plus <paramref name="empties"/> empty tubes.</summary>
        public static LevelDesign Standard(string name, int colors, int empties, int capacity = 4)
            => Standard(name, colors, empties, capacity, empties);

        /// <summary>
        /// Standard design with <paramref name="spareTubes"/> tubes beyond the colour count, of which
        /// <paramref name="empties"/> start empty; the free slots of the other spare tubes are spread over the
        /// filled tubes as partial fills (Magic Sort boards mostly have 2 spare tubes but only 1-2 empty ones).
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

        /// <summary>Number of balls of each colour the fill must place (colour index 1..ColorCount), after the fixed slots.</summary>
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
            if (remaining != luckSlots) { error = $"{luckSlots} luck slots but {remaining} balls left to place (every colour needs exactly {cap} balls)"; return null; }
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
    /// Target board size per level index, taken from the Magic Sort default level order
    /// (levels 1-10: 6 tubes / 4 colours; 11-160: 9-11 tubes / 7-9 colours; 161+: 12-14 tubes / 10-11 colours;
    /// 1-2 empty tubes; every level ending in 4 is hard, ending in 9 super-hard).
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
            /// <summary>Wanted bot-style difficulty bucket (1..7).</summary>
            public int TargetBucket;
            public bool IsTutorial;
        }

        /// <summary>Magic Sort: first hard level is 14, first super-hard 19; tiny early boards cannot be made hard.</summary>
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
            return level % 10 == 1 || level % 10 == 6 ? 1 : 2;     // Magic Sort averages ~1.6 empties per level
        }

        /// <summary>Tubes beyond the colour count (Magic Sort: 2 for most levels, 3 for about a third of the late ones).</summary>
        public static int SpareTubesFor(int level)
        {
            if (level <= 20) return 2;
            if (level > 160 && level % 3 == 0) return 3;
            return 2;
        }

        /// <summary>Mean bot difficulty of the shipped Magic Sort levels, rounded: 1 (1-20), 1-2 (21-90), 2-3 (91-160), 3 (161+); hard 4, super-hard 5.</summary>
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
                IsTutorial = level <= 4,
            };
        }

        public static LevelDesign DesignFor(int level, int capacity = 4)
        {
            var s = For(level, capacity);
            return LevelDesign.Standard($"lvl{level}", s.Colors, s.Empties, capacity, s.SpareTubes);
        }
    }
}
