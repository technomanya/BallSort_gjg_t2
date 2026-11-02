using System;
using System.IO;
using System.Text;
using BallSortGame.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace BallSortGame.LevelEditor
{
    /// <summary>
    /// SHARE: writes the whole pack as <c>levels.json</c> where the designer can reach it and shows the path. Nothing
    /// is sent anywhere. Android puts it in the public Downloads folder, the Unity Editor and desktop on the Desktop
    /// (and the clipboard).
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

            // own subfolder: SaveDir/levels.json is the dev-build pack, so a copy there would be played on every launch
            string shareDir = Path.Combine(SaveDir, "share");
            string local = Path.Combine(shareDir, ShareFileName);
            try
            {
                Directory.CreateDirectory(shareDir);
                File.WriteAllText(local, json);
            }
            catch (Exception e) { Debug.LogWarning("Level editor: could not write " + local + ": " + e.Message); }

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                string where = ShareAndroid(json);
                Status(where.EndsWith("/" + ShareFileName)
                    ? $"Saved {where}"
                    : $"Saved {where} (an older {ShareFileName} in Downloads is not this app's and could not be replaced; delete it by hand)", EditorUI.Good);
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
        /// Writes the JSON to the public Downloads folder and returns the path it ended up at. Android 10+ uses
        /// MediaStore, which needs no permission. If a levels.json we may not change is in the way, the system names
        /// ours "levels (1).json". Android 9 and older write the plain file.
        /// </summary>
        static string ShareAndroid(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            string downloads;
            using (var env = new AndroidJavaClass("android.os.Environment"))
            using (var dir = env.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", DownloadsDir))
                downloads = dir.Call<string>("getAbsolutePath");

            int sdk;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
            if (sdk < 29)
            {
                string fullPath = downloads + "/" + ShareFileName;
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
                var selectionArgs = new[] { ShareFileName, DownloadsDir + "/" };
                const string selection = "_display_name=? AND relative_path=?";

                // 1. an earlier levels.json: overwrite it in place when we may, else try to delete it
                foreach (long id in QueryIds(resolver, collection, selection, selectionArgs))
                {
                    using (var uri = ContentUri(collection, id))
                    {
                        if (WriteTo(resolver, uri, jbytes, "wt")) return downloads + "/" + ShareFileName;
                        // no C# nulls here: Unity's JNI bridge cannot pick a Java overload for a null
                        try { resolver.Call<int>("delete", collection, "_id=?", new[] { id.ToString() }); }
                        catch (Exception e) { Debug.LogWarning("Level editor: could not replace an earlier " + ShareFileName + " in Downloads: " + e.Message); }
                    }
                }

                // 2. a new file. The system renames it if a levels.json we cannot touch is still there.
                AndroidJavaObject newUri;
                using (var values = new AndroidJavaObject("android.content.ContentValues"))
                {
                    values.Call("put", "_display_name", ShareFileName);
                    values.Call("put", "mime_type", "application/json");
                    values.Call("put", "relative_path", DownloadsDir);
                    newUri = resolver.Call<AndroidJavaObject>("insert", collection, values);
                }
                if (newUri == null) throw new IOException("MediaStore refused the file");
                using (newUri)
                {
                    if (!WriteTo(resolver, newUri, jbytes, "w")) throw new IOException("MediaStore would not open the new file for writing");
                    string name = DisplayName(resolver, newUri) ?? ShareFileName;
                    string path = downloads + "/" + name;
                    if (name != ShareFileName)
                        Debug.LogWarning("Level editor: Downloads already holds a " + ShareFileName + " this app may not change, so the pack went to " + path);
                    return path;
                }
            }
        }

        static System.Collections.Generic.List<long> QueryIds(AndroidJavaObject resolver, AndroidJavaObject collection, string selection, string[] args)
        {
            var ids = new System.Collections.Generic.List<long>();
            try
            {
                using (var cursor = resolver.Call<AndroidJavaObject>("query", collection, new[] { "_id" }, selection, args, "_id ASC"))
                {
                    if (cursor == null) return ids;
                    while (cursor.Call<bool>("moveToNext")) ids.Add(cursor.Call<long>("getLong", 0));
                    cursor.Call("close");
                }
            }
            catch (Exception e) { Debug.LogWarning("Level editor: MediaStore query failed: " + e.Message); }
            return ids;
        }

        static AndroidJavaObject ContentUri(AndroidJavaObject collection, long id)
        {
            using (var contentUris = new AndroidJavaClass("android.content.ContentUris"))
                return contentUris.CallStatic<AndroidJavaObject>("withAppendedId", collection, id);
        }

        /// <summary>Writes to the MediaStore item ("wt" truncates first). False when it is not ours to write.</summary>
        static bool WriteTo(AndroidJavaObject resolver, AndroidJavaObject uri, sbyte[] jbytes, string mode)
        {
            try
            {
                using (var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri, mode))
                {
                    if (stream == null) return false;
                    stream.Call("write", jbytes);
                    stream.Call("flush");
                    stream.Call("close");
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Level editor: could not write " + ShareFileName + " (" + mode + "): " + e.Message);
                return false;
            }
        }

        static string DisplayName(AndroidJavaObject resolver, AndroidJavaObject uri)
        {
            try
            {
                using (var cursor = resolver.Call<AndroidJavaObject>("query", uri, new[] { "_display_name" }, "_id>0", new string[0], "_id ASC"))
                {
                    if (cursor == null || !cursor.Call<bool>("moveToFirst")) return null;
                    string name = cursor.Call<string>("getString", 0);
                    cursor.Call("close");
                    return name;
                }
            }
            catch (Exception) { return null; }
        }
#endif
    }
}
