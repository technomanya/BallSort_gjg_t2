using UnityEngine;

namespace BallSortGame.Garden
{
    /// <summary>
    /// Rules of the Mana Garden. Winning a slot's level gives its seed, which the player then plants. A planted
    /// flower starts young and grows into its lit form <see cref="GrowLevels"/> levels later.
    /// Unlocked and planted slots are bit masks in PlayerPrefs.
    /// </summary>
    public class ManaGarden
    {
        const string UnlockedKey = "ballsort.garden.unlocked";
        const string PlantedKey = "ballsort.garden.planted";
        const string BestKey = "ballsort.garden.best";

        /// <summary>Level that unlocks each slot.</summary>
        public static readonly int[] DefaultLevels = { 5, 10, 15, 20, 25 };

        public int[] Levels = DefaultLevels;

        /// <summary>Levels past a slot's unlock level before its flower grows lit.</summary>
        public int GrowLevels = 5;

        int _unlocked, _planted, _best;

        public int SlotCount => Levels.Length;

        public int LevelOf(int slot) => slot >= 0 && slot < Levels.Length ? Levels[slot] : 0;

        public bool IsUnlocked(int slot) => (_unlocked >> slot & 1) != 0;

        public bool IsPlanted(int slot) => (_planted >> slot & 1) != 0;

        public int GrowLevelOf(int slot) => LevelOf(slot) + GrowLevels;

        /// <summary>True when the slot's flower has grown into its lit form.</summary>
        public bool IsLit(int slot) => IsPlanted(slot) && _best >= GrowLevelOf(slot);

        /// <summary>Unlocked plants not planted yet.</summary>
        public int Seeds
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SlotCount; i++) if (IsUnlocked(i) && !IsPlanted(i)) n++;
                return n;
            }
        }

        public void Load()
        {
            int all = (1 << SlotCount) - 1;
            _unlocked = PlayerPrefs.GetInt(UnlockedKey, 0) & all;
            _planted = PlayerPrefs.GetInt(PlantedKey, 0) & _unlocked;
            // older saves have no growth key, so seed it from the saved level
            _best = PlayerPrefs.HasKey(BestKey) ? PlayerPrefs.GetInt(BestKey, 0)
                : Mathf.Max(0, Gameplay.GameManager.SavedLevel - 1);
        }

        /// <summary>
        /// Unlocks reached slots and records the level for growth. True when a new plant unlocked.
        /// </summary>
        public bool OnLevelWon(int level)
        {
            int before = _unlocked, bestBefore = _best;
            for (int i = 0; i < SlotCount; i++)
                if (Levels[i] <= level) _unlocked |= 1 << i;
            _best = Mathf.Max(_best, level);
            if (_unlocked == before && _best == bestBefore) return false;
            Save();
            return _unlocked != before;
        }

        /// <summary>Plants the next seed in hand. Returns its slot, or -1 with none left.</summary>
        public int Plant()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!IsUnlocked(i) || IsPlanted(i)) continue;
                _planted |= 1 << i;
                Save();
                return i;
            }
            return -1;
        }

        /// <summary>Plants one slot. False when it is locked or already planted.</summary>
        public bool Plant(int slot)
        {
            if (slot < 0 || slot >= SlotCount || !IsUnlocked(slot) || IsPlanted(slot)) return false;
            _planted |= 1 << slot;
            Save();
            return true;
        }

        public void Reset()
        {
            _unlocked = _planted = _best = 0;
            Save();
        }

        void Save()
        {
            PlayerPrefs.SetInt(UnlockedKey, _unlocked);
            PlayerPrefs.SetInt(PlantedKey, _planted);
            PlayerPrefs.SetInt(BestKey, _best);
            PlayerPrefs.Save();
        }
    }
}
