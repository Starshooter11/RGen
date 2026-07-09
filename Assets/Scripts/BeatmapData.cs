using System;
using System.Collections.Generic;

namespace RhythmGame
{
    [Serializable]
    public class BeatmapData
    {
        public List<NoteData> notes = new List<NoteData>();
        public float bpm;
        public float duration;
        public string songName;

        public BeatmapData(string songName, float bpm, float duration)
        {
            this.songName = songName;
            this.bpm = bpm;
            this.duration = duration;
        }
    }
}
