using System;

namespace RhythmGame
{
    [Serializable]
    public class NoteData
    {
        public float time;          // seconds from song start when note should be hit
        public int lane;            // 0-indexed lane
        public float holdDuration;  // seconds to hold after hitting (0 = tap note)

        public bool IsHold => holdDuration > 0f;

        public NoteData(float time, int lane, float holdDuration = 0f)
        {
            this.time = time;
            this.lane = lane;
            this.holdDuration = holdDuration;
        }
    }
}
