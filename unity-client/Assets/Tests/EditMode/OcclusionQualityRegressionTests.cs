using System.IO;
using NUnit.Framework;
using UnityEngine;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    /// <summary>
    /// Regression tests for the AR occlusion quality fixes:
    /// robust occlusion reference distance, route ribbon depth bias and companion contact handling.
    /// </summary>
    public sealed class OcclusionQualityRegressionTests
    {
        // ---------------------------------------------------------------------
        // Occlusion reference distance (back projection plane)
        // ---------------------------------------------------------------------

        [Test]
        public void MedianIgnoresASingleNearOutlier()
        {
            // The NSDK uses the CLOSEST depth sample of the field of view as the back projection
            // plane, which collapses as soon as something close enters the frame. A median must
            // ignore that outlier.
            float[] samples = { 0.35f, 2.0f, 2.1f, 2.2f, 2.3f };
            float median = OcclusionDistanceStabilizer.Median(samples, samples.Length);

            Assert.That(median, Is.EqualTo(2.1f).Within(0.001f));
            Assert.That(median, Is.GreaterThan(samples[0]),
                "The median must not collapse onto the closest sample.");
        }

        [Test]
        public void MedianOfEvenSampleCountAveragesTheTwoMiddleValues()
        {
            float[] samples = { 1.0f, 2.0f, 3.0f, 4.0f };
            float median = OcclusionDistanceStabilizer.Median(samples, samples.Length);
            Assert.That(median, Is.EqualTo(2.5f).Within(0.001f));
        }

        [Test]
        public void SmoothingRejectsASingleFrameSpike()
        {
            var stabilizer = new OcclusionDistanceStabilizer();

            float first = stabilizer.Smooth(5.0f, 0.016f);
            Assert.That(first, Is.EqualTo(5.0f).Within(0.001f));

            // A passer-by right in front of the camera must not move the reference plane.
            float afterSpike = stabilizer.Smooth(0.3f, 0.016f);
            Assert.That(afterSpike, Is.GreaterThan(4.0f),
                "A single close outlier must not collapse the occlusion reference plane.");
            Assert.That(stabilizer.LastRawDistance, Is.EqualTo(0.3f).Within(0.001f),
                "The raw estimate is still reported for diagnostics.");
        }

        [Test]
        public void SmoothingRecoversFromASustainedChange()
        {
            var stabilizer = new OcclusionDistanceStabilizer();
            stabilizer.Smooth(5.0f, 0.016f);

            float value = 5.0f;
            for (int i = 0; i < 120; i++)
            {
                value = stabilizer.Smooth(1.5f, 0.016f);
            }

            Assert.That(value, Is.LessThan(2.0f),
                "A sustained change of the scene depth must eventually be followed.");
        }

        [Test]
        public void SmoothingTracksSmallChangesQuickly()
        {
            var stabilizer = new OcclusionDistanceStabilizer();
            stabilizer.Smooth(3.0f, 0.016f);

            float value = stabilizer.Smooth(3.2f, 0.016f);
            Assert.That(value, Is.GreaterThanOrEqualTo(3.04f).And.LessThan(3.25f),
                "Small changes must be tracked quickly so walking towards a wall stays aligned.");
        }

        [Test]
        public void SmoothingClampsToTheSupportedDistanceRange()
        {
            // The very first sample is adopted directly (clamped), so the range limits are enforced
            // immediately instead of being approached over many frames.
            var nearStabilizer = new OcclusionDistanceStabilizer();
            float tooClose = nearStabilizer.Smooth(0.01f, 0.016f);
            Assert.That(tooClose, Is.EqualTo(OcclusionDistanceStabilizer.DefaultMinDistanceMeters).Within(0.001f));

            var farStabilizer = new OcclusionDistanceStabilizer();
            float tooFar = farStabilizer.Smooth(500f, 0.016f);
            Assert.That(tooFar, Is.EqualTo(OcclusionDistanceStabilizer.DefaultMaxDistanceMeters).Within(0.001f));
        }

        [Test]
        public void LargeJumpsAreFollowedSlowlyToAvoidPlaneCollisions()
        {
            var stabilizer = new OcclusionDistanceStabilizer();
            stabilizer.Smooth(5.0f, 0.016f);

            float value = stabilizer.Smooth(1.0f, 0.016f);
            Assert.That(value, Is.GreaterThan(4.0f),
                "A large jump must be damped by the hysteresis factor, not applied instantly.");
            Assert.That(value, Is.LessThan(5.0f), "but it still has to move towards the new value.");
        }

        [Test]
        public void ResetClearsTheTemporalState()
        {
            var stabilizer = new OcclusionDistanceStabilizer();
            stabilizer.Smooth(4.0f, 0.016f);
            Assert.That(stabilizer.HasValue, Is.True);

            stabilizer.Reset();

            Assert.That(stabilizer.HasValue, Is.False);
            Assert.That(stabilizer.SmoothedDistance, Is.EqualTo(0f).Within(0.0001f));
        }

        // ---------------------------------------------------------------------
        // Route ribbon depth bias
        // ---------------------------------------------------------------------

        [Test]
        public void RouteLineShaderAppliesTheBiasAlongTheViewAxisOnly()
        {
            string path = Path.Combine(Application.dataPath, "Shaders/ARRouteLine.shader");
            Assert.That(File.Exists(path), Is.True, "ARRouteLine.shader must exist");
            string source = File.ReadAllText(path);

            Assert.That(source, Does.Contain("viewPos.z -= _DepthBiasMeters"),
                "The depth bias must only change the view space Z component.");
            Assert.That(source, Does.Not.Contain("viewPos *="),
                "Scaling the whole view vector also moves the vertices laterally and distorts the route.");
        }

        [Test]
        public void RouteLineShaderDoesNotWriteDepth()
        {
            string path = Path.Combine(Application.dataPath, "Shaders/ARRouteLine.shader");
            string source = File.ReadAllText(path);

            Assert.That(source, Does.Contain("ZWrite Off"),
                "The blended ribbon must not write depth, otherwise it clips transparent content drawn after it.");
        }

        [Test]
        public void RouteLineShaderDefaultBiasIsSmall()
        {
            string path = Path.Combine(Application.dataPath, "Shaders/ARRouteLine.shader");
            string source = File.ReadAllText(path);

            Assert.That(source, Does.Contain("_DepthBiasMeters (\"Floor Depth Bias (m)\", Float) = 0.03"),
                "The shader default must match ArOcclusionController.DefaultRouteDepthBiasMeters.");
        }

        [Test]
        public void RouteLineShaderSupportsAnimatedChevronHologramAndSmoothCorners()
        {
            string path = Path.Combine(Application.dataPath, "Shaders/ARRouteLine.shader");
            string source = File.ReadAllText(path);
            Assert.That(source, Does.Contain("o.eyeDist = -viewPos.z"),
                "Unity view space has negative Z in front of the camera; eyeDist must negate viewPos.z or nearFade renders 0 alpha.");

            Shader shader = Shader.Find("Vps/ARRouteLine");
            Assert.That(shader, Is.Not.Null, "Shader Vps/ARRouteLine must compile cleanly.");
            Assert.That(shader.isSupported, Is.True);

            var mat = new Material(shader);
            try
            {
                Assert.That(mat.HasProperty("_ScrollSpeed"), Is.True);
                Assert.That(mat.HasProperty("_ChevronFreq"), Is.True);
                Assert.That(mat.HasProperty("_CoreGlow"), Is.True);
                Assert.That(mat.HasProperty("_DepthBiasMeters"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mat);
            }

            Vector3[] sharpTurn =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 3f),
                new Vector3(3f, 0f, 3f),
            };
            Vector3[] smoothed = SiteGraphV2NavigationExperience.BuildSmoothRenderRoutePoints(sharpTurn);
            Assert.That(smoothed.Length, Is.GreaterThan(sharpTurn.Length));
            Assert.That(smoothed[0], Is.EqualTo(sharpTurn[0]));
            Assert.That(smoothed[smoothed.Length - 1], Is.EqualTo(sharpTurn[2]));
            for (int i = 1; i < smoothed.Length; i++)
            {
                Assert.That(Vector3.Distance(smoothed[i - 1], smoothed[i]), Is.GreaterThan(0.01f));
            }
        }

        // ---------------------------------------------------------------------
        // Controller behaviour
        // ---------------------------------------------------------------------

        [Test]
        public void StabilizedOcclusionDistanceIsEnabledByDefaultAndCanBeToggled()
        {
            var hostGo = new GameObject("NavigationHost");
            try
            {
                var controller = hostGo.AddComponent<ArOcclusionController>();
                Assert.That(controller.UseStabilizedOcclusionDistance, Is.True,
                    "The robust occlusion reference distance must be the default behaviour.");

                controller.UseStabilizedOcclusionDistance = false;
                Assert.That(controller.UseStabilizedOcclusionDistance, Is.False);

                controller.UseStabilizedOcclusionDistance = true;
                Assert.That(controller.UseStabilizedOcclusionDistance, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(hostGo);
            }
        }

        [Test]
        public void OffModeResolvesToOffAndBalancedStaysBalanced()
        {
            var hostGo = new GameObject("NavigationHost");
            try
            {
                var controller = hostGo.AddComponent<ArOcclusionController>();
                Assert.That(controller.ResolveEffectiveMode(ArOcclusionQualityMode.Off), Is.EqualTo(ArOcclusionQualityMode.Off));
                Assert.That(controller.ResolveEffectiveMode(ArOcclusionQualityMode.Balanced), Is.EqualTo(ArOcclusionQualityMode.Balanced));
            }
            finally
            {
                Object.DestroyImmediate(hostGo);
            }
        }

        // ---------------------------------------------------------------------
        // Companion contact handling
        // ---------------------------------------------------------------------

        [Test]
        public void CompanionModelPivotSitsOnTheMeshBase()
        {
            // The ground drop can only stay small when the model pivot is already on the mesh base.
            GameObject prefab = Resources.Load<GameObject>("navigation");
            Assert.That(prefab, Is.Not.Null, "navigation prefab must load from Resources");

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0));

                bool hasBounds = false;
                Bounds bounds = new Bounds();
                foreach (Renderer r in renderers)
                {
                    if (r == null) continue;
                    if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                    else bounds.Encapsulate(r.bounds);
                }

                float minY = bounds.min.y - instance.transform.position.y;
                Assert.That(minY, Is.LessThan(0.02f).And.GreaterThan(-0.02f),
                    "The companion pivot must be on the mesh base, otherwise the feet float above the floor.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void DefaultGroundDropKeepsTheFeetCloseToTheFloor()
        {
            Assert.That(DoraemonCompanionGuide.DefaultGroundDrop, Is.EqualTo(0.015f).Within(0.0001f),
                "An 8 cm sink pushed the companion feet into the real floor where AR depth noise clipped them.");
            Assert.That(DoraemonCompanionGuide.LegacyDefaultGroundDrop, Is.EqualTo(0.08f).Within(0.0001f),
                "The legacy default is kept so persisted user settings can be migrated.");
        }

        [Test]
        public void CompanionUsesTheContactAwareOcclusionShader()
        {
            var hostGo = new GameObject("TestCompanion");
            var camGo = new GameObject("AR Camera");
            try
            {
                var camera = camGo.AddComponent<Camera>();
                var guide = hostGo.AddComponent<DoraemonCompanionGuide>();
                guide.Initialize(null, camera);

                GameObject instance = guide.CompanionInstance;
                Assert.That(instance, Is.Not.Null, "The companion model must be created");

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                int checkedMaterials = 0;
                foreach (Renderer r in renderers)
                {
                    if (r == null) continue;
                    foreach (Material mat in r.sharedMaterials)
                    {
                        if (mat == null) continue;
                        checkedMaterials++;
                        Assert.That(mat.shader.name, Is.EqualTo("Vps/ARCompanionOcclusion"),
                            "The companion must use the contact aware occlusion shader.");
                        Assert.That(mat.HasProperty("_ContactBiasMeters"), Is.True);
                        Assert.That(mat.GetFloat("_ContactBiasMeters"),
                            Is.EqualTo(DoraemonCompanionGuide.DefaultContactDepthBiasMeters).Within(0.0001f));
                        Assert.That(mat.GetFloat("_ContactBandMeters"),
                            Is.EqualTo(DoraemonCompanionGuide.DefaultContactBandMeters).Within(0.0001f));
                    }
                }

                Assert.That(checkedMaterials, Is.GreaterThan(0), "The companion must have materials to check");
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(hostGo);
            }
        }

        [Test]
        public void CompanionContactBiasIsClampedToASafeRange()
        {
            var hostGo = new GameObject("TestCompanion");
            try
            {
                var guide = hostGo.AddComponent<DoraemonCompanionGuide>();

                guide.ContactDepthBiasMeters = 0.5f;
                Assert.That(guide.ContactDepthBiasMeters, Is.EqualTo(0.1f).Within(0.0001f),
                    "A large contact bias would let the companion be seen through walls.");

                guide.ContactDepthBiasMeters = -1f;
                Assert.That(guide.ContactDepthBiasMeters, Is.EqualTo(0f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(hostGo);
            }
        }
    }
}
