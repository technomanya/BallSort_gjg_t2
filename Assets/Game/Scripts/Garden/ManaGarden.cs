using UnityEngine;

namespace BallSortGame.Garden
{
    /// <summary>
    /// Rules of the Mana Garden: a row of plant slots, each unlocked by winning the level written on it. Winning that
    /// level hands the player its seed (the win screen shows the seed in place of the coin, see
    /// <see cref="ManaGardenView"/>); the plant button of the garden then plants the seeds one by one into their slots.
    /// Unlocked and planted slots are bit masks kept in PlayerPrefs.
    /// </summary>
    public class ManaGarden
    {
        const string UnlockedKey = "ballsort.garden.unlocked";
        const string PlantedKey = "ballsort.garden.planted";

        /// <summary>Level that unlocks each slot (the mockup's 3 + 2 grid).</summary>
        public static readonly int[] DefaultLevels = { 5, 10, 15, 20, 25 };

        public int[] Levels = DefaultLevels;

        int _unlocked, _planted;

        public int SlotCount => Levels.Length;

        public int LevelOf(int slot) => slot >= 0 && slot < Levels.Length ? Levels[slot] : 0;

        public bool IsUnlocked(int slot) => (_unlocked >> slot & 1) != 0;

        public bool IsPlanted(int slot) => (_planted >> slot & 1) != 0;

        /// <summary>Seeds in hand: unlocked plants not planted yet (the green badge).</summary>
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
        }

        /// <summary>Unlocks every slot whose level has been reached; true when a new plant was unlocked.</summary>
        public bool OnLevelWon(int level)
        {
            int before = _unlocked;
            for (int i = 0; i < SlotCount; i++)
                if (Levels[i] <= level) _unlocked |= 1 << i;
            if (_unlocked == before) return false;
            Save();
            return true;
        }

        /// <summary>Plants the next seed in hand; the slot it went into, or -1 with none left.</summary>
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

        public void Reset()
        {
            _unlocked = _planted = 0;
            Save();
        }

        void Save()
        {
            PlayerPrefs.SetInt(UnlockedKey, _unlocked);
            PlayerPrefs.SetInt(PlantedKey, _planted);
            PlayerPrefs.Save();
        }
    }
}
