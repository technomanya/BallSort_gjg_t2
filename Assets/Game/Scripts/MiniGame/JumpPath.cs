using System;
using UnityEngine;

namespace BallSortGame.MiniGame
{
    /// <summary>
    /// Rules and saved progress of the Jump Path mini game. Each board is a path from START to a prize; every jump
    /// costs <see cref="JumpCost"/> coins and moves one stop. Board 1 needs <see cref="StartJumps"/> jumps and every
    /// next board <see cref="JumpsStep"/> more (3, 4, 5, ...) up to <see cref="MaxJumps"/>. Reaching the end pays
    /// <see cref="Prize"/> and opens the next board.
    /// </summary>
    [Serializable]
    public class JumpPath
    {
        const string BoardKey = "ballsort.jump_path.board";
        const string PositionKey = "ballsort.jump_path.position";

        [Header("Economy")]
        [Tooltip("Coins a first-time player starts with.")]
        public int StartingCoins = 30;
        [Tooltip("Coins spent on one jump.")]
        public int JumpCost = 10;

        [Header("Boards")]
        [Min(1)] public int StartJumps = 3;
        [Min(0)] public int JumpsStep = 1;
        [Min(1)] public int MaxJumps = 8;

        [Header("Prize")]
        public int BasePrize = 50;
        public int PrizeStep = 25;

        /// <summary>0-based board index.</summary>
        public int Board { get; private set; }
        /// <summary>Stop the player stands on: 0 = START, <see cref="JumpsRequired"/> = prize.</summary>
        public int Position { get; private set; }

        public int JumpsRequired => Mathf.Max(1, Mathf.Min(Mathf.Max(StartJumps, MaxJumps), StartJumps + JumpsStep * Board));
        public int Prize => BasePrize + PrizeStep * Board;
        public bool AtGoal => Position >= JumpsRequired;
        public bool CanAffordJump => Wallet.Coins >= JumpCost;

        public void Load()
        {
            Board = Mathf.Max(0, PlayerPrefs.GetInt(BoardKey, 0));
            Position = Mathf.Clamp(PlayerPrefs.GetInt(PositionKey, 0), 0, JumpsRequired);
        }

        /// <summary>Pays for one jump and moves one stop. False when at the goal or short of coins.</summary>
        public bool TryJump()
        {
            if (AtGoal || !Wallet.TrySpend(JumpCost)) return false;
            Position++;
            Save();
            return true;
        }

        /// <summary>Pays the prize and opens the next, longer board. Returns the coins paid (0 when not at the goal).</summary>
        public int CollectPrize()
        {
            if (!AtGoal) return 0;
            int prize = Prize;
            Board++;
            Position = 0;
            Save();
            Wallet.Add(prize);
            return prize;
        }

        public void ResetProgress()
        {
            Board = 0;
            Position = 0;
            Save();
        }

        void Save()
        {
            PlayerPrefs.SetInt(BoardKey, Board);
            PlayerPrefs.SetInt(PositionKey, Position);
            PlayerPrefs.Save();
        }
    }
}
