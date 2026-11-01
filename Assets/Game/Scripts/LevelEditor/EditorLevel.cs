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
    /// in-only tubes start empty, out-only tubes start with balls. Any ball may be heavy
    /// (<see cref="HeavyFlags"/>, the cannonball obstacle): still a colour, but it moves alone and only into an empty tube.
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
        /// <summary>Per-tube bit mask, parallel to <see cref="Tubes"/>: bit s = the ball in slot s (bottom-first) is heavy; null = no heavy ball.</summary>
        [JsonProperty("heavy", NullValueHandling = NullValueHandling.Ignore)] public List<int> HeavyFlags;
        /// <summary>
        /// Per-tube, bottom-first 0/1 parallel to <see cref="Tubes"/>: 1 = the ball keeps its colour in the layout but
        /// the game hides it (grey "?") until it becomes the top ball. Null when no coloured ball is hidden.
        /// </summary>
        [JsonProperty("hidden_balls", NullValueHandling = NullValueHandling.Ignore)] public List<List<int>> HiddenBalls;
        /// <summary>Seeds the generator made from this layout (main fill + variants); dropped on every edit.</summary>
        [JsonProperty("seeds", NullValueHandling = NullValueHandling.Ignore)] public LevelData Seeds;

        public int TubeCount => Tubes.Count;
        public int RandomSlots => UsageOf(Random);
        public bool HasRandom => RandomSlots > 0;
        public int HeavyCount
        {
            get { int n = 0; for (int i = 0; i < Tubes.Count; i++) for (int s = 0; s < Tubes[i].Count; s++) if (IsHeavy(i, s)) n++; return n; }
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
            for (int i = 0; i < data.Tubes.Count; i++)
            {
                var topFirst = data.Tubes[i];
                var t = new List<int>();
                for (int j = topFirst.Count - 1; j >= 0; j--)
                    if (topFirst[j] > 0)
                    {
                        if (data.IsHidden(i, j)) lv.SetBallHidden(i, t.Count, true, building: true);
                        t.Add(topFirst[j]);
                        lv.ColorCount = Math.Max(lv.ColorCount, topFirst[j]);
                    }
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
                for (int i = 0; i < HeavyFlags.Count; i++) HeavyFlags[i] = HeavyFlags[i] < 0 ? 0 : HeavyFlags[i] & ((1 << Tubes[i].Count) - 1);   // a weight needs a ball
                if (HeavyFlags.TrueForAll(h => h == 0)) HeavyFlags = null;
            }
            if (HiddenBalls != null)
            {
                while (HiddenBalls.Count < Tubes.Count) HiddenBalls.Add(new List<int>());
                if (HiddenBalls.Count > Tubes.Count) HiddenBalls.RemoveRange(Tubes.Count, HiddenBalls.Count - Tubes.Count);
                for (int i = 0; i < HiddenBalls.Count; i++)
                {
                    var h = HiddenBalls[i] ?? (HiddenBalls[i] = new List<int>());
                    while (h.Count < Tubes[i].Count) h.Add(0);
                    if (h.Count > Tubes[i].Count) h.RemoveRange(Tubes[i].Count, h.Count - Tubes[i].Count);
                    for (int s = 0; s < h.Count; s++) h[s] = h[s] != 0 && Tubes[i][s] > 0 ? 1 : 0;   // a "?" cell is hidden anyway
                }
                TrimHidden();
            }
        }

        public EditorLevel Clone() => JsonConvert.DeserializeObject<EditorLevel>(JsonConvert.SerializeObject(this));

        // ------------------------------------------------------------------ queries

        public TubeKind KindOf(int tube) => Kinds != null && tube < Kinds.Count ? (TubeKind)Kinds[tube] : TubeKind.Normal;

        /// <summary>The ball in <paramref name="slot"/> (bottom-first) of the tube is heavy.</summary>
        public bool IsHeavy(int tube, int slot) => slot >= 0 && slot < Tubes[tube].Count && ((HeavyMaskOf(tube) >> slot) & 1) != 0;

        /// <summary>Heavy balls of the tube as a bit mask over its balls (bit s = slot s, bottom-first).</summary>
        public int HeavyMaskOf(int tube)
        {
            if (HeavyFlags == null || tube >= HeavyFlags.Count || HeavyFlags[tube] <= 0) return 0;
            return HeavyFlags[tube] & ((1 << Tubes[tube].Count) - 1);
        }

        /// <summary>The game hides this ball: a "?" cell, or a coloured ball flagged in <see cref="HiddenBalls"/>.</summary>
        public bool IsBallHidden(int tube, int slot)
        {
            if (tube < 0 || tube >= Tubes.Count || slot < 0 || slot >= Tubes[tube].Count) return false;
            if (Tubes[tube][slot] == Random) return true;
            return HiddenBalls != null && tube < HiddenBalls.Count && HiddenBalls[tube] != null && slot < HiddenBalls[tube].Count && HiddenBalls[tube][slot] != 0;
        }

        /// <summary>Number of balls the game hides: "?" cells plus hidden coloured balls.</summary>
        public int HiddenBallCount
        {
            get { int n = 0; for (int i = 0; i < Tubes.Count; i++) for (int s = 0; s < Tubes[i].Count; s++) if (IsBallHidden(i, s)) n++; return n; }
        }

        public bool HasHiddenBalls => HasRandom || HiddenBalls != null;

        /// <summary>Heavy masks for a <see cref="LevelData"/> (null when no ball is heavy).</summary>
        public List<int> HeavyOrNull()
        {
            if (!HasHeavy) return null;
            var l = new List<int>(Tubes.Count);
            for (int i = 0; i < Tubes.Count; i++) l.Add(HeavyMaskOf(i));
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
        /// cells parallel to <see cref="LevelFiller.ToTopFirst"/>, 1 where the cell is a "?" (the filler picks its colour)
        /// or a hidden coloured ball; the game keeps those hidden until they are the top ball. Null when none is hidden.
        /// </summary>
        public List<List<int>> HiddenTopFirst()
        {
            if (!HasHiddenBalls) return null;
            var rows = new List<List<int>>(Tubes.Count);
            for (int i = 0; i < Tubes.Count; i++)
            {
                var row = new List<int>(Capacity);
                for (int s = Capacity - 1; s >= 0; s--) row.Add(IsBallHidden(i, s) ? 1 : 0);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Replaces every "?" cell with the colour <paramref name="board"/> (a fill of this layout) put there, so the
        /// designer sees the balls and can hide chosen ones with <see cref="SetBallHidden"/>. False when nothing changed.
        /// </summary>
        public bool ApplyFill(BoardState board)
        {
            bool any = false;
            for (int i = 0; i < Tubes.Count && i < board.TubeCount; i++)
            {
                var t = Tubes[i];
                for (int s = 0; s < t.Count; s++)
                {
                    if (t[s] != Random) continue;
                    int c = board.ColorAt(i, s);
                    if (c <= 0) continue;
                    t[s] = c;
                    if (c > ColorCount) ColorCount = c;
                    any = true;
                }
            }
            return any;
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
                d.Tubes.Add(new TubeSpec { Preset = PresetOf(i), Kind = KindOf(i), HeavyMask = HeavyMaskOf(i), Capacity = Capacity, Fixed = fixedCells, FillCount = t.Count });
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
        /// stack appends to the stack, since balls cannot float. Erasing a ball takes its weight with it.
        /// </summary>
        public bool Paint(int tube, int slot, int value)
        {
            var t = Tubes[tube];
            if (value == 0)
            {
                if (slot < 0 || slot >= t.Count) return false;
                t.RemoveAt(slot);
                if (HiddenBalls != null && tube < HiddenBalls.Count && slot < HiddenBalls[tube].Count) { HiddenBalls[tube].RemoveAt(slot); TrimHidden(); }
                RemoveHeavySlot(tube, slot);
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
            if (HiddenBalls != null && tube < HiddenBalls.Count) { HiddenBalls[tube].Clear(); TrimHidden(); }
            if (value == 0) ClearHeavy(tube);
            if (value > ColorCount) ColorCount = value;
            return true;
        }

        /// <summary>Makes the ball in <paramref name="slot"/> (bottom-first) heavy, or normal again. Needs a ball there.</summary>
        public bool SetHeavy(int tube, int slot, bool heavy)
        {
            if (tube < 0 || tube >= Tubes.Count || slot < 0 || slot >= Tubes[tube].Count) return false;
            if (IsHeavy(tube, slot) == heavy) return false;
            if (HeavyFlags == null)
            {
                if (!heavy) return false;
                HeavyFlags = Filled(Tubes.Count, 0);
            }
            int mask = HeavyMaskOf(tube);
            HeavyFlags[tube] = heavy ? mask | (1 << slot) : mask & ~(1 << slot);
            TrimHeavy();
            return true;
        }

        /// <summary>Takes every weight off the tube.</summary>
        void ClearHeavy(int tube)
        {
            if (HeavyFlags == null || tube >= HeavyFlags.Count) return;
            HeavyFlags[tube] = 0;
            TrimHeavy();
        }

        /// <summary>A ball left slot <paramref name="slot"/>: its weight goes with it and the weights above drop one slot.</summary>
        void RemoveHeavySlot(int tube, int slot)
        {
            if (HeavyFlags == null || tube >= HeavyFlags.Count || HeavyFlags[tube] <= 0) return;
            int mask = HeavyFlags[tube];
            int below = mask & ((1 << slot) - 1);
            int above = mask >> (slot + 1);
            HeavyFlags[tube] = below | (above << slot);
            TrimHeavy();
        }

        void TrimHeavy()
        {
            if (HeavyFlags != null && HeavyFlags.TrueForAll(h => h == 0)) HeavyFlags = null;
        }

        /// <summary>
        /// Hides (or shows) a coloured ball in the game; the layout keeps its colour. A "?" cell cannot be changed here.
        /// <paramref name="building"/> skips the checks while <see cref="FromLevelData"/> is still adding the tubes.
        /// </summary>
        public bool SetBallHidden(int tube, int slot, bool hidden, bool building = false)
        {
            if (tube < 0 || slot < 0) return false;
            if (!building)
            {
                if (tube >= Tubes.Count || slot >= Tubes[tube].Count || Tubes[tube][slot] == Random) return false;
                if (IsBallHidden(tube, slot) == hidden) return false;
            }
            if (HiddenBalls == null)
            {
                if (!hidden) return false;
                HiddenBalls = new List<List<int>>();
            }
            while (HiddenBalls.Count <= tube) HiddenBalls.Add(new List<int>());
            var h = HiddenBalls[tube] ?? (HiddenBalls[tube] = new List<int>());
            while (h.Count <= slot) h.Add(0);
            h[slot] = hidden ? 1 : 0;
            TrimHidden();
            return true;
        }

        /// <summary>Drops the hidden-ball table when no flag is set.</summary>
        void TrimHidden()
        {
            if (HiddenBalls == null) return;
            foreach (var h in HiddenBalls) if (h != null) foreach (var v in h) if (v != 0) return;
            HiddenBalls = null;
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
            if (kind == TubeKind.InOnly) { Tubes[tube].Clear(); ClearHeavy(tube); if (HiddenBalls != null && tube < HiddenBalls.Count) { HiddenBalls[tube].Clear(); TrimHidden(); } }
            else if (kind == TubeKind.OutOnly && Tubes[tube].Count == 0) Tubes[tube] = Filled(Capacity, Random);
            return true;
        }

        public bool AddTube()
        {
            if (Tubes.Count >= BoardState.MaxTubes) return false;
            Tubes.Add(new List<int>());
            Kinds?.Add(0);
            HeavyFlags?.Add(0);
            HiddenBalls?.Add(new List<int>());
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
            if (HiddenBalls != null && tube < HiddenBalls.Count) { HiddenBalls.RemoveAt(tube); TrimHidden(); }
            return true;
        }

        public bool SwapTubes(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a >= Tubes.Count || b >= Tubes.Count) return false;
            (Tubes[a], Tubes[b]) = (Tubes[b], Tubes[a]);
            if (Kinds != null) (Kinds[a], Kinds[b]) = (Kinds[b], Kinds[a]);
            if (HeavyFlags != null) (HeavyFlags[a], HeavyFlags[b]) = (HeavyFlags[b], HeavyFlags[a]);
            if (HiddenBalls != null && a < HiddenBalls.Count && b < HiddenBalls.Count) (HiddenBalls[a], HiddenBalls[b]) = (HiddenBalls[b], HiddenBalls[a]);
            return true;
        }

        /// <summary>Changes the tube height; balls above the new height are removed.</summary>
        public bool SetCapacity(int capacity)
        {
            capacity = Math.Max(1, Math.Min(BoardState.MaxDepth, capacity));
            if (capacity == Capacity) return false;
            Capacity = capacity;
            foreach (var t in Tubes) if (t.Count > capacity) t.RemoveRange(capacity, t.Count - capacity);
            if (HiddenBalls != null) { foreach (var h in HiddenBalls) if (h != null && h.Count > capacity) h.RemoveRange(capacity, h.Count - capacity); TrimHidden(); }
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
            HiddenBalls = null;
            return any;
        }
    }
}
