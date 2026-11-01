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
    /// designer can get at it, so levels made on one device reach the team. On Android the file goes to the public
    /// Downloads folder through MediaStore (no storage permission needed on Android 10+) and the system share sheet
    /// opens with it; in the Unity Editor and on desktop it goes to the Desktop and the JSON is also put on the
    /// clipboard. A copy is always kept under <see cref="SaveDir"/>.
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
                Status($"Saved {ShareFileName} to {where}.", EditorUI.Good);
#elif UNITY_IOS && !UNITY_EDITOR
                Status($"Saved {ShareFileName} to {local}.", EditorUI.Good);
#else
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string path = string.IsNullOrEmpty(desktop) ? local : Path.Combine(desktop, ShareFileName);
                File.WriteAllText(path, json);
                GUIUtility.systemCopyBuffer = json;
                Status($"Saved {ShareFileName} to {path} (and copied to the clipboard).", EditorUI.Good);
#endif
            }
            catch (Exception e)
            {
                Status($"Share failed: {e.Message}. Copy kept at {local}.", EditorUI.Bad);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string DownloadsDir = "Download";   // MediaStore relative path of the public Downloads folder

        /// <summary>Writes the JSON to Downloads/levels.json and opens the share sheet with it. Returns where it went.</summary>
        static string ShareAndroid(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                if (version.GetStatic<int>("SDK_INT") < 29) return ShareAndroidLegacy(bytes);
            }

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var downloads = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
            using (var collection = downloads.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
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
                {
                    using (var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                    {
                        stream.Call("write", (sbyte[])(Array)bytes);
                        stream.Call("flush");
                        stream.Call("close");
                    }

                    using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND"))
                    using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                    {
                        intent.Call<AndroidJavaObject>("setType", "application/json").Dispose();
                        intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.STREAM", uri).Dispose();
                        intent.Call<AndroidJavaObject>("addFlags", 1).Dispose();   // FLAG_GRANT_READ_URI_PERMISSION
                        using (var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Share " + ShareFileName))
                            activity.Call("startActivity", chooser);
                    }
                }
            }
            return DownloadsDir + "/" + ShareFileName;
        }

        /// <summary>Android 9 and older: plain file in the public Downloads folder (needs the storage permission).</summary>
        static string ShareAndroidLegacy(byte[] bytes)
        {
            string dir;
            using (var env = new AndroidJavaClass("android.os.Environment"))
            using (var file = env.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", "Download"))
                dir = file.Call<string>("getAbsolutePath");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, ShareFileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }
#endif
    }
}
