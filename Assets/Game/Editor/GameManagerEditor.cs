using BallSortGame.Gameplay;
using UnityEditor;
using UnityEngine;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// GameManager inspector: the normal fields plus the saved level, a button that jumps to the Test Level
    /// during play, and a button that clears the saved level.
    /// </summary>
    [CustomEditor(typeof(GameManager))]
    public class GameManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var gm = (GameManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Progress", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Saved level", GameManager.SavedLevel.ToString());
            if (Application.isPlaying) EditorGUILayout.LabelField("Current level", gm.CurrentLevel.ToString());

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button($"Play Level {gm.testLevel}")) gm.PlayTestLevel();
                }
                if (GUILayout.Button("Reset Saved Level")) GameManager.ClearSavedLevel();
            }
        }
    }
}
