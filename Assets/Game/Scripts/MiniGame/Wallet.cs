using System;
using UnityEngine;

namespace BallSortGame.MiniGame
{
    /// <summary>
    /// Player coin balance, kept in PlayerPrefs. Coins come from filled tubes (banked when the level is won, see LevelCoinsView) and Jump Path prizes and are spent on jumps.
    /// </summary>
    public static class Wallet
    {
        const string Key = "ballsort.coins";

        /// <summary>Raised with the new balance after every change.</summary>
        public static event Action<int> Changed;

        public static int Coins => PlayerPrefs.GetInt(Key, 0);

        /// <summary>Gives a first-time player <paramref name="startingCoins"/>; does nothing once a balance exists.</summary>
        public static void Init(int startingCoins)
        {
            if (PlayerPrefs.HasKey(Key)) return;
            Set(Mathf.Max(0, startingCoins));
        }

        public static void Add(int amount)
        {
            if (amount <= 0) return;
            Set(Coins + amount);
        }

        public static bool TrySpend(int amount)
        {
            if (amount < 0 || Coins < amount) return false;
            Set(Coins - amount);
            return true;
        }

        static void Set(int value)
        {
            PlayerPrefs.SetInt(Key, value);
            PlayerPrefs.Save();
            Changed?.Invoke(value);
        }
    }
}
