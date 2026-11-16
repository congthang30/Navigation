using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vps.UnityClient.Navigation
{
    /// <summary>
    /// Anti-aliased procedural SDF vector icons baked into tintable white RGBA32 Texture2Ds
    /// so UI Toolkit renders crisp iconography with zero font/emoji fallback boxes.
    /// </summary>
    public static class NavigationUiVectorIcons
    {
        public enum IconKind
        {
            Compass,
            Pin,
            Search,
            Close,
            Back,
            ChevronRight,
            Settings,
            ScanCamera,
            Robot,
            Door,
            Amenity,
            Check,
            RoutePath,
            Layers,
            Activity,
            Refresh,
            AlertCircle,
            Plus,
            Edit,
            Trash,
            Copy,
            VolumeOn,
            VolumeOff,
        }

        private const int IconSize = 64;
        private static readonly Dictionary<IconKind, Texture2D> Cache = new Dictionary<IconKind, Texture2D>();

        public static Texture2D Get(IconKind kind)
        {
            if (Cache.TryGetValue(kind, out var existing) && existing != null)
            {
                return existing;
            }

            var tex = BuildIcon(kind);
            Cache[kind] = tex;
            return tex;
        }

        private static Texture2D BuildIcon(IconKind kind)
        {
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
            {
                name = $"NavIcon_{kind}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[IconSize * IconSize];
            float pixelScale = 2f / IconSize;
            float aa = pixelScale * 1.15f;

            for (int y = 0; y < IconSize; y++)
            {
                float v = ((y + 0.5f) / IconSize) * 2f - 1f;
                int rowOffset = y * IconSize;
                for (int x = 0; x < IconSize; x++)
                {
                    float u = ((x + 0.5f) / IconSize) * 2f - 1f;
                    var p = new Vector2(u, v);
                    float d = EvaluateSignedDistance(kind, p);
                    float alpha = Mathf.Clamp01(0.5f - d / aa);
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    pixels[rowOffset + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static float EvaluateSignedDistance(IconKind kind, Vector2 p)
        {
            switch (kind)
            {
                case IconKind.Compass:
                {
                    // Dynamic tilted Live-View navigation arrow
                    var tip = new Vector2(0.60f, 0.58f);
                    var left = new Vector2(-0.62f, 0.06f);
                    var mid = new Vector2(-0.04f, -0.04f);
                    var bottom = new Vector2(0.06f, -0.62f);

                    float tri1 = SdTriangle(p, tip, left, mid);
                    float tri2 = SdTriangle(p, tip, mid, bottom);
                    return Mathf.Min(tri1, tri2) - 0.04f;
                }

                case IconKind.Pin:
                {
                    float head = SdCircle(p, new Vector2(0f, 0.22f), 0.46f);
                    float tip = SdTriangle(
                        p,
                        new Vector2(-0.40f, 0.05f),
                        new Vector2(0.40f, 0.05f),
                        new Vector2(0f, -0.76f)) - 0.03f;
                    float body = SdSmoothMin(head, tip, 0.08f);
                    float hole = SdCircle(p, new Vector2(0f, 0.22f), 0.19f);
                    return Mathf.Max(body, -hole);
                }

                case IconKind.Search:
                {
                    float lens = SdRing(p, new Vector2(-0.14f, 0.14f), 0.42f, 0.085f);
                    float handle = SdSegment(p, new Vector2(0.18f, -0.18f), new Vector2(0.62f, -0.62f)) - 0.085f;
                    return Mathf.Min(lens, handle);
                }

                case IconKind.Close:
                {
                    float a = SdSegment(p, new Vector2(-0.44f, -0.44f), new Vector2(0.44f, 0.44f)) - 0.085f;
                    float b = SdSegment(p, new Vector2(-0.44f, 0.44f), new Vector2(0.44f, -0.44f)) - 0.085f;
                    return Mathf.Min(a, b);
                }

                case IconKind.Back:
                {
                    float top = SdSegment(p, new Vector2(0.24f, 0.50f), new Vector2(-0.28f, 0f)) - 0.085f;
                    float bot = SdSegment(p, new Vector2(-0.28f, 0f), new Vector2(0.24f, -0.50f)) - 0.085f;
                    return Mathf.Min(top, bot);
                }

                case IconKind.ChevronRight:
                {
                    float top = SdSegment(p, new Vector2(-0.22f, 0.48f), new Vector2(0.28f, 0f)) - 0.08f;
                    float bot = SdSegment(p, new Vector2(0.28f, 0f), new Vector2(-0.22f, -0.48f)) - 0.08f;
                    return Mathf.Min(top, bot);
                }

                case IconKind.Settings:
                {
                    // Three horizontal tune sliders with hollow circular knobs
                    float d = 1e6f;
                    d = Mathf.Min(d, SliderRow(p, 0.46f, -0.22f));
                    d = Mathf.Min(d, SliderRow(p, 0.0f, 0.26f));
                    d = Mathf.Min(d, SliderRow(p, -0.46f, -0.12f));
                    return d;
                }

                case IconKind.ScanCamera:
                {
                    float c1 = CornerBracket(p, 1f, 1f);
                    float c2 = CornerBracket(p, -1f, 1f);
                    float c3 = CornerBracket(p, 1f, -1f);
                    float c4 = CornerBracket(p, -1f, -1f);
                    float ring = SdRing(p, Vector2.zero, 0.28f, 0.07f);
                    float dot = SdCircle(p, Vector2.zero, 0.095f);
                    return Mathf.Min(Mathf.Min(c1, c2), Mathf.Min(Mathf.Min(c3, c4), Mathf.Min(ring, dot)));
                }

                case IconKind.Robot:
                {
                    float antenna = SdSegment(p, new Vector2(0f, 0.42f), new Vector2(0f, 0.68f)) - 0.055f;
                    float bulb = SdCircle(p, new Vector2(0f, 0.74f), 0.11f);
                    float ears = SdRoundedBox(p - new Vector2(0f, -0.06f), new Vector2(0.74f, 0.18f), 0.09f);
                    float head = SdRoundedBox(p - new Vector2(0f, -0.06f), new Vector2(0.60f, 0.44f), 0.22f);
                    float shell = Mathf.Min(Mathf.Min(head, ears), Mathf.Min(antenna, bulb));
                    float visorCut = SdRoundedBox(p - new Vector2(0f, -0.02f), new Vector2(0.42f, 0.17f), 0.11f);
                    float frame = Mathf.Max(shell, -visorCut);
                    float leftEye = SdCircle(p, new Vector2(-0.19f, -0.02f), 0.08f);
                    float rightEye = SdCircle(p, new Vector2(0.19f, -0.02f), 0.08f);
                    return Mathf.Min(frame, Mathf.Min(leftEye, rightEye));
                }

                case IconKind.Door:
                {
                    float outer = SdRoundedBox(p - new Vector2(0f, 0.02f), new Vector2(0.48f, 0.62f), 0.08f);
                    float inner = SdRoundedBox(p - new Vector2(0f, -0.02f), new Vector2(0.32f, 0.48f), 0.04f);
                    float threshold = SdSegment(p, new Vector2(-0.62f, -0.60f), new Vector2(0.62f, -0.60f)) - 0.065f;
                    float frame = Mathf.Min(Mathf.Max(outer, -inner), threshold);
                    float knob = SdCircle(p, new Vector2(0.17f, -0.04f), 0.065f);
                    return Mathf.Min(frame, knob);
                }

                case IconKind.Amenity:
                {
                    float outer = SdRoundedBox(p, new Vector2(0.58f, 0.58f), 0.22f);
                    float inner = SdRoundedBox(p, new Vector2(0.41f, 0.41f), 0.12f);
                    float shell = Mathf.Max(outer, -inner);
                    float barV = SdSegment(p, new Vector2(0f, -0.24f), new Vector2(0f, 0.24f)) - 0.07f;
                    float barH = SdSegment(p, new Vector2(-0.24f, 0f), new Vector2(0.24f, 0f)) - 0.07f;
                    return Mathf.Min(shell, Mathf.Min(barV, barH));
                }

                case IconKind.Check:
                {
                    float a = SdSegment(p, new Vector2(-0.48f, -0.04f), new Vector2(-0.14f, -0.38f)) - 0.09f;
                    float b = SdSegment(p, new Vector2(-0.14f, -0.38f), new Vector2(0.52f, 0.36f)) - 0.09f;
                    return Mathf.Min(a, b);
                }

                case IconKind.RoutePath:
                {
                    float startDot = SdCircle(p, new Vector2(-0.48f, -0.48f), 0.16f);
                    float endRing = SdRing(p, new Vector2(0.48f, 0.48f), 0.16f, 0.07f);
                    float seg1 = SdSegment(p, new Vector2(-0.32f, -0.48f), new Vector2(0.32f, -0.48f)) - 0.065f;
                    float seg2 = SdSegment(p, new Vector2(0.32f, -0.48f), new Vector2(0.32f, 0.0f)) - 0.065f;
                    float seg3 = SdSegment(p, new Vector2(0.32f, 0.0f), new Vector2(-0.32f, 0.0f)) - 0.065f;
                    float seg4 = SdSegment(p, new Vector2(-0.32f, 0.0f), new Vector2(-0.32f, 0.48f)) - 0.065f;
                    float seg5 = SdSegment(p, new Vector2(-0.32f, 0.48f), new Vector2(0.26f, 0.48f)) - 0.065f;
                    float segs = Mathf.Min(Mathf.Min(seg1, seg2), Mathf.Min(seg3, Mathf.Min(seg4, seg5)));
                    return Mathf.Min(Mathf.Min(startDot, endRing), segs);
                }

                case IconKind.Layers:
                {
                    // Top solid diamond + two lower chevrons
                    var q = new Vector2(p.x, p.y - 0.34f);
                    float topDiamond = Mathf.Max(
                        Mathf.Abs(q.x * 0.78f + q.y * 1.45f) - 0.48f,
                        Mathf.Abs(q.x * 0.78f - q.y * 1.45f) - 0.48f) * 0.55f - 0.02f;

                    float mid1 = SdSegment(p, new Vector2(-0.62f, 0.04f), new Vector2(0f, -0.26f)) - 0.065f;
                    float mid2 = SdSegment(p, new Vector2(0f, -0.26f), new Vector2(0.62f, 0.04f)) - 0.065f;
                    float bot1 = SdSegment(p, new Vector2(-0.62f, -0.32f), new Vector2(0f, -0.62f)) - 0.065f;
                    float bot2 = SdSegment(p, new Vector2(0f, -0.62f), new Vector2(0.62f, -0.32f)) - 0.065f;
                    return Mathf.Min(topDiamond, Mathf.Min(Mathf.Min(mid1, mid2), Mathf.Min(bot1, bot2)));
                }

                case IconKind.Activity:
                {
                    float s1 = SdSegment(p, new Vector2(-0.68f, 0f), new Vector2(-0.36f, 0f)) - 0.07f;
                    float s2 = SdSegment(p, new Vector2(-0.36f, 0f), new Vector2(-0.14f, 0.48f)) - 0.07f;
                    float s3 = SdSegment(p, new Vector2(-0.14f, 0.48f), new Vector2(0.14f, -0.48f)) - 0.07f;
                    float s4 = SdSegment(p, new Vector2(0.14f, -0.48f), new Vector2(0.36f, 0f)) - 0.07f;
                    float s5 = SdSegment(p, new Vector2(0.36f, 0f), new Vector2(0.68f, 0f)) - 0.07f;
                    return Mathf.Min(Mathf.Min(s1, s2), Mathf.Min(s3, Mathf.Min(s4, s5)));
                }

                case IconKind.Refresh:
                {
                    float angle = Mathf.Atan2(p.y, p.x);
                    float ring = SdRing(p, Vector2.zero, 0.50f, 0.075f);
                    // Leave a clean opening in the upper-right quadrant (15°..72°)
                    float gap = (angle > 0.25f && angle < 1.25f) ? 0.25f : -1f;
                    float arc = Mathf.Max(ring, gap);
                    float arrow = SdTriangle(
                        p,
                        new Vector2(0.58f, 0.16f),
                        new Vector2(0.24f, 0.12f),
                        new Vector2(0.52f, 0.50f)) - 0.02f;
                    return Mathf.Min(arc, arrow);
                }

                case IconKind.AlertCircle:
                {
                    float ring = SdRing(p, Vector2.zero, 0.64f, 0.08f);
                    float stem = SdSegment(p, new Vector2(0f, 0.30f), new Vector2(0f, -0.05f)) - 0.075f;
                    float dot = SdCircle(p, new Vector2(0f, -0.31f), 0.08f);
                    return Mathf.Min(ring, Mathf.Min(stem, dot));
                }

                case IconKind.Plus:
                {
                    float vBar = SdSegment(p, new Vector2(0f, -0.52f), new Vector2(0f, 0.52f)) - 0.09f;
                    float hBar = SdSegment(p, new Vector2(-0.52f, 0f), new Vector2(0.52f, 0f)) - 0.09f;
                    return Mathf.Min(vBar, hBar);
                }

                case IconKind.Edit:
                {
                    float shaft = SdSegment(p, new Vector2(-0.36f, -0.36f), new Vector2(0.46f, 0.46f)) - 0.11f;
                    float tip = SdTriangle(
                        p,
                        new Vector2(-0.58f, -0.58f),
                        new Vector2(-0.46f, -0.22f),
                        new Vector2(-0.22f, -0.46f)) - 0.02f;
                    float baseLine = SdSegment(p, new Vector2(-0.18f, -0.60f), new Vector2(0.58f, -0.60f)) - 0.065f;
                    return Mathf.Min(Mathf.Min(shaft, tip), baseLine);
                }

                case IconKind.Trash:
                {
                    float lid = SdSegment(p, new Vector2(-0.52f, 0.42f), new Vector2(0.52f, 0.42f)) - 0.07f;
                    float handle = SdSegment(p, new Vector2(-0.18f, 0.56f), new Vector2(0.18f, 0.56f)) - 0.065f;
                    float outer = SdRoundedBox(p - new Vector2(0f, -0.14f), new Vector2(0.38f, 0.46f), 0.08f);
                    float inner = SdRoundedBox(p - new Vector2(0f, -0.12f), new Vector2(0.24f, 0.34f), 0.04f);
                    float bin = Mathf.Max(outer, -inner);
                    float barL = SdSegment(p, new Vector2(-0.10f, -0.30f), new Vector2(-0.10f, 0.06f)) - 0.055f;
                    float barR = SdSegment(p, new Vector2(0.10f, -0.30f), new Vector2(0.10f, 0.06f)) - 0.055f;
                    return Mathf.Min(Mathf.Min(lid, handle), Mathf.Min(bin, Mathf.Min(barL, barR)));
                }

                case IconKind.Copy:
                {
                    float frontOuter = SdRoundedBox(p - new Vector2(-0.14f, -0.14f), new Vector2(0.38f, 0.44f), 0.09f);
                    float frontInner = SdRoundedBox(p - new Vector2(-0.14f, -0.14f), new Vector2(0.24f, 0.30f), 0.04f);
                    float front = Mathf.Max(frontOuter, -frontInner);
                    float backTop = SdSegment(p, new Vector2(-0.18f, 0.58f), new Vector2(0.46f, 0.58f)) - 0.065f;
                    float backRight = SdSegment(p, new Vector2(0.46f, 0.58f), new Vector2(0.46f, -0.18f)) - 0.065f;
                    return Mathf.Min(front, Mathf.Min(backTop, backRight));
                }

                case IconKind.VolumeOn:
                {
                    float speaker = SpeakerBody(p);
                    float arcOriginX = 0.02f;
                    float waveInner = (p.x > 0.16f)
                        ? SdRing(p, new Vector2(arcOriginX, 0f), 0.30f, 0.06f)
                        : 1f;
                    float waveOuter = (p.x > 0.24f)
                        ? SdRing(p, new Vector2(arcOriginX, 0f), 0.54f, 0.06f)
                        : 1f;
                    return Mathf.Min(speaker, Mathf.Min(waveInner, waveOuter));
                }

                case IconKind.VolumeOff:
                {
                    float speaker = SpeakerBody(p);
                    float x1 = SdSegment(p, new Vector2(0.24f, 0.24f), new Vector2(0.62f, -0.24f)) - 0.065f;
                    float x2 = SdSegment(p, new Vector2(0.24f, -0.24f), new Vector2(0.62f, 0.24f)) - 0.065f;
                    return Mathf.Min(speaker, Mathf.Min(x1, x2));
                }

                default:
                    return SdCircle(p, Vector2.zero, 0.5f);
            }
        }

        private static float SpeakerBody(Vector2 p)
        {
            float body = SdRoundedBox(p - new Vector2(-0.42f, 0f), new Vector2(0.18f, 0.20f), 0.05f);
            float cone = SdTriangle(
                p,
                new Vector2(-0.32f, 0.18f),
                new Vector2(0.06f, 0.50f),
                new Vector2(0.06f, -0.50f));
            float coneBottom = SdTriangle(
                p,
                new Vector2(-0.32f, 0.18f),
                new Vector2(0.06f, -0.50f),
                new Vector2(-0.32f, -0.18f));
            return Mathf.Min(body, Mathf.Min(cone, coneBottom));
        }

        private static float SliderRow(Vector2 p, float y, float knobX)
        {
            float line = SdSegment(p, new Vector2(-0.62f, y), new Vector2(0.62f, y)) - 0.055f;
            float knobOuter = SdCircle(p, new Vector2(knobX, y), 0.155f);
            float knobInner = SdCircle(p, new Vector2(knobX, y), 0.065f);
            float lineCut = Mathf.Max(line, -knobOuter);
            float knobRing = Mathf.Max(knobOuter, -knobInner);
            return Mathf.Min(lineCut, knobRing);
        }

        private static float CornerBracket(Vector2 p, float sx, float sy)
        {
            var corner = new Vector2(0.64f * sx, 0.64f * sy);
            var hEnd = new Vector2(0.30f * sx, 0.64f * sy);
            var vEnd = new Vector2(0.64f * sx, 0.30f * sy);
            return Mathf.Min(
                SdSegment(p, corner, hEnd) - 0.075f,
                SdSegment(p, corner, vEnd) - 0.075f);
        }

        private static float SdCircle(Vector2 p, Vector2 center, float radius)
        {
            return (p - center).magnitude - radius;
        }

        private static float SdRing(Vector2 p, Vector2 center, float radius, float halfWidth)
        {
            return Mathf.Abs((p - center).magnitude - radius) - halfWidth;
        }

        private static float SdRoundedBox(Vector2 p, Vector2 halfSize, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - halfSize + new Vector2(radius, radius);
            return Mathf.Min(Mathf.Max(q.x, q.y), 0f) + new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude - radius;
        }

        private static float SdSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float denom = Vector2.Dot(ba, ba);
            float h = denom > 1e-6f ? Mathf.Clamp01(Vector2.Dot(pa, ba) / denom) : 0f;
            return (pa - ba * h).magnitude;
        }

        private static float SdTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 e0 = b - a, e1 = c - b, e2 = a - c;
            Vector2 v0 = p - a, v1 = p - b, v2 = p - c;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d = Vector2.Min(
                Vector2.Min(
                    new Vector2(Vector2.Dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                    new Vector2(Vector2.Dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                new Vector2(Vector2.Dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }

        private static float SdSmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }
    }
}
