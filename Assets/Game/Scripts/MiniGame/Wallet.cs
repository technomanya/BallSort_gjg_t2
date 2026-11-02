using System;
using UnityEngine;

namespace BallSortGame.MiniGame
{
    /// <summary>The player's coins, saved in PlayerPrefs.</summary>
    public static class Wallet
    {
        const string Key = "ballsort.coins";
        const int StartingCoins = 30;   // what a new player starts with

        /// <summary>Raised with the new balance after every change.</summary>
        public static event Action<int> Changed;

        public static int Coins => PlayerPrefs.GetInt(Key, StartingCoins);

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
