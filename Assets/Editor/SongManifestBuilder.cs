using System.IO;
using RhythmGame;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RhythmGame.EditorTools
{
    /// <summary>
    /// Android (and any platform that packs StreamingAssets into a compressed archive) can't
    /// list files there at runtime, so SongLibrary.ScanStreamingAssets()'s direct filesystem
    /// walk finds nothing on-device. This bakes that same scan into a manifest.json that
    /// SongLibrary.LoadAsync() reads via UnityWebRequest instead, which works everywhere.
    ///
    /// Runs automatically before every build and right before entering Play mode, so the
    /// manifest never goes stale just because someone forgot to rebuild it after adding a song.
    /// </summary>
    public class SongManifestBuilder : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        [MenuItem("Tools/RGen/Rebuild Song Manifest")]
        public static void RebuildManifest()
        {
            var songs = SongLibrary.ScanStreamingAssets();
            var manifest = new SongLibrary.Manifest { songs = songs };
            string json = JsonUtility.ToJson(manifest, prettyPrint: true);

            string manifestPath = Path.Combine(Application.streamingAssetsPath, SongLibrary.ManifestRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
            File.WriteAllText(manifestPath, json);
            AssetDatabase.Refresh();

            Debug.Log($"[SongManifestBuilder] Wrote {songs.Count} song(s) to {manifestPath}");
        }

        public void OnPreprocessBuild(BuildReport report) => RebuildManifest();
    }

    [InitializeOnLoad]
    internal static class SongManifestAutoRefresh
    {
        static SongManifestAutoRefresh()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    SongManifestBuilder.RebuildManifest();
            };
        }
    }
}
