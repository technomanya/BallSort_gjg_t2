using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace BallSortGame.Core
{
    /// <summary>
    /// Level pack JSON under Resources/data/. Tubes are listed TOP-first, 0 = empty slot, colours 1..N,
    /// capacity = array length.
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
        /// <summary>Per-tube kind (0 normal, 1 in-only, 2 out-only, 4 hidden). Absent = all normal.</summary>
        [JsonProperty("tube_kinds", NullValueHandling = NullValueHandling.Ignore)] public List<int> TubeKinds;
        /// <summary>
        /// Hidden ("?") balls, parallel to <see cref="Tubes"/>, top-first. 1 = the colour stays hidden until the ball
        /// reaches the top. Absent = all visible.
        /// </summary>
        [JsonProperty("hidden", NullValueHandling = NullValueHandling.Ignore)] public List<List<int>> Hidden;
        /// <summary>
        /// Heavy balls, one bit mask per tube: bit s = the s-th ball from the bottom (so 1 = the bottom ball).
        /// Absent = none.
        /// </summary>
        [JsonProperty("heavy", NullValueHandling = NullValueHandling.Ignore)] public List<int> Heavy;

        // --- optional, written by the level generator
        /// <summary>Difficulty bucket 1..7 of this fill.</summary>
        [JsonProperty("difficulty", NullValueHandling = NullValueHandling.Ignore)] public int? Difficulty;
        /// <summary>Other fills of the same design, played on retries.</summary>
        [JsonProperty("variants", NullValueHandling = NullValueHandling.Ignore)] public List<LevelData> Variants;
        /// <summary>Easier fills used after <see cref="DlbCap"/> failed attempts (dynamic level balancing).</summary>
        [JsonProperty("dlb_variants", NullValueHandling = NullValueHandling.Ignore)] public List<LevelData> DlbVariants;
        [JsonProperty("dlb_cap", NullValueHandling = NullValueHandling.Ignore)] public int? DlbCap;
        [JsonProperty("hard", NullValueHandling = NullValueHandling.Ignore)] public bool? IsHard;
        [JsonProperty("super_hard", NullValueHandling = NullValueHandling.Ignore)] public bool? IsSuperHard;

        /// <summary>This fill plus its <see cref="Variants"/>.</summary>
        [JsonIgnore] public int SeedCount => 1 + (Variants?.Count ?? 0);

        public LevelData SeedAt(int index) => index <= 0 || Variants == null ? this : Variants[Math.Min(index - 1, Variants.Count - 1)];

        /// <summary>
        /// Fill for this attempt (0 = first try): seed k on attempt k, then a random one other than
        /// <paramref name="lastPlayedIndex"/>. After <see cref="DlbCap"/> attempts the dlb fills take over,
        /// their indices offset by 1000.
        /// </summary>
        public LevelData ForAttempt(int attempt, int lastPlayedIndex, System.Random rng, out int index)
        {
            var (useDlb, i) = SeedSelector.Pick(SeedCount, DlbVariants?.Count ?? 0, DlbCap ?? 0, attempt, lastPlayedIndex >= 1000 ? lastPlayedIndex - 1000 : lastPlayedIndex, rng);
            if (useDlb) { index = 1000 + i; return DlbVariants[i]; }
            index = i;
            return SeedAt(i);
        }

        public TubeKind KindOf(int tube) => TubeKinds != null && tube < TubeKinds.Count ? (TubeKind)TubeKinds[tube] : TubeKind.Normal;

        /// <summary>Whether this ball starts hidden.</summary>
        public bool IsHidden(int tube, int topFirstIndex) =>
            Hidden != null && tube < Hidden.Count && Hidden[tube] != null && topFirstIndex < Hidden[tube].Count && Hidden[tube][topFirstIndex] != 0;

        /// <summary>Bit s = the s-th ball from the bottom.</summary>
        public int HeavyMask(int tube) => Heavy != null && tube < Heavy.Count && Heavy[tube] > 0 ? Heavy[tube] : 0;

        public bool IsHeavyAt(int tube, int ballFromBottom) => ballFromBottom >= 0 && ballFromBottom < 31 && ((HeavyMask(tube) >> ballFromBottom) & 1) != 0;

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
        /// <summary>
        /// Where the in-app level editor saves the pack on a device, since the APK's Resources are read-only.
        /// A development build loads it instead of the bundled pack, so edits survive a restart.
        /// </summary>
        public static string SavedPackPath(string resourcePath) =>
            Path.Combine(Application.persistentDataPath, "LevelEditor", Path.GetFileName(resourcePath) + ".json");

        /// <summary>Side file with the hash of the bundled pack the save was made on.</summary>
        static string BasePath(string resourcePath) => SavedPackPath(resourcePath) + ".base";

        /// <summary>FNV-1a hash of the bundled pack's text, or "" when there is none.</summary>
        static string BundledHash(string resourcePath)
        {
            var text = Resources.Load<TextAsset>(resourcePath);
            if (text == null) return "";
            ulong h = 1469598103934665603UL;
            foreach (char c in text.text) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        /// <summary>Records which bundled pack the level editor's save was made on.</summary>
        public static void MarkSaved(string resourcePath)
        {
            try { File.WriteAllText(BasePath(resourcePath), BundledHash(resourcePath)); }
            catch (Exception e) { Debug.LogWarning("Level editor: could not mark the saved pack: " + e.Message); }
        }

        static bool SavedOnCurrentPack(string resourcePath)
        {
            try { return File.Exists(BasePath(resourcePath)) && File.ReadAllText(BasePath(resourcePath)).Trim() == BundledHash(resourcePath); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Loads a stage file (e.g. "data/levels"). A development build on a device prefers the level editor's save,
        /// if it was made on the current bundled pack.
        /// </summary>
        public static StageData LoadStage(string resourcePath)
        {
            if (!Application.isEditor && Debug.isDebugBuild)
            {
                string saved = SavedPackPath(resourcePath);
                // a save made on an older bundled pack (an earlier APK) is stale: the new build's pack wins
                if (File.Exists(saved) && !SavedOnCurrentPack(resourcePath))
                    Debug.Log($"Level editor save {saved} was made with a different bundled pack; using the bundled pack.");
                else if (File.Exists(saved))
                {
                    try
                    {
                        var stage = JsonConvert.DeserializeObject<StageData>(File.ReadAllText(saved));
                        if (stage != null && stage.Levels.Count > 0)
                        {
                            Debug.Log($"Level pack loaded from the level editor's save: {saved} ({stage.Levels.Count} levels)");
                            return stage;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Level editor save {saved} could not be read, using the bundled pack: {e.Message}");
                    }
                }
            }

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
