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
    /// all random = Luck, mixed = PartialLuck, no cells = Empty. A tube may also be one-way (<see cref="Kinds"/>):
    /// in-only tubes start empty, out-only tubes start with balls. A tube's bottom ball may be heavy
    /// (<see cref="HeavyFlags"/>, the cannonball obstacle): still a colour, but it only moves into an empty tube.
    /// No Unity types, so it also runs off the main thread.
    /// </summary>
    [Serializable]
    public sealed class EditorLevel
    {
        public const int Random = -1;

        [JsonProperty("name")] public string Name = "custom_1";
        /// <summary>
        /// 1-based pack slot this layout belongs to: the level number the slot shows the first time round, before the
        /// pack loops. Save replaces that slot; 0 = a new level, appended at the end of the pack on save.
        /// </summary>
        [JsonProperty("slot")] public int Slot;
        [JsonProperty("capacity")] public int Capacity = 4;
        [JsonProperty("colors")] public int ColorCount = 4;
        /// <summary>Bottom-first cells per tube (note: pack levels list tubes top-first).</summary>
        [JsonProperty("tubes")] public List<List<int>> Tubes = new List<List<int>>();
        /// <summary>Per-tube <see cref="TubeKind"/> as ints, parallel to <see cref="Tubes"/>; null = every tube normal.</summary>
        [JsonProperty("kinds", NullValueHandling = NullValueHandling.Ignore)] public List<int> Kinds;
        /// <summary>Per-tube 0/1, parallel to <see cref="Tubes"/>: 1 = the tube's bottom ball is heavy; null = no heavy ball.</summary>
        [JsonProperty("heavy", NullValueHandling = NullValueHandling.Ignore)] public List<int> HeavyFlags;
        /// <summary>Seeds the generator made from this layout (main fill + variants); dropped on every edit.</summary>
        [JsonProperty("seeds", NullValueHandling = NullValueHandling.Ignore)] public LevelData Seeds;

        public int TubeCount => Tubes.Count;
        public int RandomSlots => UsageOf(Random);
        public bool HasRandom => RandomSlots > 0;
        public int HeavyCount
        {
            get { int n = 0; for (int i = 0; i < Tubes.Count; i++) if (IsHeavy(i)) n++; return n; }
        }
        public bool HasHeavy => HeavyCount > 0;
        public bool HasSpecialTubes => CountKind(TubeKind.InOnly) + CountKind(TubeKind.OutOnly) + CountKind(TubeKind.Hidden) > 0;

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
            if (data.TubeKinds != null) lv.Kinds = new List<int>(data.TubeKinds);
            if (data.Heavy != null) lv.HeavyFlags = new List<int>(data.Heavy);
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
            if (Kinds != null)
            {
                while (Kinds.Count < Tubes.Count) Kinds.Add(0);
                if (Kinds.Count > Tubes.Count) Kinds.RemoveRange(Tubes.Count, Kinds.Count - Tubes.Count);
                for (int i = 0; i < Kinds.Count; i++) if (Kinds[i] < 0 || Kinds[i] > 4 || Kinds[i] == (int)TubeKind.Spare) Kinds[i] = 0;
                if (Kinds.TrueForAll(k => k == 0)) Kinds = null;
            }
            if (HeavyFlags != null)
            {
                while (HeavyFlags.Count < Tubes.Count) HeavyFlags.Add(0);
                if (HeavyFlags.Count > Tubes.Count) HeavyFlags.RemoveRange(Tubes.Count, HeavyFlags.Count - Tubes.Count);
                for (int i = 0; i < HeavyFlags.Count; i++) HeavyFlags[i] = HeavyFlags[i] != 0 && Tubes[i].Count > 0 ? 1 : 0;   // a weight needs a ball
                if (HeavyFlags.TrueForAll(h => h == 0)) HeavyFlags = null;
            }
        }

        public EditorLevel Clone() => JsonConvert.DeserializeObject<EditorLevel>(JsonConvert.SerializeObject(this));

        // ------------------------------------------------------------------ queries

        public TubeKind KindOf(int tube) => Kinds != null && tube < Kinds.Count ? (TubeKind)Kinds[tube] : TubeKind.Normal;

        /// <summary>The tube's bottom ball is heavy.</summary>
        public bool IsHeavy(int tube) => HeavyFlags != null && tube < HeavyFlags.Count && HeavyFlags[tube] != 0 && Tubes[tube].Count > 0;

        /// <summary>Heavy flags for a <see cref="LevelData"/> (null when no ball is heavy).</summary>
        public List<int> HeavyOrNull()
        {
            if (!HasHeavy) return null;
            var l = new List<int>(Tubes.Count);
            for (int i = 0; i < Tubes.Count; i++) l.Add(IsHeavy(i) ? 1 : 0);
            return l;
        }

        public int CountKind(TubeKind kind)
        {
            int n = 0;
            for (int i = 0; i < Tubes.Count; i++) if (KindOf(i) == kind) n++;
            return n;
        }

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

        /// <summary>
        /// Hidden-ball mask for a <see cref="LevelData"/> made from this layout: top-first rows of <see cref="Capacity"/>
        /// cells parallel to <see cref="LevelFiller.ToTopFirst"/>, 1 where the cell is a "?" (the filler picks its colour,
        /// the game keeps it hidden until it is the top ball). Null when there is no "?".
        /// </summary>
        public List<List<int>> HiddenTopFirst()
        {
            if (!HasRandom) return null;
            var rows = new List<List<int>>(Tubes.Count);
            foreach (var t in Tubes)
            {
                var row = new List<int>(Capacity);
                for (int s = Capacity - 1; s >= 0; s--) row.Add(s < t.Count && t[s] == Random ? 1 : 0);
                rows.Add(row);
            }
            return rows;
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
                d.Tubes.Add(new TubeSpec { Preset = PresetOf(i), Kind = KindOf(i), Heavy = IsHeavy(i), Capacity = Capacity, Fixed = fixedCells, FillCount = t.Count });
            }
            return d;
        }

        /// <summary>Structural check (Magic Sort EditorManager.IsLevelValid): ball counts, colour range, sizes.</summary>
        public bool Validate(out string error)
        {
            if (Tubes.Count == 0) { error = "Add at least one tube."; return false; }
            if (Tubes.Count > BoardState.MaxTubes) { error = $"At most {BoardState.MaxTubes} tubes."; return false; }
            for (int i = 0; i < Tubes.Count; i++)
            {
                if (KindOf(i) == TubeKind.InOnly && Tubes[i].Count > 0) { error = $"Tube {i + 1} is in-only: it must start empty."; return false; }
                if (KindOf(i) == TubeKind.OutOnly && Tubes[i].Count == 0) { error = $"Tube {i + 1} is out-only: it must start with balls."; return false; }
                if (KindOf(i) == TubeKind.Hidden && Tubes.Count == 1) { error = $"Tube {i + 1} is hidden: it needs a neighbour that can be completed to open it."; return false; }
            }
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
        /// stack appends to the stack, since balls cannot float. Erasing the bottom ball takes its weight with it.
        /// </summary>
        public bool Paint(int tube, int slot, int value)
        {
            var t = Tubes[tube];
            if (value == 0)
            {
                if (slot < 0 || slot >= t.Count) return false;
                t.RemoveAt(slot);
                if (slot == 0) SetHeavy(tube, false);
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

        /// <summary>Fills the whole tube with one value (0 = empty tube, Random = luck tube). Emptying it drops the weight.</summary>
        public bool FillTube(int tube, int value)
        {
            var t = Tubes[tube];
            var next = value == 0 ? new List<int>() : Filled(Capacity, value);
            if (t.Count == next.Count && t.TrueForAll(c => c == value)) return false;
            Tubes[tube] = next;
            if (value == 0) SetHeavy(tube, false);
            if (value > ColorCount) ColorCount = value;
            return true;
        }

        /// <summary>Makes the tube's bottom ball heavy (needs a ball) or normal again.</summary>
        public bool SetHeavy(int tube, bool heavy)
        {
            if (tube < 0 || tube >= Tubes.Count) return false;
            if (heavy && Tubes[tube].Count == 0) return false;
            if (IsHeavy(tube) == heavy) return false;
            if (HeavyFlags == null)
            {
                if (!heavy) return false;
                HeavyFlags = Filled(Tubes.Count, 0);
            }
            HeavyFlags[tube] = heavy ? 1 : 0;
            if (HeavyFlags.TrueForAll(h => h == 0)) HeavyFlags = null;
            return true;
        }

        /// <summary>
        /// Sets a tube's one-way rule. An in-only tube is emptied (it starts empty by definition); an empty tube made
        /// out-only is filled with "?" so it starts with balls. Normal and hidden keep the contents.
        /// </summary>
        public bool SetKind(int tube, TubeKind kind)
        {
            if (tube < 0 || tube >= Tubes.Count || KindOf(tube) == kind) return false;
            if (kind == TubeKind.Normal)
            {
                Kinds[tube] = 0;
                if (Kinds.TrueForAll(k => k == 0)) Kinds = null;
                return true;
            }
            if (Kinds == null) Kinds = Filled(Tubes.Count, 0);
            Kinds[tube] = (int)kind;
            if (kind == TubeKind.InOnly) { Tubes[tube].Clear(); SetHeavy(tube, false); }
            else if (kind == TubeKind.OutOnly && Tubes[tube].Count == 0) Tubes[tube] = Filled(Capacity, Random);
            return true;
        }

        public bool AddTube()
        {
            if (Tubes.Count >= BoardState.MaxTubes) return false;
            Tubes.Add(new List<int>());
            Kinds?.Add(0);
            HeavyFlags?.Add(0);
            return true;
        }

        public bool RemoveTube(int tube)
        {
            if (tube < 0 || tube >= Tubes.Count) return false;
            Tubes.RemoveAt(tube);
            if (Kinds != null)
            {
                Kinds.RemoveAt(tube);
                if (Kinds.TrueForAll(k => k == 0)) Kinds = null;
            }
            if (HeavyFlags != null)
            {
                HeavyFlags.RemoveAt(tube);
                if (HeavyFlags.TrueForAll(h => h == 0)) HeavyFlags = null;
            }
            return true;
        }

        public bool SwapTubes(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a >= Tubes.Count || b >= Tubes.Count) return false;
            (Tubes[a], Tubes[b]) = (Tubes[b], Tubes[a]);
            if (Kinds != null) (Kinds[a], Kinds[b]) = (Kinds[b], Kinds[a]);
            if (HeavyFlags != null) (HeavyFlags[a], HeavyFlags[b]) = (HeavyFlags[b], HeavyFlags[a]);
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
            HeavyFlags = null;
            return any;
        }
    }
}
