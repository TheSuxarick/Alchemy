using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alchemy.SpellDrawing
{
    [Serializable]
    public struct SpellScoreEntry
    {
        public SpellData spell;
        public float precisionPercent;
        public int expectedStrokeCount;
    }

    public class GlyphMatchResult
    {
        public SpellData bestSpell;
        public float precisionPercent;
        public bool isRecognized;
        public int drawnStrokeCount;
        public float drawDurationSeconds;
        public float pathLengthPixels;
        public float speedNormalized;
        public float boostMultiplier;
        public List<SpellScoreEntry> allScores = new List<SpellScoreEntry>();
    }

    /// <summary>
    /// Multi-stroke alchemical rune recognizer based on joint stroke-cloud normalization
    /// and $1/$P-style point-cloud resampling with bounded rotation and stroke-order invariance.
    /// </summary>
    public static class GlyphRecognizer
    {
        public const int ResamplePointCount = 48;
        public const float RecognitionThresholdPercent = 60f;
        public const float SuperchargeThresholdPercent = 85f;

        private const float MaxRotationDegrees = 18f;
        private const float RotationStepDegrees = 4.5f;

        private struct NormalizedRune
        {
            public List<List<Vector2>> strokes;
            public List<float> windings;
            public float overallAspect;
        }

        public static GlyphMatchResult Recognize(
            IReadOnlyList<Vector2> rawScreenPoints,
            IReadOnlyList<SpellData> candidateSpells,
            float drawDurationSeconds)
        {
            var single = new List<IReadOnlyList<Vector2>> { rawScreenPoints };
            return Recognize(single, candidateSpells, drawDurationSeconds);
        }

        public static GlyphMatchResult Recognize(
            IReadOnlyList<IReadOnlyList<Vector2>> rawScreenStrokes,
            IReadOnlyList<SpellData> candidateSpells,
            float drawDurationSeconds)
        {
            var result = new GlyphMatchResult
            {
                drawDurationSeconds = Mathf.Max(0.05f, drawDurationSeconds)
            };

            if (rawScreenStrokes == null || rawScreenStrokes.Count == 0 || candidateSpells == null || candidateSpells.Count == 0)
            {
                return result;
            }

            // Filter out tiny accidental clicks
            var validStrokes = new List<IReadOnlyList<Vector2>>();
            float totalLength = 0f;
            for (int i = 0; i < rawScreenStrokes.Count; i++)
            {
                var stroke = rawScreenStrokes[i];
                if (stroke == null || stroke.Count < 2) continue;
                float len = PathLength(stroke);
                if (len >= 12f)
                {
                    validStrokes.Add(stroke);
                    totalLength += len;
                }
            }

            result.drawnStrokeCount = validStrokes.Count;
            result.pathLengthPixels = totalLength;
            if (validStrokes.Count == 0 || totalLength < 25f)
            {
                return result;
            }

            NormalizedRune candidateNorm = NormalizeMultiStroke(validStrokes);

            SpellData bestSpell = null;
            float bestPrecision = 0f;

            for (int i = 0; i < candidateSpells.Count; i++)
            {
                SpellData spell = candidateSpells[i];
                if (spell == null || !spell.HasValidTemplate()) continue;

                float spellBestScore = 0f;
                int expectedCount = spell.GetPrimaryStrokeCount();

                for (int t = 0; t < spell.idealTemplates.Count; t++)
                {
                    var template = spell.idealTemplates[t];
                    if (template == null) continue;

                    List<List<Vector2>> templateStrokes = template.GetStrokes();
                    if (templateStrokes.Count == 0) continue;

                    NormalizedRune templateNorm = NormalizeMultiStroke(templateStrokes);
                    float score = 0f;

                    if (validStrokes.Count == templateStrokes.Count)
                    {
                        score = CompareMultiStrokeRunes(
                            candidateNorm,
                            templateNorm,
                            spell.allowReverseStroke,
                            spell.allowAnyStrokeOrder);
                    }
                    else if (templateStrokes.Count == 1 && validStrokes.Count > 1 && validStrokes.Count <= 4)
                    {
                        // Allow drawing a 1-stroke shape (like a 4-sided square or 3-sided triangle) by lifting the mouse at corners
                        var merged = new List<Vector2>();
                        for (int s = 0; s < validStrokes.Count; s++)
                        {
                            merged.AddRange(validStrokes[s]);
                        }
                        NormalizedRune mergedCandidate = NormalizeMultiStroke(new List<IReadOnlyList<Vector2>> { merged });
                        score = CompareMultiStrokeRunes(
                            mergedCandidate,
                            templateNorm,
                            spell.allowReverseStroke,
                            spell.allowAnyStrokeOrder);
                    }
                    else
                    {
                        continue;
                    }

                    if (score > spellBestScore)
                    {
                        spellBestScore = score;
                    }
                }

                result.allScores.Add(new SpellScoreEntry
                {
                    spell = spell,
                    precisionPercent = spellBestScore,
                    expectedStrokeCount = expectedCount
                });

                if (spellBestScore > bestPrecision)
                {
                    bestPrecision = spellBestScore;
                    bestSpell = spell;
                }
            }

            result.allScores.Sort((a, b) => b.precisionPercent.CompareTo(a.precisionPercent));
            result.bestSpell = bestSpell;
            result.precisionPercent = bestPrecision;
            result.isRecognized = bestSpell != null && bestPrecision >= RecognitionThresholdPercent;

            // Speed calculation scaled gently by stroke count (more strokes get slightly more time)
            float slowCap = 1.4f + (validStrokes.Count - 1) * 0.65f;
            float fastCap = 0.28f + (validStrokes.Count - 1) * 0.22f;
            float speedNorm = Mathf.Clamp01(Mathf.InverseLerp(slowCap, fastCap, result.drawDurationSeconds));
            result.speedNormalized = speedNorm;

            if (!result.isRecognized)
            {
                result.boostMultiplier = 0f;
            }
            else
            {
                float precisionFactor = Mathf.InverseLerp(RecognitionThresholdPercent, 100f, bestPrecision);
                float precisionBonus = precisionFactor * 0.45f;
                float speedBonus = (bestPrecision >= SuperchargeThresholdPercent)
                    ? speedNorm * 0.30f
                    : speedNorm * 0.12f;

                result.boostMultiplier = Mathf.Clamp(1.0f + precisionBonus + speedBonus, 1.0f, 1.75f);
            }

            return result;
        }

        private static float CompareMultiStrokeRunes(
            NormalizedRune candidate,
            NormalizedRune template,
            bool allowReverse,
            bool allowAnyOrder)
        {
            int strokeCount = candidate.strokes.Count;
            if (strokeCount == 0 || strokeCount != template.strokes.Count) return 0f;

            // Generate stroke-index mappings (full permutations for <= 3 strokes, greedy for 4+)
            List<int[]> orderings = GetStrokeOrderings(strokeCount, allowAnyOrder);

            float bestScore = 0f;

            for (int o = 0; o < orderings.Count; o++)
            {
                int[] order = orderings[o];

                for (float angleDeg = -MaxRotationDegrees; angleDeg <= MaxRotationDegrees; angleDeg += RotationStepDegrees)
                {
                    float rad = angleDeg * Mathf.Deg2Rad;
                    float totalDist = 0f;
                    float totalWindingDiff = 0f;

                    for (int s = 0; s < strokeCount; s++)
                    {
                        List<Vector2> cStroke = candidate.strokes[s];
                        int tIdx = order[s];
                        List<Vector2> tStroke = template.strokes[tIdx];

                        float dist = BestPathDistanceRotated(cStroke, tStroke, rad, allowReverse);

                        totalDist += dist;
                        totalWindingDiff += Mathf.Abs(candidate.windings[s] - template.windings[tIdx]);
                    }

                    float avgDist = totalDist / strokeCount;
                    float avgWindingDiff = totalWindingDiff / strokeCount;

                    float spatialSimilarity = Mathf.Clamp01(1f - (avgDist / 0.36f));
                    float windingPenalty = Mathf.Clamp01(avgWindingDiff / (Mathf.PI * 3.0f)) * 0.18f;
                    float aspectDiff = Mathf.Abs(candidate.overallAspect - template.overallAspect);
                    float aspectPenalty = Mathf.Clamp01(aspectDiff - 0.35f) * 0.15f;

                    float combined = Mathf.Clamp01(spatialSimilarity - windingPenalty - aspectPenalty);
                    float curved = Mathf.Pow(combined, 0.85f) * 100f;

                    if (curved > bestScore)
                    {
                        bestScore = curved;
                    }
                }
            }

            return Mathf.Clamp(bestScore, 0f, 99.9f);
        }

        private static List<int[]> GetStrokeOrderings(int count, bool allowAnyOrder)
        {
            var result = new List<int[]>();
            if (!allowAnyOrder || count <= 1)
            {
                int[] identity = new int[count];
                for (int i = 0; i < count; i++) identity[i] = i;
                result.Add(identity);
                return result;
            }

            if (count == 2)
            {
                result.Add(new[] { 0, 1 });
                result.Add(new[] { 1, 0 });
                return result;
            }

            if (count == 3)
            {
                result.Add(new[] { 0, 1, 2 });
                result.Add(new[] { 0, 2, 1 });
                result.Add(new[] { 1, 0, 2 });
                result.Add(new[] { 1, 2, 0 });
                result.Add(new[] { 2, 0, 1 });
                result.Add(new[] { 2, 1, 0 });
                return result;
            }

            // For 4+ strokes, keep identity order to avoid factorial blowup
            int[] fallback = new int[count];
            for (int i = 0; i < count; i++) fallback[i] = i;
            result.Add(fallback);
            return result;
        }

        private static NormalizedRune NormalizeMultiStroke(IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            var resampledStrokes = new List<List<Vector2>>(rawStrokes.Count);
            var windings = new List<float>(rawStrokes.Count);
            var allPoints = new List<Vector2>(rawStrokes.Count * ResamplePointCount);

            for (int i = 0; i < rawStrokes.Count; i++)
            {
                List<Vector2> resampled = Resample(rawStrokes[i], ResamplePointCount);
                resampledStrokes.Add(resampled);
                windings.Add(CalculateTotalWinding(resampled));
                allPoints.AddRange(resampled);
            }

            Vector2 centroid = Centroid(allPoints);
            for (int s = 0; s < resampledStrokes.Count; s++)
            {
                var stroke = resampledStrokes[s];
                for (int i = 0; i < stroke.Count; i++)
                {
                    stroke[i] -= centroid;
                }
            }

            Rect bounds = BoundingBox(allPoints);
            float width = Mathf.Max(0.0001f, bounds.width);
            float height = Mathf.Max(0.0001f, bounds.height);
            float maxDim = Mathf.Max(width, height);
            float minDim = Mathf.Min(width, height);

            float aspectBalance = Mathf.Clamp01(minDim / maxDim);
            float blendNonUniform = Mathf.InverseLerp(0.18f, 0.45f, aspectBalance);
            float scaleX = Mathf.Lerp(maxDim, width, blendNonUniform);
            float scaleY = Mathf.Lerp(maxDim, height, blendNonUniform);

            for (int s = 0; s < resampledStrokes.Count; s++)
            {
                var stroke = resampledStrokes[s];
                for (int i = 0; i < stroke.Count; i++)
                {
                    stroke[i] = new Vector2(stroke[i].x / scaleX, stroke[i].y / scaleY);
                }
            }

            return new NormalizedRune
            {
                strokes = resampledStrokes,
                windings = windings,
                overallAspect = aspectBalance
            };
        }

        /// <summary>
        /// Normalizes all strokes jointly into [0..1] canvas coordinates, preserving relative
        /// positions and sizes between strokes.
        /// </summary>
        public static List<List<Vector2>> NormalizeMultiStrokesToUnitBox(
            IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes,
            float padding = 0.12f)
        {
            var result = new List<List<Vector2>>();
            if (rawStrokes == null || rawStrokes.Count == 0) return result;

            var allPts = new List<Vector2>();
            for (int s = 0; s < rawStrokes.Count; s++)
            {
                if (rawStrokes[s] != null) allPts.AddRange(rawStrokes[s]);
            }
            if (allPts.Count == 0) return result;

            Rect bounds = BoundingBox(allPts);
            float maxDim = Mathf.Max(bounds.width, bounds.height, 0.0001f);
            Vector2 center = bounds.center;
            float usableSpan = 1f - padding * 2f;

            for (int s = 0; s < rawStrokes.Count; s++)
            {
                var stroke = rawStrokes[s];
                if (stroke == null || stroke.Count < 2) continue;

                var normStroke = new List<Vector2>(stroke.Count);
                for (int i = 0; i < stroke.Count; i++)
                {
                    Vector2 offset = (stroke[i] - center) / maxDim;
                    normStroke.Add(new Vector2(0.5f + offset.x * usableSpan, 0.5f + offset.y * usableSpan));
                }
                result.Add(Resample(normStroke, ResamplePointCount));
            }

            return result;
        }

        public static List<Vector2> NormalizePointsToUnitBox(IReadOnlyList<Vector2> rawPoints, float padding = 0.1f)
        {
            var multi = NormalizeMultiStrokesToUnitBox(new List<IReadOnlyList<Vector2>> { rawPoints }, padding);
            return multi.Count > 0 ? multi[0] : new List<Vector2>();
        }

        public static List<Vector2> Resample(IReadOnlyList<Vector2> points, int targetCount)
        {
            var source = new List<Vector2>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                if (source.Count == 0 || Vector2.Distance(source[source.Count - 1], points[i]) > 0.0001f)
                {
                    source.Add(points[i]);
                }
            }

            if (source.Count == 0) return new List<Vector2>();

            if (source.Count == 1)
            {
                var single = new List<Vector2>(targetCount);
                for (int i = 0; i < targetCount; i++) single.Add(source[0]);
                return single;
            }

            float totalLength = PathLength(source);
            float interval = totalLength / (targetCount - 1);
            float accumulated = 0f;

            var resampled = new List<Vector2>(targetCount) { source[0] };

            int idx = 1;
            while (idx < source.Count)
            {
                Vector2 prev = source[idx - 1];
                Vector2 curr = source[idx];
                float segmentDist = Vector2.Distance(prev, curr);

                if (accumulated + segmentDist >= interval && segmentDist > 0.00001f)
                {
                    float t = (interval - accumulated) / segmentDist;
                    Vector2 newPoint = Vector2.Lerp(prev, curr, t);
                    resampled.Add(newPoint);
                    source.Insert(idx, newPoint);
                    accumulated = 0f;
                }
                else
                {
                    accumulated += segmentDist;
                }
                idx++;
            }

            while (resampled.Count < targetCount)
            {
                resampled.Add(source[source.Count - 1]);
            }

            if (resampled.Count > targetCount)
            {
                resampled.RemoveRange(targetCount, resampled.Count - targetCount);
            }

            return resampled;
        }

        private static float CalculateTotalWinding(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 3) return 0f;
            float totalAbsAngle = 0f;
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector2 v1 = points[i] - points[i - 1];
                Vector2 v2 = points[i + 1] - points[i];
                if (v1.sqrMagnitude > 0.000001f && v2.sqrMagnitude > 0.000001f)
                {
                    float angle = Vector2.SignedAngle(v1, v2) * Mathf.Deg2Rad;
                    totalAbsAngle += Mathf.Abs(angle);
                }
            }
            return totalAbsAngle;
        }

        private static float BestPathDistanceRotated(
            IReadOnlyList<Vector2> a,
            IReadOnlyList<Vector2> b,
            float radians,
            bool allowReverse)
        {
            int count = Mathf.Min(a.Count, b.Count);
            if (count == 0) return float.MaxValue;

            float best = PathDistanceRotated(a, b, radians, reverseTemplate: false, shift: 0);
            if (allowReverse)
            {
                float rev = PathDistanceRotated(a, b, radians, reverseTemplate: true, shift: 0);
                if (rev < best) best = rev;
            }

            // If template or candidate is a closed loop (e.g. Square, Triangle, Circle), test cyclic starting-point shifts
            // so starting at ANY corner or drawing clockwise vs counter-clockwise matches seamlessly!
            bool isClosedLoop = Vector2.Distance(b[0], b[count - 1]) < 0.35f ||
                                Vector2.Distance(a[0], a[count - 1]) < 0.35f;
            if (isClosedLoop)
            {
                const int shiftStep = 3;
                for (int shift = shiftStep; shift < count; shift += shiftStep)
                {
                    float dFwd = PathDistanceRotated(a, b, radians, reverseTemplate: false, shift: shift);
                    if (dFwd < best) best = dFwd;

                    if (allowReverse)
                    {
                        float dRev = PathDistanceRotated(a, b, radians, reverseTemplate: true, shift: shift);
                        if (dRev < best) best = dRev;
                    }
                }
            }

            return best;
        }

        private static float PathDistanceRotated(
            IReadOnlyList<Vector2> a,
            IReadOnlyList<Vector2> b,
            float radians,
            bool reverseTemplate,
            int shift)
        {
            int count = Mathf.Min(a.Count, b.Count);
            if (count == 0) return float.MaxValue;

            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            float sum = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector2 p = a[i];
                Vector2 rotated = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
                int bIdx = (i + shift) % count;
                if (reverseTemplate)
                {
                    bIdx = (count - 1 - bIdx + count) % count;
                }
                Vector2 target = b[bIdx];
                sum += Vector2.Distance(rotated, target);
            }

            return sum / count;
        }

        public static float PathLength(IReadOnlyList<Vector2> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                length += Vector2.Distance(points[i - 1], points[i]);
            }
            return length;
        }

        private static Vector2 Centroid(IReadOnlyList<Vector2> points)
        {
            if (points.Count == 0) return Vector2.zero;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Count; i++) sum += points[i];
            return sum / points.Count;
        }

        private static Rect BoundingBox(IReadOnlyList<Vector2> points)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = points[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public static List<Vector2> GenerateDefaultTemplate(string elementId)
        {
            var pts = new List<Vector2>();
            switch (elementId.ToLowerInvariant())
            {
                case "fire":
                    AddLineSegment(pts, new Vector2(0.18f, 0.20f), new Vector2(0.50f, 0.85f), 16);
                    AddLineSegment(pts, new Vector2(0.50f, 0.85f), new Vector2(0.82f, 0.20f), 16);
                    AddLineSegment(pts, new Vector2(0.82f, 0.20f), new Vector2(0.18f, 0.20f), 16);
                    break;

                case "water":
                    for (int i = 0; i < 48; i++)
                    {
                        float t = i / 47f;
                        float x = Mathf.Lerp(0.12f, 0.88f, t);
                        float y = 0.50f + Mathf.Sin(t * Mathf.PI * 2f) * 0.24f;
                        pts.Add(new Vector2(x, y));
                    }
                    break;

                case "air":
                    for (int i = 0; i < 48; i++)
                    {
                        float t = i / 47f;
                        float angle = t * Mathf.PI * 3.8f;
                        float radius = Mathf.Lerp(0.05f, 0.40f, t);
                        pts.Add(new Vector2(0.50f + Mathf.Cos(angle) * radius, 0.50f + Mathf.Sin(angle) * radius));
                    }
                    break;

                case "earth":
                    AddLineSegment(pts, new Vector2(0.22f, 0.78f), new Vector2(0.78f, 0.78f), 12);
                    AddLineSegment(pts, new Vector2(0.78f, 0.78f), new Vector2(0.78f, 0.22f), 12);
                    AddLineSegment(pts, new Vector2(0.78f, 0.22f), new Vector2(0.22f, 0.22f), 12);
                    AddLineSegment(pts, new Vector2(0.22f, 0.22f), new Vector2(0.22f, 0.78f), 12);
                    break;
            }
            return pts;
        }

        private static void AddLineSegment(List<Vector2> list, Vector2 from, Vector2 to, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)steps;
                list.Add(Vector2.Lerp(from, to, t));
            }
        }
    }
}
