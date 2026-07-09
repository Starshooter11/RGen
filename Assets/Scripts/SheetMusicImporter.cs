using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;
using UnityEngine;
using UnityEngine.Networking;

namespace RhythmGame
{
    public enum ClefFilter { Both, TrebleOnly, BassOnly }

    /// <summary>
    /// Parses a MuseScore file (.mscx or .mscz) into a BeatmapData.
    ///
    /// .mscx  — plain XML, place in Assets/StreamingAssets/
    /// .mscz  — zip containing an .mscx, same location
    ///
    /// PDF sheet music is NOT directly supported — PDF has no semantic note data.
    /// Convert PDF → MusicXML first using Audiveris (free, open source OMR):
    ///   https://github.com/Audiveris/audiveris
    /// Then open in MuseScore and export as .mscx.
    /// </summary>
    public static class SheetMusicImporter
    {
        // Minimum note duration (seconds) to be treated as a hold note
        private const float HoldThreshold = 0.3f;
        // Two notes closer than this in time are treated as simultaneous
        private const float SimultaneousEpsilon = 0.001f;
        // Grace note duration types to exclude
        private static readonly HashSet<string> GraceTypes = new HashSet<string>
            { "acciaccatura", "appoggiatura", "grace4", "grace8", "grace16", "grace32" };
        // Ornament/trill articulation names to exclude
        private static readonly HashSet<string> OrnamentTypes = new HashSet<string>
            { "trill", "trill_sharp", "mordent", "prallprall", "turn", "tremblement",
              "prallmordent", "upprall", "downprall" };

        // Raw note collected during parse
        private struct RawNote
        {
            public float time;
            public int   pitch;
            public float duration;  // seconds
            public int   staffId;   // 1 = treble, 2 = bass for piano
            public bool  isGrace;
            public bool  tiedToNext; // true if this note has an outgoing <Spanner type="Tie"><Tie> to its continuation
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        // A part/instrument available in a parsed document, for the player to choose from
        // before a beatmap is generated (see SongSelectMenu).
        public struct PartOption
        {
            public string displayName;  // e.g. "Piano" or "Piano (2)" when duplicated
            public int globalPartIndex; // index into the document's Part list, in document order
            public int staffCount;      // 1 = single staff, 2 = grand staff (separate hands)
        }

        // On Android, StreamingAssets lives inside the compressed APK and is exposed as a
        // "jar:file://...!/assets" URI — System.IO.File can't read through that, so this
        // path must go through UnityWebRequest instead. Desktop/iOS still get a plain
        // filesystem path and could use File I/O directly, but routing everything through
        // UnityWebRequest keeps one code path for all platforms.
        public static IEnumerator LoadDocumentFromStreamingAssetsAsync(string fileName, Action<XmlDocument> onLoaded)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);

            using var request = UnityWebRequest.Get(path);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[SheetMusicImporter] Failed to load '{path}': {request.error}");
                onLoaded(null);
                yield break;
            }

            byte[] data = request.downloadHandler.data;
            string ext  = Path.GetExtension(fileName).ToLowerInvariant();

            XmlDocument doc = ext switch
            {
                ".mscx" => LoadMscxDoc(data),
                ".mscz" => LoadMsczDoc(data),
                _       => LogUnsupportedDoc(ext)
            };
            onLoaded(doc);
        }

        // Lists the instruments/parts available in a parsed document, in document order.
        public static List<PartOption> ListParts(XmlDocument doc)
        {
            var parts = BuildPartList(doc);
            var counts = new Dictionary<string, int>();
            var options = new List<PartOption>();

            for (int i = 0; i < parts.Count; i++)
            {
                PartInfo p = parts[i];
                counts.TryGetValue(p.name, out int occurrence);
                string label = occurrence == 0 ? p.name : $"{p.name} ({occurrence + 1})";
                counts[p.name] = occurrence + 1;

                options.Add(new PartOption
                {
                    displayName = label,
                    globalPartIndex = i,
                    staffCount = p.staffCount
                });
            }

            return options;
        }

        public static BeatmapData FromMscx(string filePath, int laneCount, int globalPartIndex, ClefFilter clef = ClefFilter.Both)
        {
            var doc = new XmlDocument();
            doc.Load(filePath);
            return ParseMscx(doc, Path.GetFileNameWithoutExtension(filePath), laneCount, globalPartIndex, clef);
        }

        public static BeatmapData FromMscz(string filePath, int laneCount, int globalPartIndex, ClefFilter clef = ClefFilter.Both)
        {
            using var zip = ZipFile.OpenRead(filePath);
            foreach (var entry in zip.Entries)
            {
                if (!entry.Name.EndsWith(".mscx", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = entry.Open();
                var doc = new XmlDocument();
                doc.Load(stream);
                return ParseMscx(doc, Path.GetFileNameWithoutExtension(filePath), laneCount, globalPartIndex, clef);
            }
            Debug.LogError("[SheetMusicImporter] No .mscx found inside .mscz archive.");
            return null;
        }

        private static XmlDocument LoadMscxDoc(byte[] data)
        {
            var doc = new XmlDocument();
            using var stream = new MemoryStream(data);
            doc.Load(stream);
            return doc;
        }

        private static XmlDocument LoadMsczDoc(byte[] data)
        {
            using var zipStream = new MemoryStream(data);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (!entry.Name.EndsWith(".mscx", StringComparison.OrdinalIgnoreCase)) continue;
                using var entryStream = entry.Open();
                var doc = new XmlDocument();
                doc.Load(entryStream);
                return doc;
            }
            Debug.LogError("[SheetMusicImporter] No .mscx found inside .mscz archive.");
            return null;
        }

        private static XmlDocument LogUnsupportedDoc(string ext)
        {
            Debug.LogError($"[SheetMusicImporter] Unsupported format '{ext}'.\n" +
                "For PDF: use Audiveris (https://github.com/Audiveris/audiveris) to convert " +
                "to MusicXML, then open in MuseScore and export as .mscx.");
            return null;
        }

        // -------------------------------------------------------------------------
        // Core parse
        // -------------------------------------------------------------------------

        public static BeatmapData ParseMscx(XmlDocument doc, string songName,
            int laneCount, int globalPartIndex, ClefFilter clef, int maxNotesAtOnce = 2)
        {
            float beatsPerSecond = 2f;
            var beatmap  = new BeatmapData(songName, beatsPerSecond * 60f, 0f);
            var rawNotes = new List<RawNote>();
            float totalDuration = 0f;

            // Find which staff IDs belong to the requested part
            HashSet<int> partStaffIds = GetStaffIdsForPart(doc, globalPartIndex);
            if (partStaffIds.Count == 0)
            {
                Debug.LogError($"[SheetMusicImporter] Part index {globalPartIndex} not found.");
                return null;
            }

            // Within the part, assign local staff index (0 = first/treble, 1 = second/bass)
            // so ClefFilter works regardless of the global staff IDs.
            var sortedIds = new List<int>(partStaffIds);
            sortedIds.Sort();

            XmlNodeList staffNodes = doc.GetElementsByTagName("Staff");

            // Tempo markings apply to the whole score, but MuseScore only writes each one onto
            // whichever single staff it happens to be attached to — not duplicated across every
            // staff/part. If we only looked for tempo on the staff(s) belonging to whatever part
            // is currently selected, picking a part that doesn't include that one staff would
            // silently miss every tempo marking and fall back to the hardcoded 2f default,
            // stretching (or compressing) every note's duration and drifting further out of
            // sync with the audio as the piece goes on. So gather tempo-by-measure-index from
            // every staff in the document up front, independent of the selected part.
            var tempoByMeasureIndex = new Dictionary<int, float>();
            // Same reasoning applies to time signature: it's only needed to resolve a
            // whole-measure rest (durationType "measure") into an actual beat count, and to
            // estimate an empty measure's length, but MuseScore likewise only writes a TimeSig
            // once (on one staff) each time it changes, not on every staff/part.
            var beatsPerMeasureByMeasureIndex = new Dictionary<int, float>();
            foreach (XmlNode tempoStaffNode in staffNodes)
            {
                int measureIdx = 0;
                foreach (XmlNode measure in tempoStaffNode.SelectNodes("Measure"))
                {
                    XmlNode tempoNode = measure.SelectSingleNode(".//Tempo/tempo");
                    if (tempoNode != null && float.TryParse(tempoNode.InnerText,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float bps))
                    {
                        tempoByMeasureIndex[measureIdx] = bps;
                    }

                    XmlNode timeSigNode = measure.SelectSingleNode(".//TimeSig");
                    if (timeSigNode != null)
                    {
                        int sigN = int.TryParse(timeSigNode.SelectSingleNode("sigN")?.InnerText, out int n) ? n : 4;
                        int sigD = int.TryParse(timeSigNode.SelectSingleNode("sigD")?.InnerText, out int d) ? d : 4;
                        beatsPerMeasureByMeasureIndex[measureIdx] = sigN * (4f / sigD);
                    }

                    measureIdx++;
                }
            }

            // Expand startRepeat/endRepeat/Volta (first/second-ending) barlines into an actual
            // playback order of measure indices. Real recordings of a piece play repeats out —
            // a chart that instead walks measures once, straight through in document order,
            // drifts further out of sync with the audio every measure after the first repeat
            // point. Same "gather from every staff" reasoning as tempo/time-sig above: MuseScore
            // only writes these barline markers on whichever staff happens to have them.
            List<int> playbackOrder = BuildPlaybackOrder(staffNodes);

            foreach (XmlNode staffNode in staffNodes)
            {
                if (staffNode.Attributes?["id"] == null) continue;
                if (!int.TryParse(staffNode.Attributes["id"].Value, out int staffId)) continue;
                if (!partStaffIds.Contains(staffId)) continue;

                // Local index within this instrument (0 = treble, 1 = bass)
                int localIndex = sortedIds.IndexOf(staffId);
                // Remap to staffId convention used elsewhere: 1 = treble, 2 = bass
                int mappedStaffId = localIndex + 1;

                // Skip staves the user doesn't want
                if (clef == ClefFilter.TrebleOnly && mappedStaffId != 1) continue;
                if (clef == ClefFilter.BassOnly   && mappedStaffId != 2) continue;

                float currentTime = 0f;
                float beatsPerMeasure = 4f; // 4/4 until a TimeSig says otherwise
                XmlNodeList measureNodes = staffNode.SelectNodes("Measure");

                // Walk the expanded playback order (repeats unrolled, first/second endings
                // resolved) rather than raw document order, so a repeated measure is processed
                // once per actual playthrough and lands at the right point on the timeline.
                foreach (int measureIndex in playbackOrder)
                {
                    if (measureIndex >= measureNodes.Count) continue;
                    XmlNode measure = measureNodes[measureIndex];

                    if (tempoByMeasureIndex.TryGetValue(measureIndex, out float bps))
                    {
                        beatsPerSecond = bps;
                        beatmap.bpm = bps * 60f;
                    }
                    if (beatsPerMeasureByMeasureIndex.TryGetValue(measureIndex, out float bpm))
                        beatsPerMeasure = bpm;

                    float measureStart    = currentTime;
                    float measureDuration = 0f;

                    foreach (XmlNode child in measure.ChildNodes)
                    {
                        if (child.Name == "voice")
                        {
                            float voiceTime = measureStart;
                            // Tuplet/endTuplet are marker siblings, not containers: Tuplet declares
                            // a ratio that applies to every Chord/Rest until the matching endTuplet.
                            // A stack (rather than a simple on/off flag) also lets nested tuplets
                            // compound their ratios correctly.
                            var tupletRatioStack = new Stack<float>();

                            foreach (XmlNode vc in child.ChildNodes)
                            {
                                if (vc.Name == "Tuplet")
                                {
                                    float normal = 2f, actual = 3f;
                                    XmlNode nn = vc.SelectSingleNode("normalNotes");
                                    XmlNode an = vc.SelectSingleNode("actualNotes");
                                    if (nn != null && float.TryParse(nn.InnerText, out float n)) normal = n;
                                    if (an != null && float.TryParse(an.InnerText, out float a)) actual = a;
                                    float parentRatio = tupletRatioStack.Count > 0 ? tupletRatioStack.Peek() : 1f;
                                    tupletRatioStack.Push(parentRatio * (normal / actual));
                                    continue;
                                }

                                if (vc.Name == "endTuplet")
                                {
                                    if (tupletRatioStack.Count > 0) tupletRatioStack.Pop();
                                    continue;
                                }

                                float ratio = tupletRatioStack.Count > 0 ? tupletRatioStack.Peek() : 1f;
                                float adv = ProcessElement(vc, voiceTime, beatsPerSecond, beatsPerMeasure, mappedStaffId, rawNotes, ratio);
                                voiceTime += adv;
                            }

                            measureDuration = Mathf.Max(measureDuration, voiceTime - measureStart);
                        }
                        else
                        {
                            float adv = ProcessElement(child, currentTime, beatsPerSecond, beatsPerMeasure, mappedStaffId, rawNotes);
                            currentTime   += adv;
                            measureDuration += adv;
                        }
                    }

                    currentTime += measureDuration > 0f
                        ? measureDuration
                        : beatsPerMeasure / beatsPerSecond;
                }

                totalDuration = Mathf.Max(totalDuration, currentTime);
            }

            beatmap.duration = totalDuration;

            // Merge tied notes of the same pitch on the same staff
            rawNotes = MergeTies(rawNotes);

            // Filter and cap to at most maxNotesAtOnce simultaneous notes (combined across both
            // hands), prioritising shortest (fastest) non-grace notes
            var filtered = FilterAndCap(rawNotes, maxNotesAtOnce);

            // Assign lanes — split lane space by staff when both clefs are active
            AssignLanes(filtered, beatmap, laneCount, clef);

            beatmap.notes.Sort((a, b) => a.time.CompareTo(b.time));

            Debug.Log($"[SheetMusicImporter] {beatmap.notes.Count} notes, " +
                      $"BPM {beatmap.bpm:F1}, duration {beatmap.duration:F1}s, clef: {clef}");
            return beatmap;
        }

        // -------------------------------------------------------------------------
        // Repeat / volta (first & second ending) expansion
        // -------------------------------------------------------------------------

        // Expands startRepeat/endRepeat/Volta barlines into a concrete sequence of measure
        // indices to actually play, e.g. [0,1,2,3(1st ending),0,1,2,4(2nd ending),5,6,...].
        // Only handles the common, non-nested case (one repeat span at a time, single-measure
        // endings) — sufficient for real-world scores like Für Elise's opening-phrase repeat;
        // a repeat nested inside another repeat would need a stack instead of single ints here.
        private static List<int> BuildPlaybackOrder(XmlNodeList staffNodes)
        {
            int measureCount = 0;
            var startRepeats  = new HashSet<int>();
            var endRepeatCounts = new Dictionary<int, int>();
            var voltaEndings  = new Dictionary<int, HashSet<int>>();

            foreach (XmlNode staffNode in staffNodes)
            {
                int idx = 0;
                foreach (XmlNode measure in staffNode.SelectNodes("Measure"))
                {
                    if (measure.SelectSingleNode("startRepeat") != null)
                        startRepeats.Add(idx);

                    XmlNode endRepeatNode = measure.SelectSingleNode("endRepeat");
                    if (endRepeatNode != null)
                        endRepeatCounts[idx] = int.TryParse(endRepeatNode.InnerText, out int c) ? c : 2;

                    XmlNode endingsNode = measure.SelectSingleNode(".//Spanner[@type='Volta']/Volta/endings");
                    if (endingsNode != null)
                    {
                        var endings = new HashSet<int>();
                        foreach (string part in endingsNode.InnerText.Split(','))
                            if (int.TryParse(part.Trim(), out int e)) endings.Add(e);
                        if (endings.Count > 0) voltaEndings[idx] = endings;
                    }

                    idx++;
                }
                measureCount = Mathf.Max(measureCount, idx);
            }

            var order = new List<int>();
            if (startRepeats.Count == 0 && endRepeatCounts.Count == 0)
            {
                for (int idx = 0; idx < measureCount; idx++) order.Add(idx);
                return order;
            }

            var loopCounts = new Dictionary<int, int>();
            int repeatStart = 0;
            int lastEndRepeatIndex = -1;
            int i = 0;
            int safety = 0;
            int safetyLimit = Mathf.Max(1, measureCount) * 8; // generous cap against malformed repeat data
            while (i < measureCount && safety++ < safetyLimit)
            {
                if (startRepeats.Contains(i)) repeatStart = i;
                if (endRepeatCounts.ContainsKey(i)) lastEndRepeatIndex = i;

                loopCounts.TryGetValue(lastEndRepeatIndex, out int loopsForCurrentGroup);
                int pass = lastEndRepeatIndex >= 0 ? loopsForCurrentGroup + 1 : 1;

                bool skip = voltaEndings.TryGetValue(i, out var endings) && !endings.Contains(pass);
                if (!skip) order.Add(i);

                if (endRepeatCounts.TryGetValue(i, out int totalCount))
                {
                    loopCounts.TryGetValue(i, out int loopsSoFar);
                    if (loopsSoFar < totalCount - 1)
                    {
                        loopCounts[i] = loopsSoFar + 1;
                        i = repeatStart;
                        continue;
                    }
                }
                i++;
            }
            return order;
        }

        // -------------------------------------------------------------------------
        // Element processing
        // -------------------------------------------------------------------------

        private static float ProcessElement(XmlNode node, float startTime,
            float beatsPerSecond, float beatsPerMeasure, int staffId, List<RawNote> notes, float tupletRatio = 1f)
        {
            if (node.Name == "Chord")
            {
                bool isGrace = IsGraceChord(node);
                float dur    = isGrace ? 0f : NodeDurationSeconds(node, beatsPerSecond, beatsPerMeasure) * tupletRatio;

                if (!HasOrnamentOnly(node))
                {
                    foreach (XmlNode noteNode in node.SelectNodes("Note"))
                    {
                        XmlNode pitchNode = noteNode.SelectSingleNode("pitch");
                        if (pitchNode != null && int.TryParse(pitchNode.InnerText, out int pitch))
                        {
                            // A note starting a tie has a <Spanner type="Tie"> containing a <Tie>
                            // child (plus a <next> pointer); the continuation note it ties into
                            // instead has a <Spanner type="Tie"> containing only <prev>. Checking
                            // for the <Tie> child specifically distinguishes "this note ties
                            // forward" from "this note is the destination of an incoming tie".
                            bool tiedToNext = noteNode.SelectSingleNode("Spanner[@type='Tie']/Tie") != null;

                            notes.Add(new RawNote
                            {
                                time       = startTime,
                                pitch      = pitch,
                                duration   = dur,
                                staffId    = staffId,
                                isGrace    = isGrace,
                                tiedToNext = tiedToNext
                            });
                        }
                    }
                }

                return dur;
            }

            if (node.Name == "Rest")
                return NodeDurationSeconds(node, beatsPerSecond, beatsPerMeasure) * tupletRatio;

            return 0f;
        }

        private static bool IsGraceChord(XmlNode chord)
        {
            foreach (XmlNode child in chord.ChildNodes)
                if (GraceTypes.Contains(child.Name)) return true;

            XmlNode typeNode = chord.SelectSingleNode("durationType");
            return typeNode != null && GraceTypes.Contains(typeNode.InnerText);
        }

        // Returns true if the chord's only articulation is an ornament (trill etc.)
        // In practice, trill/ornament articulations sit on real notes — we keep the note
        // but mark it as lower priority by leaving isGrace=false; the ornament type check
        // below is here for the case where someone has written out a trill manually as
        // individual fast notes (rare, but possible).
        private static bool HasOrnamentOnly(XmlNode chord)
        {
            var articulations = chord.SelectNodes(".//Articulation/subtype");
            if (articulations == null || articulations.Count == 0) return false;
            foreach (XmlNode a in articulations)
                if (!OrnamentTypes.Contains(a.InnerText)) return false;
            return true;
        }

        // -------------------------------------------------------------------------
        // Filtering / capping
        // -------------------------------------------------------------------------

        private static List<RawNote> FilterAndCap(List<RawNote> notes, int maxNotesAtOnce)
        {
            // Sort by time then by duration ascending (shortest = fastest = highest priority)
            notes.Sort((a, b) =>
            {
                int tc = a.time.CompareTo(b.time);
                if (tc != 0) return tc;
                // Grace notes go last (lowest priority)
                if (a.isGrace != b.isGrace) return a.isGrace ? 1 : -1;
                return a.duration.CompareTo(b.duration); // shorter duration = higher priority
            });

            var result   = new List<RawNote>(notes.Count);
            int i        = 0;
            while (i < notes.Count)
            {
                float groupTime = notes[i].time;

                // Collect all notes at this timestamp
                var group = new List<RawNote>();
                while (i < notes.Count && Mathf.Abs(notes[i].time - groupTime) < SimultaneousEpsilon)
                {
                    group.Add(notes[i]);
                    i++;
                }

                // Exclude grace notes entirely
                group.RemoveAll(n => n.isGrace);
                if (group.Count == 0) continue;

                // Already sorted by duration ascending — take up to maxNotesAtOnce total across
                // both hands combined (this is the player-facing "max notes on screen at once"
                // setting, so a two-hand chord must compete for the same budget, not get one
                // hand's worth of headroom each). Pitch dedup stays scoped per staff so a
                // legitimate unison between hands isn't mistaken for a stacked duplicate lane.
                var seenByStaff = new Dictionary<int, HashSet<int>>();
                int addedTotal = 0;
                foreach (var n in group)
                {
                    if (addedTotal >= maxNotesAtOnce) break;

                    if (!seenByStaff.TryGetValue(n.staffId, out var seen))
                    {
                        seen = new HashSet<int>();
                        seenByStaff[n.staffId] = seen;
                    }
                    // We don't know lane yet, but we can deduplicate by pitch to avoid
                    // two notes of the same pitch (unisons) stacking
                    if (seen.Contains(n.pitch)) continue;
                    seen.Add(n.pitch);
                    result.Add(n);
                    addedTotal++;
                }
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // Tie merging
        // -------------------------------------------------------------------------

        private static List<RawNote> MergeTies(List<RawNote> notes)
        {
            notes.Sort((a, b) => a.time.CompareTo(b.time));

            // Gate merging strictly on the tiedToNext flag read from the file (not on a
            // same-pitch/zero-gap proximity guess) — otherwise two genuinely re-articulated
            // repeats of the same pitch with no gap between them get silently glued into one
            // sustained note. And search forward for the nearest later note of the same
            // pitch/staff rather than assuming it's the very next list entry — a tied note can
            // share its start time with an unrelated note from the same chord, so its real
            // continuation isn't always adjacent once everything is flattened into one
            // time-sorted list.
            for (int i = 0; i < notes.Count; i++)
            {
                if (!notes[i].tiedToNext) continue;

                int targetIndex = -1;
                for (int j = i + 1; j < notes.Count; j++)
                {
                    if (notes[j].pitch == notes[i].pitch && notes[j].staffId == notes[i].staffId)
                    {
                        targetIndex = j;
                        break;
                    }
                }

                if (targetIndex == -1) continue; // dangling tie reference — leave the note as-is

                RawNote a = notes[i], b = notes[targetIndex];
                notes[i] = new RawNote
                {
                    time       = a.time,
                    pitch      = a.pitch,
                    duration   = (b.time - a.time) + b.duration,
                    staffId    = a.staffId,
                    isGrace    = false,
                    tiedToNext = b.tiedToNext // preserve the chain so a 3+ note tie fully merges
                };
                notes.RemoveAt(targetIndex);
                i--; // re-check this index in case the merged note itself ties further
            }
            return notes;
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static float NodeDurationSeconds(XmlNode node, float beatsPerSecond, float beatsPerMeasure)
        {
            XmlNode typeNode = node.SelectSingleNode("durationType");
            string  type     = typeNode?.InnerText ?? "quarter";

            // A whole-measure rest ("durationType: measure") has no fixed beat count of its
            // own — it means "rest for however long this measure's time signature says", which
            // varies (6 beats in 6/4, 3 in 6/8, etc.), so it can't go in the fixed lookup table.
            if (type == "measure") return beatsPerMeasure / beatsPerSecond;

            XmlNode dotsNode = node.SelectSingleNode("dots");
            float beats  = DurationTypeToBeats(type);
            int   dots   = dotsNode != null && int.TryParse(dotsNode.InnerText, out int d) ? d : 0;
            float dotAdd = beats;
            for (int i = 0; i < dots; i++) { dotAdd *= 0.5f; beats += dotAdd; }
            return beats / beatsPerSecond;
        }

        private static float DurationTypeToBeats(string type) => type switch
        {
            "whole"   => 4f,
            "half"    => 2f,
            "quarter" => 1f,
            "eighth"  => 0.5f,
            "16th"    => 0.25f,
            "32nd"    => 0.125f,
            "64th"    => 0.0625f,
            _         => 1f
        };

        private static void AssignLanes(List<RawNote> notes, BeatmapData beatmap,
            int laneCount, ClefFilter clef)
        {
            var rng = new System.Random(42);

            if (clef != ClefFilter.Both)
            {
                // Single clef: map pitch across all lanes, low→left high→right
                (int lo, int hi) = GetPitchRange(notes);
                AssignLanesForRegion(notes, beatmap, 0, laneCount - 1, lo, hi, staffId: null, rng);
                return;
            }

            // Both clefs: split lane space
            //   k even  (k=2n): bass → [0, n-1],  treble → [n, k-1]
            //   k odd   (k=2n+1): bass → [0, n],  treble → [n, k-1]  (middle lane shared)
            int n2 = laneCount / 2;
            int bassFirst   = 0,  bassLast   = n2 - 1 + (laneCount % 2); // inclusive
            int trebleFirst = n2, trebleLast = laneCount - 1;

            (int bassLo,    int bassHi)    = GetPitchRangeForStaff(notes, staffId: 2);
            (int trebleLo,  int trebleHi)  = GetPitchRangeForStaff(notes, staffId: 1);

            AssignLanesForRegion(notes, beatmap, bassFirst,   bassLast,   bassLo,   bassHi,   staffId: 2, rng);
            AssignLanesForRegion(notes, beatmap, trebleFirst, trebleLast, trebleLo, trebleHi, staffId: 1, rng);
        }

        // Assigns lanes for one hand's lane range, walking notes in time order and treating all
        // notes at the same timestamp as one set. The pitch-mapped "ideal" lane is kept whenever
        // possible; it's only overridden when it collides with another note in the same set
        // (always resolved) or repeats the previous set's lane (resolved probabilistically via
        // RepeatLaneWeight, so pitch contour isn't distorted on every single repeated note).
        private static void AssignLanesForRegion(List<RawNote> notes, BeatmapData beatmap,
            int laneFirst, int laneLast, int minPitch, int maxPitch, int? staffId, System.Random rng)
        {
            var regionNotes = staffId.HasValue
                ? notes.FindAll(n => n.staffId == staffId.Value)
                : new List<RawNote>(notes);
            if (regionNotes.Count == 0) return;

            regionNotes.Sort((a, b) => a.time.CompareTo(b.time));

            var lastGroupLanes = new HashSet<int>();
            int i = 0;
            while (i < regionNotes.Count)
            {
                float groupTime = regionNotes[i].time;
                var group = new List<RawNote>();
                while (i < regionNotes.Count && Mathf.Abs(regionNotes[i].time - groupTime) < SimultaneousEpsilon)
                {
                    group.Add(regionNotes[i]);
                    i++;
                }

                var usedThisGroup = new HashSet<int>();
                foreach (var n in group)
                {
                    int ideal = PitchToLane(n.pitch, laneFirst, laneLast, minPitch, maxPitch);
                    int lane  = ResolveLane(ideal, laneFirst, laneLast, lastGroupLanes, usedThisGroup, rng);
                    usedThisGroup.Add(lane);
                    beatmap.notes.Add(new NoteData(n.time, lane, n.duration >= HoldThreshold ? n.duration : 0f));
                }

                lastGroupLanes = usedThisGroup;
            }
        }

        // Weight given to keeping a lane that repeats the previous timestamp's lane
        // (0 = never repeat, 1 = no preference against repeating).
        private const float RepeatLaneWeight = 0.2f;

        private static int ResolveLane(int idealLane, int laneFirst, int laneLast,
            HashSet<int> lastGroupLanes, HashSet<int> usedThisGroup, System.Random rng)
        {
            if (laneFirst == laneLast) return laneFirst;

            bool mustAvoid   = usedThisGroup.Contains(idealLane); // same chord can't stack on one lane
            bool shouldAvoid = !mustAvoid && lastGroupLanes.Contains(idealLane)
                                && rng.NextDouble() >= RepeatLaneWeight;

            if (!mustAvoid && !shouldAvoid)
                return idealLane;

            bool tryDownFirst = rng.NextDouble() < 0.5;
            for (int offset = 1; offset <= laneLast - laneFirst; offset++)
            {
                int down = idealLane - offset;
                int up   = idealLane + offset;
                int first  = tryDownFirst ? down : up;
                int second = tryDownFirst ? up   : down;

                if (IsFreeLane(first, laneFirst, laneLast, lastGroupLanes, usedThisGroup)) return first;
                if (IsFreeLane(second, laneFirst, laneLast, lastGroupLanes, usedThisGroup)) return second;
            }

            // No lane avoids both constraints — at minimum, don't stack on top of this chord's own note
            for (int lane = laneFirst; lane <= laneLast; lane++)
                if (!usedThisGroup.Contains(lane)) return lane;

            return idealLane;
        }

        private static bool IsFreeLane(int lane, int laneFirst, int laneLast,
            HashSet<int> lastGroupLanes, HashSet<int> usedThisGroup)
        {
            return lane >= laneFirst && lane <= laneLast
                && !usedThisGroup.Contains(lane) && !lastGroupLanes.Contains(lane);
        }

        // Maps pitch to a lane index within [laneFirst, laneLast]
        private static int PitchToLane(int pitch, int laneFirst, int laneLast, int minPitch, int maxPitch)
        {
            int range = laneLast - laneFirst;
            if (range <= 0 || maxPitch == minPitch) return laneFirst + range / 2;
            float fraction = (float)(pitch - minPitch) / (maxPitch - minPitch);
            return laneFirst + Mathf.Clamp(Mathf.FloorToInt(fraction * (range + 1)), 0, range);
        }

        private static (int, int) GetPitchRange(List<RawNote> notes)
        {
            if (notes.Count == 0) return (60, 72);
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var n in notes) { lo = Math.Min(lo, n.pitch); hi = Math.Max(hi, n.pitch); }
            return (lo, hi);
        }

        private static (int, int) GetPitchRangeForStaff(List<RawNote> notes, int staffId)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var n in notes)
                if (n.staffId == staffId) { lo = Math.Min(lo, n.pitch); hi = Math.Max(hi, n.pitch); }
            if (lo == int.MaxValue) return (60, 72);
            return (lo, hi);
        }

        // Walks all Parts in document order, counts their Staff children to derive
        // sequential global staff IDs (MuseScore 4 doesn't put id= on Part>Staff).
        // Returns a list of (partName, firstGlobalStaffId, staffCount) for every Part.
        private struct PartInfo
        {
            public string name;
            public int    firstStaffId;
            public int    staffCount;
        }

        private static List<PartInfo> BuildPartList(XmlDocument doc)
        {
            var result  = new List<PartInfo>();
            int globalId = 1;

            // Parts appear as direct children of <Score>
            XmlNode score = doc.SelectSingleNode("//Score") ?? doc.DocumentElement;
            foreach (XmlNode part in score.ChildNodes)
            {
                if (part.Name != "Part") continue;

                string name = part.SelectSingleNode("trackName")?.InnerText
                           ?? part.SelectSingleNode("Instrument/longName")?.InnerText
                           ?? part.SelectSingleNode("Instrument/shortName")?.InnerText
                           ?? "Unknown";

                int staffCount = 0;
                foreach (XmlNode child in part.ChildNodes)
                    if (child.Name == "Staff") staffCount++;

                result.Add(new PartInfo { name = name, firstStaffId = globalId, staffCount = staffCount });
                globalId += staffCount;
            }

            return result;
        }

        private static HashSet<int> GetStaffIdsForPart(XmlDocument doc, int globalPartIndex)
        {
            var parts = BuildPartList(doc);
            if (globalPartIndex < 0 || globalPartIndex >= parts.Count) return new HashSet<int>();

            PartInfo p = parts[globalPartIndex];
            var ids = new HashSet<int>();
            for (int i = 0; i < p.staffCount; i++)
                ids.Add(p.firstStaffId + i);
            return ids;
        }
    }
}
