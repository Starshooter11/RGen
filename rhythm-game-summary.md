# Rhythm Game — Project Summary

## Status
Idea stage, not yet built. Unity.

## Core Concept
A rhythm game where players (or the system) can generate beatmaps automatically from arbitrary songs, rather than relying entirely on hand-charted maps.

## Chosen Approach
Audio analysis-based beatmap generation (not rule-based/randomized). This means using signal processing techniques — onset detection, BPM/tempo tracking, spectral flux analysis, FFT-based transient detection — to find musically meaningful moments in a track and convert them into playable notes.

In Unity terms, this likely involves either FFT-based analysis via `AudioSource.GetSpectrumData` or an offline analysis step (possibly in Python or a dedicated audio library) that pre-processes songs into beatmap data before runtime.

## Known Challenges / Discussion So Far

**The pipeline itself is tractable.** Getting onset/beat timestamps out of raw audio is a solvable, well-understood problem (FFT + onset detection). This part can be built relatively quickly (days, not weeks).

**The hard part is everything downstream of raw onsets:**
- Deciding which onsets become actual notes (not every transient should be a note, or maps become unplayable spam)
- Spacing notes so they're physically playable at a given difficulty
- Mapping onsets to lanes/positions in a way that feels intentional, not random
- Matching note density/intensity to the song's actual energy (buildups vs. choruses vs. drops should feel distinct)

**This requires iterative tuning, not one clever algorithm.** The realistic process is: generate a map, playtest it, notice something feels sparse/unplayable/mismatched to the music, adjust thresholds/weighting/lane logic, regenerate, repeat. Each cycle is roughly 15–60 minutes. This needs to happen across genre-diverse test songs (e.g. a 128bpm EDM track vs. a sparse acoustic ballad will break different assumptions). This tuning loop is bottlenecked on human listening/playing feedback — it can't be fully automated or offloaded.

Note: even commercial games in this space generally ship "decent baseline + adjustable difficulty/density" rather than claiming a fully-solved auto-charting problem. "Always feels good" is still an open, fuzzy problem industry-wide.

## ML-Based Alternative (Considered, Not Yet Pursued)
Idea: train a model on existing beatmaps from another rhythm game (e.g. osu!, StepMania, Beat Saber) to learn note-placement/spacing patterns, rather than hand-tuning heuristics.

Tradeoffs discussed:
- **Rights/licensing**: official first-party charts are likely off-limits; community-made charts (osu! beatmaps, StepMania simfiles) are often more permissively shared, but licensing should be checked per-source before building a pipeline around it.
- **What it would actually solve**: it can learn stylistic conventions (density patterns, common spacing idioms, genre-appropriate response to song energy) better than hand-rolled heuristics might.
- **What it would NOT solve**: it doesn't eliminate the tuning loop, it relocates it — from tuning thresholds/code to tuning dataset quality/model architecture/hyperparameters, which is often a slower, less interpretable debugging cycle ("why does the model space notes weirdly in quiet sections" is harder to fix than "why does my onset threshold misfire in quiet sections").
- **Scope**: this turns a Unity feature into a small ML project — dataset collection/cleaning, training (likely outside Unity in Python), exporting for runtime inference (e.g. ONNX) or precomputing offline, and validating generalization beyond the source game's specific style.

**Recommendation discussed**: build the signal-processing/heuristic approach first since it's faster to get working and fully interpretable when something's off. Treat the ML approach as a possible v2 if the heuristic approach plateaus and there's a specific desire to mimic an existing game's charting feel — rather than starting with ML before having a felt sense of what "good" means for this game specifically.

## Open Questions for Next Steps
- Genre scope: is this meant to work well across all music genres, or focused on a narrower set?
- Difficulty tiers: single difficulty per song, or multiple (easy/medium/hard) generated from the same analysis?
- Lane count / input scheme (how many lanes, what input device — keyboard, controller, etc.)?
- Whether beatmap generation happens at runtime (on-device) or as an offline pre-processing step before the game ships.
