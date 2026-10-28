using System;
using System.Collections.Generic;
using BallSortGame.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BallSortGame.LevelEditor
{
    /// <summary>
    /// Layout edited by the in-app level editor (Magic Sort's EditorBottleCollection, for ball sort).
    /// Tubes are bottom-first stacks without gaps; a cell is a colour 1..N or <see cref="Random"/> (a luck slot that
    /// the filler fills per seed). The tube preset follows from its cells: all colours = Deterministic,
    /// all random = Luck, mixed = PartialLuck, no cells = Empty. No Unity types, so it also runs off the main thread.
    /// </summary>
    [Serializable]
    public sealed class EditorLevel
    {
        public const int Random = -1;

        [JsonProperty("name")] public string Name = "custom_1";
        [JsonProperty("capacity")] public int Capacity = 4;
        [JsonProperty("colors")] public int ColorCount = 4;
        /// <summary>Bottom-first cells per tube (note: pack levels list tubes top-first).</summary>
        [JsonProperty("tubes")] public List<List<int>> Tubes = new List<List<int>>();
        /// <summary>Seeds the generator made from this layout (main fill + variants); dropped on every edit.</summary>
        [JsonProperty("seeds", NullValueHandling = NullValueHandling.Ignore)] public LevelData Seeds;

        public int TubeCount => Tubes.Count;
        public int RandomSlots => UsageOf(Random);
        public bool HasRandom => RandomSlots > 0;

        // ------------------------------------------------------------------ factories

        /// <summary><paramref name="colors"/> full luck tubes plus <paramref name="empties"/> empty ones (Magic Sort "empty level").</summary>
        public static EditorLevel Standard(string name, int colors, int empties, int capacity)
        {
            var lv = new EditorLevel { Name = name, Capacity = capacity, ColorCount = colors };
            for (int i = 0; i < colors; i++) lv.Tubes.Add(Filled(capacity, Random));
            for (int i = 0; i < empties; i++) lv.Tubes.Add(new List<int>());
            return lv;
        }

        /// <summary>A concrete pack level (tubes top-first, 0 = empty) as a fully deterministic layout.</summary>
        public static EditorLevel FromLevelData(LevelData data, string name)
        {
            var lv = new EditorLevel { Name = name, Capacity = 1, ColorCount = 1 };
            foreach (var topFirst in data.Tubes)
            {
                var t = new List<int>();
                for (int j = topFirst.Count - 1; j >= 0; j--)
                    if (topFirst[j] > 0) { t.Add(topFirst[j]); lv.ColorCount = Math.Max(lv.ColorCount, topFirst[j]); }
                lv.Tubes.Add(t);
                lv.Capacity = Math.Max(lv.Capacity, topFirst.Count);
            }
            lv.Normalize();
            return lv;
        }

        /// <summary>Accepts an editor layout, a pack level, a whole pack (first level) or a bare top-first tube array.</summary>
        public static bool TryParse(string json, out EditorLevel level, out string error)
        {
            level = null;
            error = null;
            try
            {
                var tok = JToken.Parse(json);
                if (tok is JObject o)
                {
                    if (o["capacity"] != null) level = o.ToObject<EditorLevel>();
                    else if (o["levels"] is JArray levels && levels.Count > 0)
                    {
                        var data = levels[0].ToObject<LevelData>();
                        level = FromLevelData(data, data.PuzzleId ?? "pasted");
                    }
                    else if (o["tubes"] != null)
                    {
                        var data = o.ToObject<LevelData>();
                        level = FromLevelData(data, data.PuzzleId ?? "pasted");
                    }
                }
                else if (tok is JArray arr)
                {
                    level = FromLevelData(new LevelData { Tubes = arr.ToObject<List<List<int>>>() }, "pasted");
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            if (level == null) { error = "not a level layout"; return false; }
            level.Normalize();
            return true;
        }

        static List<int> Filled(int count, int value)
        {
            var t = new List<int>(count);
            for (int i = 0; i < count; i++) t.Add(value);
            return t;
        }

        /// <summary>Clamps sizes and drops invalid cells (after loading untrusted JSON).</summary>
        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Name)) Name = "custom_1";
            if (Tubes == null) Tubes = new List<List<int>>();
            Capacity = Math.Max(1, Math.Min(BoardState.MaxDepth, Capacity));
            if (Tubes.Count > BoardState.MaxTubes) Tubes.RemoveRange(BoardState.MaxTubes, Tubes.Count - BoardState.MaxTubes);
            for (int i = 0; i < Tubes.Count; i++)
            {
                var t = Tubes[i] ?? (Tubes[i] = new List<int>());
                t.RemoveAll(c => c == 0 || c < Random);
                if (t.Count > Capacity) t.RemoveRange(Capacity, t.Count - Capacity);
            }
            ColorCount = Math.Max(Math.Max(1, ColorCount), MaxUsedColor());
        }

        public EditorLevel Clone() => JsonConvert.DeserializeObject<EditorLevel>(JsonConvert.SerializeObject(this));

        // ------------------------------------------------------------------ queries

        public TubePreset PresetOf(int tube)
        {
            var t = Tubes[tube];
            if (t.Count == 0) return TubePreset.Empty;
            int random = 0;
            foreach (var c in t) if (c == Random) random++;
            return random == 0 ? TubePreset.Deterministic : random == t.Count ? TubePreset.Luck : TubePreset.PartialLuck;
        }

        public int UsageOf(int value)
        {
            int n = 0;
            foreach (var t in Tubes) foreach (var c in t) if (c == value) n++;
            return n;
        }

        public int MaxUsedColor()
        {
            int m = 0;
            foreach (var t in Tubes) foreach (var c in t) m = Math.Max(m, c);
            return m;
        }

        public int BallCount
        {
            get { int n = 0; foreach (var t in Tubes) n += t.Count; return n; }
        }

        /// <summary>Random slots the filler still has to fill (every colour needs exactly <see cref="Capacity"/> balls).</summary>
        public int RandomSlotsNeeded
        {
            get
            {
                int fixedBalls = 0;
                foreach (var t in Tubes) foreach (var c in t) if (c > 0) fixedBalls++;
                return ColorCount * Capacity - fixedBalls;
            }
        }

        /// <summary>The generator's view of the layout.</summary>
        public LevelDesign ToDesign()
        {
            var d = new LevelDesign { Name = Name, ColorCount = ColorCount };
            for (int i = 0; i < Tubes.Count; i++)
            {
                var t = Tubes[i];
                var fixedCells = new int[t.Count];
                for (int s = 0; s < t.Count; s++) fixedCells[s] = t[s] == Random ? 0 : t[s];
                d.Tubes.Add(new TubeSpec { Preset = PresetOf(i), Capacity = Capacity, Fixed = fixedCells, FillCount = t.Count });
            }
            return d;
        }

        /// <summary>Structural check (Magic Sort EditorManager.IsLevelValid): ball counts, colour range, sizes.</summary>
        public bool Validate(out string error)
        {
            if (Tubes.Count == 0) { error = "Add at least one tube."; return false; }
            if (Tubes.Count > BoardState.MaxTubes) { error = $"At most {BoardState.MaxTubes} tubes."; return false; }
            for (int c = 1; c <= ColorCount; c++)
            {
                int used = UsageOf(c);
                if (used > Capacity) { error = $"Colour {c} is placed {used} times; a colour has exactly {Capacity} balls."; return false; }
            }
            int need = ColorCount * Capacity;
            if (BallCount != need)
            {
                error = $"{BallCount} balls placed, {need} needed ({ColorCount} colours x {Capacity}). " +
                        (BallCount < need ? "Add balls or remove a colour." : "Remove balls or add a colour.");
                return false;
            }
            int empties = 0;
            foreach (var t in Tubes) if (t.Count == 0) empties++;
            if (empties == 0 && BallCount == Tubes.Count * Capacity) { error = "Every tube is full: leave space (an empty tube or free slots)."; return false; }
            if (!ToDesign().Validate(out error)) return false;
            if (!HasRandom)
            {
                var board = Fill(0, out error);
                if (board == null) return false;
                if (board.IsWin()) { error = "The layout is already solved."; return false; }
            }
            return true;
        }

        /// <summary>One concrete fill (seed) of the layout. Null with an error when the layout is invalid.</summary>
        public BoardState Fill(int seed, out string error) => LevelFiller.Fill(ToDesign(), seed, out error);

        // ------------------------------------------------------------------ edits (return true when something changed)

        /// <summary>
        /// Paints <paramref name="value"/> into a slot. 0 erases the ball (balls above drop down); a slot above the
        /// stack appends to the stack, since balls cannot float.
        /// </summary>
        public bool Paint(int tube, int slot, int value)
        {
            var t = Tubes[tube];
            if (value == 0)
            {
                if (slot < 0 || slot >= t.Count) return false;
                t.RemoveAt(slot);
                return true;
            }
            if (slot < t.Count)
            {
                if (t[slot] == value) return false;
                t[slot] = value;
            }
            else
            {
                if (t.Count >= Capacity) return false;
                t.Add(value);
            }
            if (value > ColorCount) ColorCount = value;
            return true;
        }

        /// <summary>Fills the whole tube with one value (0 = empty tube, Random = luck tube).</summary>
        public bool FillTube(int tube, int value)
        {
            var t = Tubes[tube];
            var next = value == 0 ? new List<int>() : Filled(Capacity, value);
            if (t.Count == next.Count && t.TrueForAll(c => c == value)) return false;
            Tubes[tube] = next;
            if (value > ColorCount) ColorCount = value;
            return true;
        }

        public bool AddTube()
        {
            if (Tubes.Count >= BoardState.MaxTubes) return false;
            Tubes.Add(new List<int>());
            return true;
        }

        public bool RemoveTube(int tube)
        {
            if (tube < 0 || tube >= Tubes.Count) return false;
            Tubes.RemoveAt(tube);
            return true;
        }

        public bool SwapTubes(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a >= Tubes.Count || b >= Tubes.Count) return false;
            (Tubes[a], Tubes[b]) = (Tubes[b], Tubes[a]);
            return true;
        }

        /// <summary>Changes the tube height; balls above the new height are removed.</summary>
        public bool SetCapacity(int capacity)
        {
            capacity = Math.Max(1, Math.Min(BoardState.MaxDepth, capacity));
            if (capacity == Capacity) return false;
            Capacity = capacity;
            foreach (var t in Tubes) if (t.Count > capacity) t.RemoveRange(capacity, t.Count - capacity);
            return true;
        }

        /// <summary>Sets the colour count; it cannot drop below the highest colour already placed.</summary>
        public bool SetColorCount(int colors, int maxColors)
        {
            colors = Math.Max(Math.Max(1, MaxUsedColor()), Math.Min(maxColors, colors));
            if (colors == ColorCount) return false;
            ColorCount = colors;
            return true;
        }

        public bool ClearBalls()
        {
            bool any = false;
            foreach (var t in Tubes) if (t.Count > 0) { t.Clear(); any = true; }
            return any;
        }
    }
}
