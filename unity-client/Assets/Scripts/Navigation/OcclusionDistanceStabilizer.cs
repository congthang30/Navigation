using System;
using NianticSpatial.NSDK.AR.Occlusion;
using UnityEngine;

namespace Vps.UnityClient.Navigation
{
    /// <summary>
    /// Produces a stable "back projection plane" distance for the NSDK occlusion depth warp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The NSDK occlusion extension reprojects the depth image onto the current camera pose using a
    /// single back projection plane distance (<c>XRDisplayContext.OccludeeEyeDepth</c>).
    /// NianticSpatial.NSDK.AR.Occlusion.OcclusionExtensionUtils.SampleImageSubregion computes that
    /// distance as the <b>closest</b> depth sample inside the sampling region
    /// (it keeps the minimum of a 6x3 sparse grid), which is documented as "average depth from the
    /// whole field of view" but is implemented as a minimum.
    /// </para>
    /// <para>
    /// A minimum is a badly conditioned estimator for a back projection plane: as soon as anything
    /// close enters the frame (a chair, a passer-by, the floor right below the camera) the plane
    /// collapses towards the camera, and the whole depth buffer is warped with the wrong reference
    /// plane. The result is large areas of the screen being wrongly occluded, and the reference
    /// distance jumping from frame to frame, which reads as "depth occlusion is not smooth".
    /// </para>
    /// <para>
    /// This class computes a robust estimate instead (median of a sparse grid, which ignores a
    /// minority of near samples) and smooths it over time so the reference plane cannot jump.
    /// The result is written back into <c>XRDisplayContext.OccludeeEyeDepth</c> using only public
    /// NSDK API - the SDK package itself is never modified.
    /// </para>
    /// </remarks>
    public sealed class OcclusionDistanceStabilizer
    {
        /// <summary>Lower clamp for the occlusion reference distance (meters).</summary>
        public const float DefaultMinDistanceMeters = 0.25f;

        /// <summary>Upper clamp for the occlusion reference distance (meters).</summary>
        public const float DefaultMaxDistanceMeters = 12f;

        /// <summary>Number of samples per axis of the sparse sampling grid.</summary>
        public const int DefaultGridSize = 5;

        /// <summary>Relative change above which the smoothing becomes deliberately slow.</summary>
        public const float LargeJumpThreshold = 0.35f;

        /// <summary>Smoothing factor used for small changes.</summary>
        public const float NormalSmoothingFactor = 0.25f;

        /// <summary>Smoothing factor used for large changes (hysteresis).</summary>
        public const float LargeJumpSmoothingFactor = 0.08f;

        private readonly float[] rawSamples = new float[DefaultGridSize * DefaultGridSize];

        /// <summary>
        /// Scratch buffer for the median computation. Unity drives the occlusion pipeline from the
        /// main thread only, so a shared buffer is safe and keeps the per frame allocation at zero.
        /// </summary>
        private static readonly float[] sortedSamples = new float[DefaultGridSize * DefaultGridSize];

        private float smoothedDistance;
        private float lastRawDistance;
        private bool hasValue;

        /// <summary>Current smoothed occlusion reference distance (meters).</summary>
        public float SmoothedDistance => smoothedDistance;

        /// <summary>Last raw (unsmoothed) robust estimate (meters).</summary>
        public float LastRawDistance => lastRawDistance;

        /// <summary>Whether a smoothed value has been produced at least once.</summary>
        public bool HasValue => hasValue;

        /// <summary>Number of valid depth samples used by the last estimate.</summary>
        public int LastValidSampleCount { get; private set; }

        /// <summary>Clears the temporal state (used when occlusion is disabled or re-configured).</summary>
        public void Reset()
        {
            hasValue = false;
            smoothedDistance = 0f;
            lastRawDistance = 0f;
            LastValidSampleCount = 0;
        }

        /// <summary>
        /// Applies the temporal filter to a raw robust estimate.
        /// </summary>
        /// <param name="rawDistance">Robust distance estimate in meters.</param>
        /// <param name="deltaTime">Frame delta time in seconds (unused, kept for future frame rate compensation).</param>
        /// <returns>The smoothed distance in meters.</returns>
        public float Smooth(float rawDistance, float deltaTime)
        {
            float clamped = Mathf.Clamp(rawDistance, DefaultMinDistanceMeters, DefaultMaxDistanceMeters);
            lastRawDistance = clamped;

            if (!hasValue || !IsFinite(smoothedDistance) || smoothedDistance <= 0f)
            {
                smoothedDistance = clamped;
                hasValue = true;
                return smoothedDistance;
            }

            // Hysteresis: a large jump is followed slowly so a single frame outlier (someone walking
            // in front of the camera) cannot move the reference plane, while small changes are tracked
            // quickly enough to follow the user walking towards a wall.
            float relative = Mathf.Abs(clamped - smoothedDistance) / Mathf.Max(smoothedDistance, 0.1f);
            float factor = relative > LargeJumpThreshold ? LargeJumpSmoothingFactor : NormalSmoothingFactor;

            smoothedDistance = Mathf.Lerp(smoothedDistance, clamped, factor);
            smoothedDistance = Mathf.Clamp(smoothedDistance, DefaultMinDistanceMeters, DefaultMaxDistanceMeters);
            return smoothedDistance;
        }

        /// <summary>
        /// Estimates a robust occlusion reference distance from the live depth image.
        /// </summary>
        /// <param name="extension">The NSDK occlusion extension.</param>
        /// <param name="region">Normalized screen region to sample (0..1).</param>
        /// <param name="distance">The robust estimate in meters.</param>
        /// <returns>True when at least one valid depth sample was available.</returns>
        public bool TryEstimate(NsdkOcclusionExtension extension, Rect region, out float distance)
        {
            distance = 0f;
            LastValidSampleCount = 0;

            if (extension == null || !extension.enabled)
            {
                return false;
            }

            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            int count = 0;
            for (int gy = 0; gy < DefaultGridSize; gy++)
            {
                float fy = (gy + 0.5f) / DefaultGridSize;
                int py = Mathf.Clamp(Mathf.RoundToInt((region.yMin + fy * region.height) * height), 0, height - 1);
                for (int gx = 0; gx < DefaultGridSize; gx++)
                {
                    float fx = (gx + 0.5f) / DefaultGridSize;
                    int px = Mathf.Clamp(Mathf.RoundToInt((region.xMin + fx * region.width) * width), 0, width - 1);

                    if (!extension.TryGetDepth(px, py, out float sample))
                    {
                        continue;
                    }

                    if (!IsFinite(sample) || sample <= 0.05f || sample > 100f)
                    {
                        continue;
                    }

                    if (count < rawSamples.Length)
                    {
                        rawSamples[count++] = sample;
                    }
                }
            }

            LastValidSampleCount = count;
            if (count == 0)
            {
                return false;
            }

            distance = Median(rawSamples, count);
            return true;
        }

        /// <summary>
        /// Computes the median of the first <paramref name="count"/> entries of the buffer.
        /// </summary>
        public static float Median(float[] buffer, int count)
        {
            if (buffer == null || count <= 0)
            {
                return 0f;
            }

            int n = Mathf.Min(count, buffer.Length);
            Array.Copy(buffer, sortedSamples, n);
            Array.Sort(sortedSamples, 0, n);
            return n % 2 == 1
                ? sortedSamples[n / 2]
                : (sortedSamples[n / 2 - 1] + sortedSamples[n / 2]) * 0.5f;
        }

        /// <summary>
        /// Computes the normalized screen rectangle covered by a renderer.
        /// </summary>
        public static bool TryGetScreenRect(Renderer renderer, Camera camera, out Rect rect)
        {
            rect = new Rect(0f, 0f, 1f, 1f);
            if (renderer == null || camera == null)
            {
                return false;
            }

            Bounds bounds = renderer.bounds;
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;

            float maxX = 0f, maxY = 0f, minX = 1f, minY = 1f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? c.x - e.x : c.x + e.x,
                    (i & 2) == 0 ? c.y - e.y : c.y + e.y,
                    (i & 4) == 0 ? c.z - e.z : c.z + e.z);
                Vector3 vp = camera.WorldToViewportPoint(corner);
                if (vp.z <= 0f)
                {
                    // Behind the camera: the whole renderer cannot be trusted, fall back to the
                    // central region instead of producing a degenerate rectangle.
                    return false;
                }

                maxX = Mathf.Max(maxX, vp.x);
                minX = Mathf.Min(minX, vp.x);
                maxY = Mathf.Max(maxY, vp.y);
                minY = Mathf.Min(minY, vp.y);
            }

            minX = Mathf.Clamp01(minX);
            minY = Mathf.Clamp01(minY);
            maxX = Mathf.Clamp01(maxX);
            maxY = Mathf.Clamp01(maxY);
            if (maxX - minX <= 0.01f || maxY - minY <= 0.01f)
            {
                return false;
            }

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
