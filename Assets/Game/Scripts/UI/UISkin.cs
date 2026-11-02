using UnityEngine;

namespace BallSortGame.UI
{
    /// <summary>
    /// UI kit sprites that code needs at runtime, kept in Resources/UISkin.asset.
    /// Filled by BallSort > Rebuild UI Skin.
    /// </summary>
    [CreateAssetMenu(menuName = "BallSort/UI Skin", fileName = "UISkin")]
    public class UISkin : ScriptableObject
    {
        public const string ResourcePath = "UISkin";

        [Header("Buttons and frames")]
        public Sprite buttonCircle;
        public Sprite buttonSquare;
        public Sprite buttonRect;
        public Sprite buttonRectGreen;
        public Sprite squareFrame;
        public Sprite greenCircle;
        [Tooltip("Gold stadium button: CONTINUE, prices, the LEVEL pill.")]
        public Sprite buttonPill;

        [Header("Popups")]
        [Tooltip("Big gold-framed panel.")]
        public Sprite popupPanel;
        [Tooltip("Small gold-framed panel.")]
        public Sprite pill;
        [Tooltip("Full-screen dim behind popups.")]
        public Sprite overlay;

        [Header("Rewards")]
        public Sprite rewardStar;
        public Sprite rewardPotion;
        public Sprite rewardCrystal;
        public Sprite rewardSeed;

        [Header("Mana Flip")]
        public Sprite rewardRow;
        [Tooltip("Glow behind a found reward.")]
        public Sprite rewardGlow;
        [Tooltip("Tile under a revealed reward.")]
        public Sprite cardBackFound;

        [Header("Items")]
        public Sprite coin;
        public Sprite logo;

        [Header("Main menu")]
        public Sprite background;
        public Sprite pot;
        public Sprite soil;
        [Tooltip("Garden flowers in slot order; pivot at the foot of the stem.")]
        public Sprite[] flowers = new Sprite[5];
        [Tooltip("The same flowers, grown.")]
        public Sprite[] flowersLit = new Sprite[5];

        [Header("Icons")]
        public Sprite iconClose;
        public Sprite iconSettings;
        public Sprite iconRestart;
        public Sprite iconUndo;
        public Sprite iconCheck;
        [Tooltip("Up arrow under one-way tubes (flipped for in-only).")]
        public Sprite iconArrow;
        public Sprite iconPlant;

        [Header("Lettering")]
        [Tooltip("Font for every Text, if one is set.")]
        public Font font;
        public Sprite wordContinue;
        public Sprite wordHome;
        public Sprite wordTryAgain;
        public Sprite wordWellDone;
        public Sprite wordOutOfMoves;
        public Sprite wordManaFlip;
        public Sprite wordManaGarden;

        static UISkin _current;
        static bool _loaded;

        /// <summary>The skin from Resources, or null if it hasn't been made yet.</summary>
        public static UISkin Current
        {
            get
            {
                if (!_loaded)
                {
                    _current = Resources.Load<UISkin>(ResourcePath);
                    _loaded = true;
                }
                return _current;
            }
        }
    }
}
