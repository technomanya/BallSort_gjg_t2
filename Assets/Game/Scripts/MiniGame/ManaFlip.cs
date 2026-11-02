using System;
using System.Collections.Generic;
using UnityEngine;

namespace BallSortGame.MiniGame
{
    /// <summary>
    /// Rules and saved state of Mana Flip. A play is earned by a streak of first-try level wins, the length set per
    /// stage. A retry drops the streak to zero. In a play the player flips cards until one item shows
    /// <see cref="MatchCount"/> copies, which pays that item's prize and advances the stage. A half-done play resumes.
    /// </summary>
    [Serializable]
    public class ManaFlip
    {
        const string CardsKey = "ballsort.mana_flip.cards";
        const string RevealedKey = "ballsort.mana_flip.revealed";
        const string StageKey = "ballsort.mana_flip.stage";
        const string StreakKey = "ballsort.mana_flip.streak";
        const string PlaysKey = "ballsort.mana_flip.plays";

        public const int ItemKinds = 3;

        public enum FlipResult { Refused, Item, Reward }

        [Serializable]
        public class Stage
        {
            [Tooltip("One-try level wins in a row that earn a play at this stage.")]
            [Min(1)] public int Levels = 3;
            [Tooltip("Coins for matching potion, crystal, star.")]
            public int[] Rewards = { 100, 150, 200 };
        }

        [Header("Stages")]
        [Tooltip("Played in order, wrapping back to the first.")]
        public Stage[] Stages =
        {
            new Stage { Levels = 3, Rewards = new[] { 100, 150, 200 } },
            new Stage { Levels = 5, Rewards = new[] { 100, 200, 500 } },
            new Stage { Levels = 7, Rewards = new[] { 200, 500, 1000 } },
        };

        [Header("Board")]
        [Tooltip("Cards of each item on the board.")]
        [Min(1)] public int Copies = 4;
        [Tooltip("Open copies of one item that end the play and pay its prize.")]
        [Min(1)] public int MatchCount = 3;

        public int CardCount => ItemKinds * Copies;
        public int StageCount => Stages != null && Stages.Length > 0 ? Stages.Length : 1;
        /// <summary>Stage of the next play, 0-based.</summary>
        public int StageIndex { get; private set; }
        /// <summary>One-try wins in a row towards the next play.</summary>
        public int Streak { get; private set; }
        /// <summary>Plays earned and not yet finished.</summary>
        public int Plays { get; private set; }
        public bool CanPlay => Plays > 0;
        public int StageLevels => CurrentStage.Levels;
        public int LevelsToGo => Mathf.Max(0, StageLevels - Streak);
        public int[] StageRewards => CurrentStage.Rewards ?? new int[0];
        public int Reward(int item) => item >= 0 && item < StageRewards.Length ? StageRewards[item] : 0;

        /// <summary>Item of each card, 0 to ItemKinds - 1.</summary>
        public IReadOnlyList<int> Cards => _cards;
        public bool IsRevealed(int card) => card >= 0 && card < _revealed.Length && _revealed[card];
        /// <summary>Copies of the item revealed so far on this board.</summary>
        public int Found(int item) => item >= 0 && item < ItemKinds ? _found[item] : 0;
        /// <summary>The item that ended the play, or -1 while the play is on.</summary>
        public int Matched { get; private set; } = -1;
        public bool RoundOver => Matched >= 0;

        Stage CurrentStage => Stages != null && Stages.Length > 0 ? Stages[Mathf.Clamp(StageIndex, 0, Stages.Length - 1)] : new Stage();

        int[] _cards = new int[0];
        bool[] _revealed = new bool[0];
        readonly int[] _found = new int[ItemKinds];
        readonly System.Random _rng = new System.Random();

        public void Load()
        {
            StageIndex = Mathf.Clamp(PlayerPrefs.GetInt(StageKey, 0), 0, StageCount - 1);
            Streak = Mathf.Max(0, PlayerPrefs.GetInt(StreakKey, 0));
            Plays = Mathf.Max(0, PlayerPrefs.GetInt(PlaysKey, 0));

            string saved = PlayerPrefs.GetString(CardsKey, "");
            var parts = saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != CardCount)
            {
                NewBoard();
                return;
            }
            _cards = new int[CardCount];
            for (int i = 0; i < CardCount; i++)
                if (!int.TryParse(parts[i], out _cards[i]) || _cards[i] < 0 || _cards[i] >= ItemKinds) { NewBoard(); return; }
            int mask = PlayerPrefs.GetInt(RevealedKey, 0);
            _revealed = new bool[CardCount];
            Array.Clear(_found, 0, _found.Length);
            Matched = -1;
            for (int i = 0; i < CardCount; i++)
            {
                _revealed[i] = (mask & (1 << i)) != 0;
                if (_revealed[i]) _found[_cards[i]]++;
            }
            if (AnyMatched() >= 0) NewBoard();   // a finished play is never shown again
        }

        public void NewBoard()
        {
            _cards = new int[CardCount];
            int n = 0;
            for (int item = 0; item < ItemKinds; item++)
                for (int c = 0; c < Copies; c++) _cards[n++] = item;
            for (int i = _cards.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }
            _revealed = new bool[CardCount];
            Array.Clear(_found, 0, _found.Length);
            Matched = -1;
            Save();
        }

        /// <summary>
        /// Counts a won level towards the streak. Returns true when it earns a play. Wins do not count while a play
        /// is still waiting.
        /// </summary>
        public bool OnLevelWon(bool oneShot)
        {
            if (Plays > 0) return false;
            Streak = oneShot ? Streak + 1 : 0;
            bool earned = Streak >= StageLevels;
            if (earned)
            {
                Streak = 0;
                Plays++;
                if (RoundOver) NewBoard();   // the last play's matched board was never dealt again
            }
            Save();
            return earned;
        }

        /// <summary>
        /// Turns a card over. A Reward result ends the play, and the reward is already paid.
        /// </summary>
        public FlipResult TryFlip(int card, out int item, out int reward)
        {
            reward = 0;
            item = card >= 0 && card < _cards.Length ? _cards[card] : -1;
            if (!CanPlay || RoundOver || IsRevealed(card) || item < 0) return FlipResult.Refused;
            _revealed[card] = true;
            _found[item]++;
            if (_found[item] < MatchCount)
            {
                Save();
                return FlipResult.Item;
            }
            Matched = item;
            reward = Reward(item);
            Wallet.Add(reward);
            Plays--;
            StageIndex = (StageIndex + 1) % StageCount;
            Save();
            return FlipResult.Reward;
        }

        int AnyMatched()
        {
            for (int i = 0; i < ItemKinds; i++) if (_found[i] >= MatchCount) return i;
            return -1;
        }

        void Save()
        {
            PlayerPrefs.SetString(CardsKey, string.Join(",", _cards));
            int mask = 0;
            for (int i = 0; i < _revealed.Length; i++) if (_revealed[i]) mask |= 1 << i;
            PlayerPrefs.SetInt(RevealedKey, mask);
            PlayerPrefs.SetInt(StageKey, StageIndex);
            PlayerPrefs.SetInt(StreakKey, Streak);
            PlayerPrefs.SetInt(PlaysKey, Plays);
            PlayerPrefs.Save();
        }

        /// <summary>Debug: clears the streak, stage and earned plays.</summary>
        public void ResetProgress()
        {
            StageIndex = 0;
            Streak = 0;
            Plays = 0;
            NewBoard();
        }

        /// <summary>Debug only.</summary>
        public void GrantPlay()
        {
            Plays++;
            if (RoundOver) NewBoard();
            Save();
        }
    }
}
