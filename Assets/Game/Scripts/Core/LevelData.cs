using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace BallSortGame.Core
{
    /// <summary>
    /// Level pack format of the reference game ("StageData" JSON under Resources/data/):
    /// { "stage_id": "levels", "levels": [ { "puzzle_id": "...", "tubes": [[top..bottom], ...], "steps": [[from,to],...] } ] }
    /// Tubes are listed TOP-first, 0 = empty slot, colors 1..N. Capacity = array length.
    /// </summary>
    [Serializable]
    public class StageData
    {
        [JsonProperty("stage_id")] public string StageId;
        [JsonProperty("levels")] public List<LevelData> Levels = new List<LevelData>();
    }

    [Serializable]
    public class LevelData
    {
        [JsonProperty("puzzle_id")] public string PuzzleId;
        [JsonProperty("tubes")] public List<List<int>> Tubes = new List<List<int>>();
        [JsonProperty("steps")] public List<List<int>> Steps = new List<List<int>>();
        /// <summary>Per-tube <see cref="TubeKind"/> (0 normal, 1 in-only, 2 out-only, 4 hidden); absent = every tube normal.</summary>
        [JsonProperty("tube_kinds", NullValueHandling = NullValueHandling.Ignore)] public List<int> TubeKinds;
        /// <summary>
        /// Hidden balls, parallel to <see cref="Tubes"/> (top-first, 1 = the ball's colour is hidden until it becomes the
        /// top ball of its tube); absent = every ball visible. The level editor's "?" cells.
        /// </summary>
        [JsonProperty("hidden", NullValueHandling = NullValueHandling.Ignore)] public List<List<int>> Hidden;
        /// <summary>
        /// Heavy balls (the cannonball obstacle), one flag per tube: 1 = the bottom ball of that tube is heavy. A heavy
        /// ball is a normal coloured ball that only ever sits at the bottom, so it only moves into an empty tube.
        /// Absent = no heavy ball.
        /// </summary>
        [JsonProperty("heavy", NullValueHandling = NullValueHandling.Ignore)] public List<int> Heavy;

        // ---- optional fields written by the level generator (absent in the reference packs) ----
        /// <summary>Difficulty bucket 1..7 (Magic Sort bot scale) of this fill.</summary>
        [JsonProperty("difficulty", NullValueHandling = NullValueHandling.Ignore)] public int? Difficulty;
        /// <summary>Alternative fills of the same design, played on retries (seed k on attempt k, then random, never the last one).</summary>
        [JsonProperty("variants", NullValueHandling = NullValueHandling.Ignore)] public List<LevelData> Variants;
        /// <summary>Easier fills used after <see cref="DlbCap"/> failed attempts (dynamic level balancing).</summary>
        [JsonProperty("dlb_variants", NullValueHandling = NullValueHandling.Ignore)] public List<LevelData> DlbVariants;
        [JsonProperty("dlb_cap", NullValueHandling = NullValueHandling.Ignore)] public int? DlbCap;
        [JsonProperty("hard", NullValueHandling = NullValueHandling.Ignore)] public bool? IsHard;
        [JsonProperty("super_hard", NullValueHandling = NullValueHandling.Ignore)] public bool? IsSuperHard;

        /// <summary>All fills of this level slot: this one first, then <see cref="Variants"/>.</summary>
        [JsonIgnore] public int SeedCount => 1 + (Variants?.Count ?? 0);

        public LevelData SeedAt(int index) => index <= 0 || Variants == null ? this : Variants[Math.Min(index - 1, Variants.Count - 1)];

        /// <summary>
        /// Fill to play on the given attempt (0 = first try), following the Magic Sort rule: seed k on attempt k,
        /// then a random one that is not <paramref name="lastPlayedIndex"/>; after <see cref="DlbCap"/> attempts the
        /// dlb fills take over. Returns the chosen fill and its index (dlb indices are offset by 1000).
        /// </summary>
        public LevelData ForAttempt(int attempt, int lastPlayedIndex, System.Random rng, out int index)
        {
            var (useDlb, i) = SeedSelector.Pick(SeedCount, DlbVariants?.Count ?? 0, DlbCap ?? 0, attempt, lastPlayedIndex >= 1000 ? lastPlayedIndex - 1000 : lastPlayedIndex, rng);
            if (useDlb) { index = 1000 + i; return DlbVariants[i]; }
            index = i;
            return SeedAt(i);
        }

        public TubeKind KindOf(int tube) => TubeKinds != null && tube < TubeKinds.Count ? (TubeKind)TubeKinds[tube] : TubeKind.Normal;

        /// <summary>Whether the ball at <paramref name="topFirstIndex"/> of <paramref name="tube"/> starts hidden.</summary>
        public bool IsHidden(int tube, int topFirstIndex) =>
            Hidden != null && tube < Hidden.Count && Hidden[tube] != null && topFirstIndex < Hidden[tube].Count && Hidden[tube][topFirstIndex] != 0;

        /// <summary>Whether the bottom ball of <paramref name="tube"/> is heavy.</summary>
        public bool IsHeavyBottom(int tube) => Heavy != null && tube < Heavy.Count && Heavy[tube] != 0;

        [JsonIgnore] public bool HasHeavyBalls
        {
            get { if (Heavy == null) return false; foreach (var h in Heavy) if (h != 0) return true; return false; }
        }

        [JsonIgnore] public bool HasHiddenBalls
        {
            get
            {
                if (Hidden == null) return false;
                foreach (var t in Hidden) if (t != null) foreach (var h in t) if (h != 0) return true;
                return false;
            }
        }

        [JsonIgnore] public int Capacity
        {
            get { int c = 0; foreach (var t in Tubes) c = Mathf.Max(c, t.Count); return c; }
        }

        [JsonIgnore] public int ColorCount
        {
            get
            {
                var set = new HashSet<int>();
                foreach (var t in Tubes) foreach (var v in t) if (v > 0) set.Add(v);
                return set.Count;
            }
        }

        public BoardState ToBoard()
        {
            var tubes = new List<IList<int>>(Tubes.Count);
            foreach (var t in Tubes) tubes.Add(t);
            return BoardState.FromTopFirst(tubes, TubeKinds, Heavy);
        }
    }

    public static class LevelLoader
    {
        /// <summary>Loads a stage file from Resources (e.g. "data/levels").</summary>
        public static StageData LoadStage(string resourcePath)
        {
            var text = Resources.Load<TextAsset>(resourcePath);
            if (text == null)
            {
                Debug.LogError($"Level pack not found in Resources: {resourcePath}");
                return null;
            }
            return JsonConvert.DeserializeObject<StageData>(text.text);
        }
    }
}
