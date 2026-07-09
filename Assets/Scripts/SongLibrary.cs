using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace RhythmGame
{
    /// <summary>
    /// Finds playable songs in StreamingAssets: a sheet-music file (.mscx/.mscz) paired with
    /// an audio file of the same base name (.wav/.mp3/.mp4/.ogg/etc.) in the same folder.
    ///
    /// Two APIs, for two different jobs:
    ///  - ScanStreamingAssets() walks the filesystem directly with System.IO. Only works where
    ///    the platform exposes StreamingAssets as real files (Desktop, iOS, Editor) — on
    ///    Android it's packed inside the compressed APK and can't be listed this way, so this
    ///    silently finds nothing there. Used at edit time by SongManifestBuilder to bake the
    ///    manifest file below; not meant to run standalone in a shipped Android build.
    ///  - LoadAsync() reads that pre-baked manifest.json via UnityWebRequest instead, which
    ///    works uniformly on every platform including Android. This is what SongSelectMenu
    ///    actually calls at runtime.
    /// </summary>
    public static class SongLibrary
    {
        public const string ManifestRelativePath = "Music/manifest.json";

        private static readonly string[] SheetMusicExtensions = { ".mscx", ".mscz" };
        private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg", ".aiff", ".aif", ".m4a", ".mp4" };

        [Serializable]
        public class SongEntry
        {
            public string displayName;
            public string sheetMusicFileName; // relative to StreamingAssets
            public string audioFileName;      // relative to StreamingAssets
        }

        [Serializable]
        public class Manifest
        {
            public List<SongEntry> songs = new List<SongEntry>();
        }

        // Runtime-facing: reads the manifest SongManifestBuilder already baked into
        // StreamingAssets, via UnityWebRequest so it works on every platform (including
        // Android, where a direct filesystem scan can't see inside the APK at all).
        public static IEnumerator LoadAsync(Action<List<SongEntry>> onLoaded)
        {
            string path = Path.Combine(Application.streamingAssetsPath, ManifestRelativePath);

            using var request = UnityWebRequest.Get(path);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[SongLibrary] Failed to load song manifest '{path}': {request.error}. " +
                    "In the Editor, run Tools > RGen > Rebuild Song Manifest.");
                onLoaded(new List<SongEntry>());
                yield break;
            }

            Manifest manifest = JsonUtility.FromJson<Manifest>(request.downloadHandler.text);
            onLoaded(manifest?.songs ?? new List<SongEntry>());
        }

        // Edit-time (or Desktop/iOS-only) filesystem scan — see class doc comment above.
        public static List<SongEntry> ScanStreamingAssets()
        {
            var entries = new List<SongEntry>();
            string root = Application.streamingAssetsPath;
            if (!Directory.Exists(root)) return entries;

            var sheetFiles = new List<string>();
            foreach (string ext in SheetMusicExtensions)
                sheetFiles.AddRange(Directory.GetFiles(root, "*" + ext, SearchOption.AllDirectories));

            foreach (string sheetPath in sheetFiles)
            {
                string dir = Path.GetDirectoryName(sheetPath);
                string baseName = Path.GetFileNameWithoutExtension(sheetPath);

                string audioPath = null;
                foreach (string ext in AudioExtensions)
                {
                    string candidate = Path.Combine(dir, baseName + ext);
                    if (File.Exists(candidate)) { audioPath = candidate; break; }
                }

                if (audioPath == null)
                {
                    Debug.LogWarning($"[SongLibrary] No matching audio file (.wav/.mp3/.mp4/.ogg) found for '{sheetPath}'. Skipping.");
                    continue;
                }

                entries.Add(new SongEntry
                {
                    displayName = baseName,
                    sheetMusicFileName = ToStreamingAssetsRelative(root, sheetPath),
                    audioFileName = ToStreamingAssetsRelative(root, audioPath)
                });
            }

            entries.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
            return entries;
        }

        private static string ToStreamingAssetsRelative(string root, string fullPath)
        {
            string rel = fullPath.Substring(root.Length).Replace('\\', '/');
            return rel.TrimStart('/');
        }
    }
}
