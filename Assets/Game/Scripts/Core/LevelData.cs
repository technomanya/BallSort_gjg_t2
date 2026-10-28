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
