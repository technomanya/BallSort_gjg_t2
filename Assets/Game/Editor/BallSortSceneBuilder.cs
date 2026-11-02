using BallSortGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Word-art helpers. They edit the hand-laid scene in place and never rebuild it.
    /// </summary>
    public static class BallSortSceneBuilder
    {
        internal static readonly Color Gold = new Color(1f, 0.72f, 0.12f, 1f);         // popup titles, LEVEL text
        internal static readonly Color Navy = new Color(0.12f, 0.06f, 0.30f, 1f);       // outlines, panel titles

        /// <summary>
        /// Gives the HUD's popup titles and buttons their word pictures, styles the LEVEL labels and applies the skin font.
        /// Moves nothing and is safe to run again.
        /// </summary>
        [MenuItem("BallSort/Apply Word Art")]
        public static void ApplyWordArt()
        {
            var hud = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("Apply Word Art: no GameHUD in the open scene.");
                return;
            }
            // the scene is hand-laid, so ask first and never save (Undo backs it out)
            if (!EditorUtility.DisplayDialog("Apply Word Art",
                    "Replace the HUD's lettering with the word pictures? This edits the hand-laid scene (LEVEL labels, popup titles, button labels). The scene is left unsaved.",
                    "Apply", "Cancel")) return;
            // use the skin as is: rebuilding it would revert hand-made skin changes
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>("Assets/Game/Resources/UISkin.asset");
            if (skin == null) skin = NewUISkinBuilder.BuildSkin();
            Undo.RegisterFullObjectHierarchyUndo(hud.gameObject, "Apply Word Art");
            ApplyWordArt(hud, skin);
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Debug.Log("Apply Word Art: lettering pictures applied (scene not saved).");
        }

        internal static void ApplyWordArt(GameHUD hud, UISkin skin)
        {
            if (skin.font != null)
                foreach (var t in hud.GetComponentsInChildren<Text>(true)) { t.font = skin.font; t.fontStyle = WordArt.Style(t.fontStyle); EditorUtility.SetDirty(t); }
            if (hud.levelText != null) LevelText(hud.levelText);
            if (hud.mainMenu != null && hud.mainMenu.levelText != null) LevelText(hud.mainMenu.levelText);
            WordArt.Picture(hud.successTitle, skin.wordWellDone);
            WordArt.Picture(ButtonLabel(hud.nextButton), skin.wordContinue);
            WordArt.Picture(ButtonLabel(hud.noMoveContinueButton), skin.wordContinue);
            WordArt.Picture(ButtonLabel(hud.noMoveHomeButton), skin.wordHome);
            WordArt.Picture(ButtonLabel(hud.noMoveReplayButton), skin.wordTryAgain);
            var loseTitle = hud.noMoveView != null ? hud.noMoveView.transform.Find("panel/title") : null;
            if (loseTitle != null) WordArt.Picture(loseTitle.GetComponent<Text>(), skin.wordOutOfMoves);
        }

        static Text ButtonLabel(Button b)
        {
            var label = b != null ? b.transform.Find("label") : null;
            return label != null ? label.GetComponent<Text>() : null;
        }

        // LEVEL titles are drawn 1.5x the original size.
        internal const float LevelScale = 1.5f;

        // Styles a "LEVEL n" label as plain gold text with a navy outline.
        internal static Text LevelText(Text text)
        {
            if (text.fontSize < 60) text.fontSize = Mathf.RoundToInt(text.fontSize * LevelScale);   // only scale a label that was not scaled yet
            text.enabled = true;
            text.gameObject.SetActive(true);
            text.color = Gold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            if (!text.TryGetComponent(out Outline outline)) outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = Navy;
            outline.effectDistance = new Vector2(3f, -3f);
            EditorUtility.SetDirty(text);
            return text;
        }
    }
}
