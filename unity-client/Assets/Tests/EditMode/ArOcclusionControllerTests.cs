using NianticSpatial.NSDK.AR.Occlusion;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class ArOcclusionControllerTests
    {
        [Test]
        public void InitializeDefaultsToBalancedAndConfiguresNsdkOcclusionExtension()
        {
            var originGo = new GameObject("XR Origin");
            var camGo = new GameObject("AR Camera");
            camGo.transform.SetParent(originGo.transform, false);
            var hostGo = new GameObject("NavigationHost");

            try
            {
                var xrOrigin = originGo.AddComponent<XROrigin>();
                var camera = camGo.AddComponent<Camera>();
                xrOrigin.Camera = camera;

                var controller = hostGo.AddComponent<ArOcclusionController>();
                controller.Initialize(camera, xrOrigin);

                Assert.That(controller.IsInitialized, Is.True);
                Assert.That(controller.RequestedMode, Is.EqualTo(ArOcclusionQualityMode.Balanced));
                Assert.That(controller.EffectiveMode, Is.EqualTo(ArOcclusionQualityMode.Balanced));

                var occMgr = camGo.GetComponent<AROcclusionManager>();
                var nsdkOcc = camGo.GetComponent<NsdkOcclusionExtension>();

                Assert.That(occMgr, Is.Not.Null);
                Assert.That(nsdkOcc, Is.Not.Null);
                Assert.That(occMgr.enabled, Is.True);
                Assert.That(occMgr.requestedEnvironmentDepthMode, Is.EqualTo(EnvironmentDepthMode.Fastest));
                Assert.That(nsdkOcc.enabled, Is.True);
                Assert.That(controller.IsBypassUpdatesConfigured, Is.True);
                Assert.That(nsdkOcc.OverrideOcclusionManagerSettings, Is.True);
                Assert.That(controller.PreferSmoothEdges, Is.True);
                Assert.That(nsdkOcc.TargetFrameRate, Is.EqualTo(ArOcclusionController.BalancedTargetFrameRate));
                Assert.That(nsdkOcc.OcclusionDistanceMode, Is.EqualTo(NsdkOcclusionExtension.OptimalOcclusionDistanceMode.ClosestOccluder));
            }
            finally
            {
                Object.DestroyImmediate(hostGo);
                Object.DestroyImmediate(originGo);
            }
        }

        [Test]
        public void ModeTransitionsProperlyToggleComponentsBetweenOffFallbackBalancedAndHighQuality()
        {
            var originGo = new GameObject("XR Origin");
            var camGo = new GameObject("AR Camera");
            camGo.transform.SetParent(originGo.transform, false);
            var hostGo = new GameObject("NavigationHost");

            try
            {
                var xrOrigin = originGo.AddComponent<XROrigin>();
                var camera = camGo.AddComponent<Camera>();
                xrOrigin.Camera = camera;

                var controller = hostGo.AddComponent<ArOcclusionController>();
                controller.Initialize(camera, xrOrigin);

                var occMgr = camGo.GetComponent<AROcclusionManager>();
                var nsdkOcc = camGo.GetComponent<NsdkOcclusionExtension>();

                // 1. Switch to Off
                controller.SetQualityMode(ArOcclusionQualityMode.Off);
                Assert.That(controller.EffectiveMode, Is.EqualTo(ArOcclusionQualityMode.Off));
                Assert.That(occMgr.enabled, Is.False);
                Assert.That(occMgr.requestedEnvironmentDepthMode, Is.EqualTo(EnvironmentDepthMode.Disabled));
                Assert.That(nsdkOcc.enabled, Is.False);

                // 2. Switch to Fallback (ARFoundation Instant Depth only)
                controller.SetQualityMode(ArOcclusionQualityMode.Fallback);
                Assert.That(controller.EffectiveMode, Is.EqualTo(ArOcclusionQualityMode.Fallback));
                Assert.That(occMgr.enabled, Is.True);
                Assert.That(occMgr.requestedEnvironmentDepthMode, Is.EqualTo(EnvironmentDepthMode.Fastest));
                Assert.That(nsdkOcc.enabled, Is.False);

                // 3. Switch to HighQuality
                controller.SetQualityMode(ArOcclusionQualityMode.HighQuality);
                if (controller.IsStabilizationSupportedByGraphicsApi)
                {
                    Assert.That(controller.EffectiveMode, Is.EqualTo(ArOcclusionQualityMode.HighQuality));
                    Assert.That(nsdkOcc.enabled, Is.True);
                    Assert.That(nsdkOcc.TargetFrameRate, Is.EqualTo(ArOcclusionController.HighQualityTargetFrameRate));
                    Assert.That(controller.IsStabilizationConfigured, Is.True);
                    Assert.That(originGo.GetComponentInChildren<ARMeshManager>(true), Is.Not.Null);
                }
                else
                {
                    // On Vulkan or headless environments where Stabilization is unsupported, degrades to Balanced
                    Assert.That(controller.EffectiveMode, Is.EqualTo(ArOcclusionQualityMode.Balanced));
                }

                // 4. Switch back to Balanced disables runtime meshing cleanly
                controller.SetQualityMode(ArOcclusionQualityMode.Balanced);
                Assert.That(controller.EffectiveMode, Is.EqualTo(ArOcclusionQualityMode.Balanced));
                Assert.That(controller.IsStabilizationConfigured, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(hostGo);
                Object.DestroyImmediate(originGo);
            }
        }

        [Test]
        public void PrincipalOccludeeBindingTracksActiveRendererAndFallsBackToClosestOccluderWhenNull()
        {
            var originGo = new GameObject("XR Origin");
            var camGo = new GameObject("AR Camera");
            camGo.transform.SetParent(originGo.transform, false);
            var hostGo = new GameObject("NavigationHost");
            var characterGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            try
            {
                var xrOrigin = originGo.AddComponent<XROrigin>();
                var camera = camGo.AddComponent<Camera>();
                xrOrigin.Camera = camera;

                var controller = hostGo.AddComponent<ArOcclusionController>();
                controller.Initialize(camera, xrOrigin);

                var nsdkOcc = camGo.GetComponent<NsdkOcclusionExtension>();
                var renderer = characterGo.GetComponent<Renderer>();

                characterGo.SetActive(true);
                controller.SetPrincipalOccludee(renderer);
                Assert.That(
                    nsdkOcc.OcclusionDistanceMode,
                    Is.EqualTo(NsdkOcclusionExtension.OptimalOcclusionDistanceMode.SpecifiedGameObject)
                );

                // Clearing principal occludee falls back to ClosestOccluder so no warning is logged
                controller.SetPrincipalOccludee(null);
                Assert.That(
                    nsdkOcc.OcclusionDistanceMode,
                    Is.EqualTo(NsdkOcclusionExtension.OptimalOcclusionDistanceMode.ClosestOccluder)
                );
            }
            finally
            {
                Object.DestroyImmediate(characterGo);
                Object.DestroyImmediate(hostGo);
                Object.DestroyImmediate(originGo);
            }
        }

        [Test]
        public void RuntimeMeshDebugToggleControlsCameraCullingMaskForOcclusionMeshLayer()
        {
            var originGo = new GameObject("XR Origin");
            var camGo = new GameObject("AR Camera");
            camGo.transform.SetParent(originGo.transform, false);
            var hostGo = new GameObject("NavigationHost");

            try
            {
                var xrOrigin = originGo.AddComponent<XROrigin>();
                var camera = camGo.AddComponent<Camera>();
                xrOrigin.Camera = camera;

                var controller = hostGo.AddComponent<ArOcclusionController>();
                controller.Initialize(camera, xrOrigin);

                int layerBit = 1 << ArOcclusionController.RuntimeOcclusionMeshLayer;
                Assert.That(camera.cullingMask & layerBit, Is.EqualTo(0), "Runtime occlusion mesh layer should be hidden from main camera by default");

                controller.RuntimeMeshDebugEnabled = true;
                Assert.That(camera.cullingMask & layerBit, Is.Not.EqualTo(0), "Runtime occlusion mesh layer should be visible when debug is enabled");

                controller.RuntimeMeshDebugEnabled = false;
                Assert.That(camera.cullingMask & layerBit, Is.EqualTo(0), "Runtime occlusion mesh layer should be hidden again when debug is disabled");
            }
            finally
            {
                Object.DestroyImmediate(hostGo);
                Object.DestroyImmediate(originGo);
            }
        }

        [Test]
        public void ApplyRouteMaterialDepthSettingsSetsDepthBiasMeters()
        {
            var hostGo = new GameObject("NavigationHost");
            Shader shader = Shader.Find("Vps/ARRouteLine");
            Assert.That(shader, Is.Not.Null, "Shader Vps/ARRouteLine must exist");
            var mat = new Material(shader);

            try
            {
                var controller = hostGo.AddComponent<ArOcclusionController>();
                controller.RouteDepthBiasMeters = 0.08f;
                controller.ApplyRouteMaterialDepthSettings(mat);

                Assert.That(mat.GetFloat("_DepthBiasMeters"), Is.EqualTo(0.08f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(mat);
                Object.DestroyImmediate(hostGo);
            }
        }
    }
}
