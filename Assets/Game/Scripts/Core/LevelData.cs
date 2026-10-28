using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace BallSortGame.Core
{
    /// <summary>
    /// Level pack format of the reference game ("StageData" JSON under Resources/data/):
    /// { "stage_id": "lv_1_100", "levels": [ { "puzzle_id": "...", "tubes": [[top..bottom], ...], "steps": [[from,to],...] } ] }
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
            return BoardState.FromTopFirst(tubes);
        }
    }

    public static class LevelLoader
    {
        /// <summary>Loads a stage file from Resources (e.g. "data/lv_1_100").</summary>
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
