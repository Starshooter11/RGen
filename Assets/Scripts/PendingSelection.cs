using System.Xml;

namespace RhythmGame
{
    /// <summary>
    /// Carries the song + settings chosen in SongSelectMenu (MainMenu scene) across to
    /// whichever gameplay scene loads next. Plain static state survives a
    /// SceneManager.LoadScene just fine at runtime (no domain reload happens for that), so a
    /// MonoBehaviour/DontDestroyOnLoad object isn't needed for this handoff.
    /// </summary>
    public static class PendingSelection
    {
        public static bool HasPending { get; private set; }

        public static SongLibrary.SongEntry Song { get; private set; }
        public static XmlDocument SheetMusicDoc { get; private set; }
        public static int GlobalPartIndex { get; private set; }
        public static ClefFilter Clef { get; private set; }
        public static int MaxNotesAtOnce { get; private set; }
        public static float SpeedLevel { get; private set; }
        public static float PlaybackSpeed { get; private set; }

        public static void Set(SongLibrary.SongEntry song, XmlDocument sheetMusicDoc, int globalPartIndex,
            ClefFilter clef, int maxNotesAtOnce, float speedLevel, float playbackSpeed)
        {
            Song            = song;
            SheetMusicDoc   = sheetMusicDoc;
            GlobalPartIndex = globalPartIndex;
            Clef            = clef;
            MaxNotesAtOnce  = maxNotesAtOnce;
            SpeedLevel      = speedLevel;
            PlaybackSpeed   = playbackSpeed;
            HasPending      = true;
        }

        // Called once GameManager has consumed the selection, so returning to the menu and
        // coming back without picking a new song doesn't replay a stale one.
        public static void Clear()
        {
            HasPending    = false;
            Song          = null;
            SheetMusicDoc = null;
        }
    }
}
