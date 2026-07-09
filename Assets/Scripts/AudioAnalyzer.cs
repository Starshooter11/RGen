using System;
using System.Collections.Generic;
using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Analyzes an AudioClip offline (before playback) using spectral flux onset detection.
    /// Returns a list of onset timestamps in seconds and an estimated BPM.
    /// </summary>
    public static class AudioAnalyzer
    {
        // FFT frame size — must be power of two
        private const int FrameSize = 1024;
        private const int HopSize = 512;

        public static AnalysisResult Analyze(AudioClip clip)
        {
            int channels = clip.channels;
            int sampleRate = clip.frequency;
            int totalSamples = clip.samples;

            float[] rawSamples = new float[totalSamples * channels];
            clip.GetData(rawSamples, 0);

            // Downmix to mono
            float[] mono = Downmix(rawSamples, channels, totalSamples);

            float[] flux = ComputeSpectralFlux(mono, FrameSize, HopSize);
            float[] smoothedFlux = SmoothSignal(flux, windowSize: 5);
            List<int> peakFrames = PickPeaks(smoothedFlux, sensitivity: 1.3f, minGapFrames: 3);

            float hopSeconds = (float)HopSize / sampleRate;
            var onsets = new List<float>();
            var strengths = new List<float>();
            foreach (int frame in peakFrames)
            {
                onsets.Add(frame * hopSeconds);
                strengths.Add(smoothedFlux[frame]);
            }

            float bpm = EstimateBPM(onsets, clip.length);

            return new AnalysisResult
            {
                onsetTimes = onsets,
                onsetStrengths = strengths,
                bpm = bpm,
                duration = clip.length
            };
        }

        private static float[] Downmix(float[] interleaved, int channels, int monoLength)
        {
            float[] mono = new float[monoLength];
            float invChannels = 1f / channels;
            for (int i = 0; i < monoLength; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += interleaved[i * channels + c];
                mono[i] = sum * invChannels;
            }
            return mono;
        }

        private static float[] ComputeSpectralFlux(float[] mono, int frameSize, int hopSize)
        {
            int numFrames = (mono.Length - frameSize) / hopSize;
            if (numFrames <= 0) return Array.Empty<float>();

            float[] flux = new float[numFrames];
            float[] prevMag = new float[frameSize / 2];
            float[] re = new float[frameSize];
            float[] im = new float[frameSize];
            float[] window = BuildHannWindow(frameSize);

            for (int f = 0; f < numFrames; f++)
            {
                int offset = f * hopSize;
                // Apply window and copy into FFT buffers
                for (int i = 0; i < frameSize; i++)
                {
                    re[i] = mono[offset + i] * window[i];
                    im[i] = 0f;
                }

                FFT(re, im, frameSize);

                // Compute positive spectral flux (half spectrum)
                float frameFlux = 0f;
                for (int i = 0; i < frameSize / 2; i++)
                {
                    float mag = Mathf.Sqrt(re[i] * re[i] + im[i] * im[i]);
                    float diff = mag - prevMag[i];
                    if (diff > 0f) frameFlux += diff;
                    prevMag[i] = mag;
                }
                flux[f] = frameFlux;
            }

            return flux;
        }

        // Cooley-Tukey iterative in-place FFT (power-of-2 sizes only)
        private static void FFT(float[] re, float[] im, int n)
        {
            // Bit-reversal permutation
            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2.0 * Math.PI / len;
                float wRe = (float)Math.Cos(angle);
                float wIm = (float)Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    float curRe = 1f, curIm = 0f;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int u = i + k, v = i + k + len / 2;
                        float tRe = curRe * re[v] - curIm * im[v];
                        float tIm = curRe * im[v] + curIm * re[v];
                        re[v] = re[u] - tRe;
                        im[v] = im[u] - tIm;
                        re[u] += tRe;
                        im[u] += tIm;
                        float nextRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = nextRe;
                    }
                }
            }
        }

        private static float[] BuildHannWindow(int size)
        {
            float[] w = new float[size];
            for (int i = 0; i < size; i++)
                w[i] = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (size - 1)));
            return w;
        }

        private static float[] SmoothSignal(float[] signal, int windowSize)
        {
            float[] smoothed = new float[signal.Length];
            int half = windowSize / 2;
            for (int i = 0; i < signal.Length; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int k = -half; k <= half; k++)
                {
                    int idx = i + k;
                    if (idx >= 0 && idx < signal.Length)
                    {
                        sum += signal[idx];
                        count++;
                    }
                }
                smoothed[i] = sum / count;
            }
            return smoothed;
        }

        // Adaptive threshold peak-picking: a frame is an onset if flux > mean + sensitivity * std
        private static List<int> PickPeaks(float[] flux, float sensitivity, int minGapFrames)
        {
            if (flux.Length == 0) return new List<int>();

            // Local mean/std in a sliding window
            int windowRadius = 20;
            var peaks = new List<int>();
            int lastPeak = -minGapFrames - 1;

            for (int i = 1; i < flux.Length - 1; i++)
            {
                // Local statistics
                float mean = 0f, sqSum = 0f;
                int count = 0;
                for (int k = Math.Max(0, i - windowRadius); k <= Math.Min(flux.Length - 1, i + windowRadius); k++)
                {
                    mean += flux[k];
                    count++;
                }
                mean /= count;
                for (int k = Math.Max(0, i - windowRadius); k <= Math.Min(flux.Length - 1, i + windowRadius); k++)
                {
                    float d = flux[k] - mean;
                    sqSum += d * d;
                }
                float std = Mathf.Sqrt(sqSum / count);

                float threshold = mean + sensitivity * std;

                bool isLocalMax = flux[i] > flux[i - 1] && flux[i] >= flux[i + 1];
                bool aboveThreshold = flux[i] > threshold;
                bool respectsGap = (i - lastPeak) >= minGapFrames;

                if (isLocalMax && aboveThreshold && respectsGap)
                {
                    peaks.Add(i);
                    lastPeak = i;
                }
            }

            return peaks;
        }

        // Estimate BPM from inter-onset intervals using a histogram
        private static float EstimateBPM(List<float> onsets, float duration)
        {
            if (onsets.Count < 4) return 120f;

            // Build IOI histogram in BPM space
            const int bins = 200;
            const float minBPM = 60f, maxBPM = 200f;
            int[] histogram = new int[bins];

            for (int i = 1; i < onsets.Count; i++)
            {
                float ioi = onsets[i] - onsets[i - 1];
                if (ioi <= 0f) continue;
                float bpmCandidate = 60f / ioi;
                // Also consider half and double time
                for (int mult = 1; mult <= 2; mult++)
                {
                    float b = bpmCandidate * mult;
                    if (b >= minBPM && b <= maxBPM)
                    {
                        int bin = Mathf.RoundToInt((b - minBPM) / (maxBPM - minBPM) * (bins - 1));
                        histogram[bin]++;
                    }
                    b = bpmCandidate / mult;
                    if (b >= minBPM && b <= maxBPM)
                    {
                        int bin = Mathf.RoundToInt((b - minBPM) / (maxBPM - minBPM) * (bins - 1));
                        histogram[bin]++;
                    }
                }
            }

            int maxBin = 0;
            for (int i = 1; i < bins; i++)
                if (histogram[i] > histogram[maxBin]) maxBin = i;

            return minBPM + (float)maxBin / (bins - 1) * (maxBPM - minBPM);
        }

        public struct AnalysisResult
        {
            public List<float> onsetTimes;
            public List<float> onsetStrengths; // spectral flux magnitude at each onset
            public float bpm;
            public float duration;
        }
    }
}
