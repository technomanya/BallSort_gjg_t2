using System;
using System.IO;
using System.Text;
using BallSortGame.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace BallSortGame.LevelEditor
{
    /// <summary>
    /// SHARE: writes the pack the editor saves into (every level, seeds included) as <c>levels.json</c> somewhere the
    /// designer can get at it, and shows the path in the status line. Nothing is sent anywhere. On Android the file
    /// goes to the public Downloads folder through MediaStore (no storage permission needed on Android 10+), so it
    /// shows up in the Files app and over USB; in the Unity Editor and on desktop it goes to the Desktop and the JSON
    /// is also put on the clipboard. A copy is always kept under <see cref="SaveDir"/>.
    /// </summary>
    public partial class LevelEditorManager
    {
        public const string ShareFileName = "levels.json";

        /// <summary>The pack as JSON: the game's loaded pack, else the pack file on disk.</summary>
        string PackJson()
        {
            StageData stage = game != null ? game.Stage : null;
            if (stage == null && File.Exists(PackPath)) return File.ReadAllText(PackPath);
            if (stage == null || stage.Levels.Count == 0) return null;
            return JsonConvert.SerializeObject(stage);
        }

        void Share()
        {
            string json;
            try { json = PackJson(); }
            catch (Exception e) { Status("Share failed: " + e.Message, EditorUI.Bad); return; }
            if (string.IsNullOrEmpty(json)) { Status("Nothing to share yet: save a level first.", EditorUI.Bad); return; }

            // always keep a copy next to the pack
            string local = Path.Combine(SaveDir, ShareFileName);
            try
            {
                Directory.CreateDirectory(SaveDir);
                File.WriteAllText(local, json);
            }
            catch (Exception e) { Debug.LogWarning("Level editor: could not write " + local + ": " + e.Message); }

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                string where = ShareAndroid(json);
                Status($"Saved {where}", EditorUI.Good);
#elif UNITY_IOS && !UNITY_EDITOR
                Status($"Saved {local}", EditorUI.Good);
#else
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string path = string.IsNullOrEmpty(desktop) ? local : Path.Combine(desktop, ShareFileName);
                File.WriteAllText(path, json);
                GUIUtility.systemCopyBuffer = json;
                Status($"Saved {path} (and copied to the clipboard)", EditorUI.Good);
#endif
            }
            catch (Exception e)
            {
                Status($"Share failed: {e.Message}. Copy kept at {local}", EditorUI.Bad);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string DownloadsDir = "Download";   // MediaStore relative path of the public Downloads folder

        /// <summary>
        /// Writes the JSON to the public Downloads folder and returns the full path. Android 10+ goes through MediaStore
        /// (no permission needed); Android 9 and older write the plain file, which needs the storage permission, and
        /// otherwise the copy under <see cref="SaveDir"/> is what the designer gets.
        /// </summary>
        static string ShareAndroid(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            string downloads;
            using (var env = new AndroidJavaClass("android.os.Environment"))
            using (var dir = env.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", DownloadsDir))
                downloads = dir.Call<string>("getAbsolutePath");
            string fullPath = downloads + "/" + ShareFileName;

            int sdk;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
            if (sdk < 29)
            {
                Directory.CreateDirectory(downloads);
                File.WriteAllBytes(fullPath, bytes);
                return fullPath;
            }

            var jbytes = new sbyte[bytes.Length];   // JNI wants a Java byte[], which Unity maps to sbyte[]
            Buffer.BlockCopy(bytes, 0, jbytes, 0, bytes.Length);
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var collectionClass = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
            using (var collection = collectionClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
            {
                // replace an earlier levels.json of ours so the name stays "levels" (another app's file cannot be touched;
                // the system then names the new one "levels (1).json")
                try { resolver.Call<int>("delete", collection, "_display_name=? AND relative_path=?", new[] { ShareFileName, DownloadsDir + "/" }); }
                catch (Exception) { }

                AndroidJavaObject uri;
                using (var values = new AndroidJavaObject("android.content.ContentValues"))
                {
                    values.Call("put", "_display_name", ShareFileName);
                    values.Call("put", "mime_type", "application/json");
                    values.Call("put", "relative_path", DownloadsDir);
                    uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
                }
                if (uri == null) throw new IOException("MediaStore refused the file");
                using (uri)
                using (var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                {
                    stream.Call("write", jbytes);
                    stream.Call("flush");
                    stream.Call("close");
                }
            }
            return fullPath;
        }
#endif
    }
}
