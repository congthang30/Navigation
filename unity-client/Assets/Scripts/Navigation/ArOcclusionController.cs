using System;
using System.Collections.Generic;
using System.Reflection;
using NianticSpatial.NSDK.AR.Meshing;
using NianticSpatial.NSDK.AR.Occlusion;
using NianticSpatial.NSDK.AR.SceneSegmentation;
using NianticSpatial.NSDK.AR.Subsystems.SceneSegmentation;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace Vps.UnityClient.Navigation
{
    /// <summary>
    /// Mobile performance and visual quality tiers for AR Depth Occlusion.
    /// </summary>
    public enum ArOcclusionQualityMode
    {
        /// <summary>Disables all depth occlusion subsystems.</summary>
        Off = 0,

        /// <summary>Lightweight AR Foundation Instant Depth without NSDK post-processing or meshing.</summary>
        Fallback = 1,

        /// <summary>
        /// Production default for mobile/tablet: Instant Depth + NSDK Occlusion Extension
        /// (BypassOcclusionManagerUpdates, SmoothEdges, Principal Occludee tracking).
        /// </summary>
        Balanced = 2,

        /// <summary>
        /// High-end mode: Instant Depth + NSDK Occlusion Extension + Runtime Mesh-Blended Occlusion Stabilization
        /// when supported by graphics API and subsystem.
        /// </summary>
        HighQuality = 3
    }

    /// <summary>
    /// Production-ready AR Depth Occlusion controller integrating ARFoundation's AROcclusionManager,
    /// Niantic Spatial SDK's NsdkOcclusionExtension, runtime ARMeshManager + NsdkMeshingExtension
    /// for Occlusion Stabilization, and optional ARSceneSegmentationManager for Occlusion Suppression.
    /// Strictly isolated from VPS2 Localization, Route Recording, and downloaded VPS Site Meshes.
    /// </summary>
    public sealed class ArOcclusionController : MonoBehaviour
    {
        public const int RuntimeOcclusionMeshLayer = 8;

        /// <summary>
        /// Depth inference rate for Balanced mode. The NSDK caps the recommended rate at
        /// NsdkOcclusionSubsystem.MaxRecommendedFrameRate (20), so this is the highest rate the SDK
        /// supports without dropping frames.
        /// </summary>
        public const uint BalancedTargetFrameRate = 20u;

        /// <summary>
        /// Depth inference rate for HighQuality mode. Identical to Balanced on purpose: the SDK
        /// maximum recommended rate is 20 Hz, and HighQuality spends its extra budget on the higher
        /// resolution depth mode and on mesh based stabilization instead of on a faster inference rate.
        /// </summary>
        public const uint HighQualityTargetFrameRate = 20u;

        public const float DefaultStabilizationThreshold = 0.25f;

        /// <summary>
        /// Default metric depth priority offset for the route ribbon (meters).
        /// Small on purpose: it only has to beat floor depth noise (a few centimeters), while a large
        /// value would lift the ribbon off the floor and let it punch through low obstacles.
        /// </summary>
        public const float DefaultRouteDepthBiasMeters = 0.03f;

        private static readonly int ColorMaskPropId = Shader.PropertyToID("_ColorMask");
        private static readonly int DepthBiasPropId = Shader.PropertyToID("_DepthBiasMeters");

        private static readonly FieldInfo NsdkIsInitializedField =
            typeof(NsdkOcclusionExtension).GetField("_isInitialized", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NsdkOcclusionComponentField =
            typeof(NsdkOcclusionExtension).GetField("_occlusionComponent", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NsdkRequestBypassField =
            typeof(NsdkOcclusionExtension).GetField("_requestBypassOcclusionManagerUpdates", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NsdkRequestSmoothEdgesField =
            typeof(NsdkOcclusionExtension).GetField("_requestPreferSmoothEdges", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NsdkRequestStabilizationField =
            typeof(NsdkOcclusionExtension).GetField("_requestOcclusionStabilizationEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NsdkRequestSuppressionField =
            typeof(NsdkOcclusionExtension).GetField("_requestOcclusionSuppressionEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NsdkMeshManagerField =
            typeof(NsdkOcclusionExtension).GetField("_meshManager", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NsdkSuppressionChannelsField =
            typeof(NsdkOcclusionExtension).GetField("_requestedSuppressionChannels", BindingFlags.Instance | BindingFlags.NonPublic);

        [SerializeField] private ArOcclusionQualityMode requestedMode = ArOcclusionQualityMode.Balanced;
        [SerializeField] private bool preferSmoothEdges = true;
        [SerializeField] private bool semanticSuppressionEnabled = false;
        [SerializeField] private bool suppressGroundChannel = false;
        [SerializeField] private bool depthVisualizationEnabled = false;
        [SerializeField] private bool runtimeMeshDebugEnabled = false;
        [SerializeField] private float routeDepthBiasMeters = DefaultRouteDepthBiasMeters;
        [SerializeField] private bool useStabilizedOcclusionDistance = true;

        private Camera arCamera;
        private XROrigin xrOrigin;
        private AROcclusionManager occlusionManager;
        private NsdkOcclusionExtension nsdkOcclusion;
        private ARSceneSegmentationManager segmentationManager;
        private ARMeshManager runtimeMeshManager;
        private NsdkMeshingExtension nsdkMeshing;
        private GameObject runtimeMeshRootObject;
        private GameObject runtimeMeshChunkPrefab;
        private Material runtimeMeshMaterial;
        private Renderer principalOccludee;

        private readonly OcclusionDistanceStabilizer distanceStabilizer = new OcclusionDistanceStabilizer();

        private ArOcclusionQualityMode effectiveMode = ArOcclusionQualityMode.Off;
        private bool isInitialized;
        private bool isDepthSubsystemAvailable;
        private bool isMeshingSubsystemAvailable;
        private bool isSegmentationSubsystemAvailable;
        private bool isStabilizationSupportedByGraphicsApi;
        private bool? stabilizationGraphicsApiSupportOverride;
        private bool lastPrincipalOccludeeActive;
        private bool isBypassUpdatesConfigured;
        private bool isStabilizationConfigured;
        private bool hasAppliedConfiguration;

        // Telemetry state (updated without per-frame GC allocation)
        private float smoothedDeltaTime = 0.0166f;
        private float currentFps = 60f;
        private float currentFrameTimeMs = 16.6f;
        private float allocatedMemoryMb;
        private int activeRuntimeMeshCount;
        private float nextTelemetryUpdateTime;
        private string activeSubsystemSummary = "Chua khoi tao";
        private string lastWarningOrError = string.Empty;
        private string fallbackReason = string.Empty;

        private string depthModeName = "None";
        private uint depthTargetFrameRate;
        private float estimatedDepthFps;
        private Vector2Int depthTextureResolution;
        private Matrix4x4? lastDepthExtrinsics;
        private int depthFrameCounter;
        private float depthFpsWindowStart;
        private float occludeeEyeDepth;

        public ArOcclusionQualityMode RequestedMode => requestedMode;
        public ArOcclusionQualityMode EffectiveMode => effectiveMode;
        public bool IsInitialized => isInitialized;
        public bool IsOcclusionActive => effectiveMode != ArOcclusionQualityMode.Off && isDepthSubsystemAvailable;
        public bool IsDepthSubsystemAvailable => isDepthSubsystemAvailable;
        public bool IsMeshingSubsystemAvailable => isMeshingSubsystemAvailable;
        public bool IsSegmentationSubsystemAvailable => isSegmentationSubsystemAvailable;
        public bool IsStabilizationSupportedByGraphicsApi => isStabilizationSupportedByGraphicsApi;
        public bool IsBypassUpdatesConfigured => isBypassUpdatesConfigured;
        public bool IsStabilizationConfigured => isStabilizationConfigured;
        public bool IsStabilizationActive =>
            effectiveMode == ArOcclusionQualityMode.HighQuality &&
            isStabilizationConfigured &&
            nsdkOcclusion != null &&
            nsdkOcclusion.enabled;

        /// <summary>Graphics API actually used by the device, reported for diagnostics.</summary>
        public string GraphicsApiName => SystemInfo.graphicsDeviceType + " (" + SystemInfo.graphicsDeviceName + ")";

        /// <summary>Environment depth mode requested from the occlusion subsystem.</summary>
        public string DepthModeName => depthModeName;

        /// <summary>Configured depth inference rate (Hz).</summary>
        public uint DepthTargetFrameRate => depthTargetFrameRate;

        /// <summary>
        /// Estimated rate at which new depth frames arrive (Hz). This is derived from the depth
        /// prediction pose changing, so it is an estimate: a completely static camera and a static
        /// scene can report fewer frames than the configured target.
        /// </summary>
        public float EstimatedDepthFps => estimatedDepthFps;

        /// <summary>Resolution of the depth image currently used for occlusion (pixels).</summary>
        public Vector2Int DepthTextureResolution => depthTextureResolution;

        /// <summary>Current (smoothed) back projection distance used for the depth warp (meters).</summary>
        public float OccludeeEyeDepth => occludeeEyeDepth;

        /// <summary>Occlusion distance mode currently active on the NSDK extension.</summary>
        public string OcclusionDistanceModeName =>
            nsdkOcclusion == null ? "None" : nsdkOcclusion.OcclusionDistanceMode.ToString();

        /// <summary>Reason why the effective mode differs from the requested mode (empty when they match).</summary>
        public string FallbackReason => fallbackReason;

        /// <summary>
        /// Human readable occlusion diagnostics: graphics API, requested/effective mode, depth mode,
        /// estimated depth frame rate, depth resolution, occlusion reference plane and stabilization state.
        /// </summary>
        public string DiagnosticsReport
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("API ").Append(GraphicsApiName);
                sb.Append(" · ").Append(GetModeDisplayName(effectiveMode));
                sb.Append(" (yêu cầu: ").Append(GetModeDisplayName(requestedMode)).Append(')');
                sb.Append('\n');
                sb.Append("Depth ").Append(depthModeName);
                sb.Append(" @ ").Append(depthTargetFrameRate).Append("Hz");
                sb.Append(" · thực tế ~").Append(estimatedDepthFps.ToString("0")).Append("Hz");
                sb.Append(" · ").Append(depthTextureResolution.x).Append('x').Append(depthTextureResolution.y);
                sb.Append('\n');
                sb.Append("Plane ").Append(occludeeEyeDepth.ToString("0.00")).Append(" m");
                sb.Append(" · ").Append(OcclusionDistanceModeName);
                sb.Append(" · MeshStab ").Append(IsStabilizationActive ? "ON" : "OFF");
                sb.Append(" (").Append(activeRuntimeMeshCount).Append(')');
                if (!string.IsNullOrEmpty(fallbackReason))
                {
                    sb.Append('\n').Append("Fallback: ").Append(fallbackReason);
                }
                if (!string.IsNullOrEmpty(lastWarningOrError))
                {
                    sb.Append('\n').Append(lastWarningOrError);
                }
                return sb.ToString();
            }
        }

        public bool PreferSmoothEdges
        {
            get => preferSmoothEdges;
            set
            {
                if (preferSmoothEdges == value) return;
                preferSmoothEdges = value;
                ApplyCurrentConfiguration();
            }
        }

        public bool SemanticSuppressionEnabled
        {
            get => semanticSuppressionEnabled;
            set
            {
                if (semanticSuppressionEnabled == value) return;
                semanticSuppressionEnabled = value;
                ApplyCurrentConfiguration();
            }
        }

        public bool SuppressGroundChannel
        {
            get => suppressGroundChannel;
            set
            {
                if (suppressGroundChannel == value) return;
                suppressGroundChannel = value;
                ApplyCurrentConfiguration();
            }
        }

        public bool DepthVisualizationEnabled
        {
            get => depthVisualizationEnabled;
            set
            {
                if (depthVisualizationEnabled == value) return;
                depthVisualizationEnabled = value;
                ApplyDebugVisualizations();
            }
        }

        public bool RuntimeMeshDebugEnabled
        {
            get => runtimeMeshDebugEnabled;
            set
            {
                if (runtimeMeshDebugEnabled == value) return;
                runtimeMeshDebugEnabled = value;
                ApplyDebugVisualizations();
            }
        }

        public float RouteDepthBiasMeters
        {
            get => routeDepthBiasMeters;
            set
            {
                // The bias only has to beat floor depth noise. Values beyond ~15 cm make the route
                // ribbon visible through low obstacles, so the range is deliberately limited.
                routeDepthBiasMeters = Mathf.Clamp(value, 0.0f, 0.15f);
            }
        }

        /// <summary>
        /// Whether the occlusion reference distance (back projection plane) is computed by the app with a
        /// robust median based, temporally smoothed estimate. When disabled the NSDK default is used,
        /// which is the CLOSEST depth sample of the field of view.
        /// </summary>
        public bool UseStabilizedOcclusionDistance
        {
            get => useStabilizedOcclusionDistance;
            set
            {
                if (useStabilizedOcclusionDistance == value) return;
                useStabilizedOcclusionDistance = value;
                distanceStabilizer.Reset();
                occludeeEyeDepth = 0f;
            }
        }

        public float CurrentFps => currentFps;
        public float CurrentFrameTimeMs => currentFrameTimeMs;
        public float AllocatedMemoryMb => allocatedMemoryMb;
        public int ActiveRuntimeMeshCount => activeRuntimeMeshCount;
        public string ActiveSubsystemSummary => activeSubsystemSummary;
        public string LastWarningOrError => lastWarningOrError;
        public Renderer PrincipalOccludee => principalOccludee;

        /// <summary>
        /// Binds the controller to the AR Camera and XR Origin without modifying their pose or hierarchy.
        /// </summary>
        public void Initialize(Camera camera, XROrigin origin)
        {
            if (camera == null)
            {
                lastWarningOrError = "AR Camera is null; cannot initialize ArOcclusionController.";
                isInitialized = false;
                return;
            }

            arCamera = camera;
            xrOrigin = origin;
            RefreshStabilizationGraphicsApiSupport();

            EnsureCameraOcclusionComponents();
            CheckSubsystemsAvailability();
            isInitialized = true;
            ApplyCurrentConfiguration();
            RefreshTelemetryNow();
        }

        /// <summary>
        /// Overrides the auto detected graphics API support for the occlusion stabilization.
        /// The NSDK refuses to deploy the stabilization feature on Vulkan
        /// (NianticSpatial.NSDK.AR.Occlusion.OcclusionComponentDeployment, "Occlusion stabilization is
        /// not yet supported on Vulkan devices."), so the controller mirrors that check. This setter
        /// exists for diagnostics and for automated regression tests; pass null to restore auto detection.
        /// </summary>
        public void SetStabilizationGraphicsApiSupportOverride(bool? supported)
        {
            stabilizationGraphicsApiSupportOverride = supported;
            RefreshStabilizationGraphicsApiSupport();
            if (isInitialized)
            {
                ApplyCurrentConfiguration();
            }
        }

        private void RefreshStabilizationGraphicsApiSupport()
        {
            isStabilizationSupportedByGraphicsApi = stabilizationGraphicsApiSupportOverride
                ?? (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan);
        }

        /// <summary>
        /// Sets the desired occlusion quality mode and reconfigures NSDK/ARFoundation components safely.
        /// </summary>
        public void SetQualityMode(ArOcclusionQualityMode mode)
        {
            requestedMode = mode;
            if (isInitialized)
            {
                ApplyCurrentConfiguration();
                RefreshTelemetryNow();
            }
        }

        /// <summary>
        /// Registers the primary 3D world-space AR character renderer (e.g., Doraemon) as Principal Occludee
        /// so NSDK ZBuffer occlusion optimizes depth scaling around the companion character.
        /// Automatically falls back to ClosestOccluder when the character is hidden or null.
        /// </summary>
        public void SetPrincipalOccludee(Renderer targetRenderer)
        {
            principalOccludee = targetRenderer;
            UpdatePrincipalOccludeeBinding();
        }

        /// <summary>
        /// Applies the configured metric depth bias to a route line material so floor-aligned lines
        /// do not Z-fight with flat floor depth noise while still being occluded by walls, furniture, and people.
        /// </summary>
        public void ApplyRouteMaterialDepthSettings(Material routeMaterial)
        {
            if (routeMaterial == null) return;
            if (routeMaterial.HasProperty(DepthBiasPropId))
            {
                routeMaterial.SetFloat(DepthBiasPropId, routeDepthBiasMeters);
            }
        }

        /// <summary>
        /// Clears runtime occlusion stabilization meshes on relocalization, floor change, or navigation stop
        /// so stale environment meshes never cause ghost occlusions or memory growth.
        /// </summary>
        public void ClearStabilizationMeshes()
        {
            if (Application.isPlaying && runtimeMeshManager != null && runtimeMeshManager.subsystem != null)
            {
                runtimeMeshManager.DestroyAllMeshes();
            }
            activeRuntimeMeshCount = 0;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.0001f && dt < 1.0f)
            {
                smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, dt, 0.1f);
                currentFps = 1.0f / Mathf.Max(0.0001f, smoothedDeltaTime);
                currentFrameTimeMs = smoothedDeltaTime * 1000.0f;
            }

            if (!isInitialized || arCamera == null) return;

            // Keep Principal Occludee mode synchronized with whether the character is currently active
            bool currentOccludeeActive = principalOccludee != null && principalOccludee.gameObject.activeInHierarchy;
            if (currentOccludeeActive != lastPrincipalOccludeeActive)
            {
                UpdatePrincipalOccludeeBinding();
            }

            if (Time.unscaledTime >= nextTelemetryUpdateTime)
            {
                nextTelemetryUpdateTime = Time.unscaledTime + 0.5f;
                EnsureCameraOcclusionComponents();
                CheckSubsystemsAvailability();
                RefreshTelemetryNow();
                ApplyConfigurationIfResolutionChanged();
            }
        }

        /// <summary>
        /// Runs after the NSDK occlusion extension (which executes at DefaultExecutionOrder int.MinValue),
        /// so the occlusion reference distance written here is the one used by the next depth reprojection.
        /// </summary>
        private void LateUpdate()
        {
            UpdateOcclusionReferenceDistance();
        }

        private void EnsureCameraOcclusionComponents()
        {
            if (arCamera == null) return;

            // 1. Ensure AROcclusionManager is present on the AR Camera
            occlusionManager = arCamera.GetComponent<AROcclusionManager>();
            if (occlusionManager == null)
            {
                occlusionManager = arCamera.gameObject.AddComponent<AROcclusionManager>();
            }

            // Human segmentation is never used by this app: it is expensive and it would let a
            // segmented person occlude the companion. Applied unconditionally because the manager can
            // also be created implicitly by the NsdkOcclusionExtension RequireComponent attribute.
            occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
            occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;

            // 2. Ensure NsdkOcclusionExtension is present on the AR Camera.
            if (nsdkOcclusion == null)
            {
                // At runtime the extension destroys itself when the XR loader has not produced an
                // XROcclusionSubsystem yet, so it must only be created once the subsystem exists.
                // In the editor the subsystem never exists and the extension stays alive (it simply
                // waits for the XR manager), so it is created unconditionally there.
                if (Application.isPlaying && !IsOcclusionSubsystemLoaded()) return;

                nsdkOcclusion = arCamera.GetComponent<NsdkOcclusionExtension>();
                if (nsdkOcclusion == null)
                {
                    nsdkOcclusion = arCamera.gameObject.AddComponent<NsdkOcclusionExtension>();
                }
            }
        }

        private static bool IsOcclusionSubsystemLoaded()
        {
            if (XRGeneralSettings.Instance == null || XRGeneralSettings.Instance.Manager == null) return false;
            var manager = XRGeneralSettings.Instance.Manager;
            if (!manager.isInitializationComplete) return false;
            return manager.activeLoader != null &&
                   manager.activeLoader.GetLoadedSubsystem<XROcclusionSubsystem>() != null;
        }

        private void CheckSubsystemsAvailability()
        {
            if (occlusionManager != null && occlusionManager.subsystem != null)
            {
                isDepthSubsystemAvailable = true;
            }
            else
            {
                isDepthSubsystemAvailable = IsOcclusionSubsystemLoaded();
            }

            if (runtimeMeshManager != null && runtimeMeshManager.subsystem != null)
            {
                isMeshingSubsystemAvailable = true;
            }
            else
            {
                var meshDescriptors = new List<XRMeshSubsystemDescriptor>();
                SubsystemManager.GetSubsystemDescriptors(meshDescriptors);
                isMeshingSubsystemAvailable = meshDescriptors.Count > 0;
            }

            var segMgr = segmentationManager != null
                ? segmentationManager
                : (arCamera != null ? arCamera.GetComponent<ARSceneSegmentationManager>() : null);
            if (segMgr != null && segMgr.subsystem != null)
            {
                isSegmentationSubsystemAvailable = true;
            }
            else
            {
                isSegmentationSubsystemAvailable = isDepthSubsystemAvailable;
            }
        }

        /// <summary>
        /// Re-resolves the requested mode and re-applies the configuration when the resolution changed
        /// (for example when the AR session became ready after the controller was initialized, or when a
        /// subsystem appeared or disappeared). Without this the effective mode was latched forever at
        /// the moment Initialize() ran, which silently disabled occlusion or downgraded HighQuality.
        /// </summary>
        private void ApplyConfigurationIfResolutionChanged()
        {
            if (!isInitialized) return;

            ArOcclusionQualityMode resolved = ResolveEffectiveMode(requestedMode);
            if (resolved == effectiveMode && hasAppliedConfiguration) return;
            ApplyCurrentConfiguration();
        }

        private void ApplyCurrentConfiguration()
        {
            if (arCamera == null || occlusionManager == null)
            {
                effectiveMode = ArOcclusionQualityMode.Off;
                hasAppliedConfiguration = false;
                distanceStabilizer.Reset();
                return;
            }

            effectiveMode = ResolveEffectiveMode(requestedMode);
            hasAppliedConfiguration = true;

            if (effectiveMode == ArOcclusionQualityMode.Off)
            {
                DisableAllOcclusion();
                return;
            }

            if (effectiveMode == ArOcclusionQualityMode.Fallback)
            {
                ConfigureFallbackMode();
                ApplyDebugVisualizations();
                return;
            }

            // Balanced or HighQuality mode using NsdkOcclusionExtension
            ConfigureNsdkOcclusionMode(effectiveMode);
            ApplyDebugVisualizations();
        }

        public ArOcclusionQualityMode ResolveEffectiveMode(ArOcclusionQualityMode desired)
        {
            fallbackReason = string.Empty;

            if (desired == ArOcclusionQualityMode.Off)
            {
                return ArOcclusionQualityMode.Off;
            }

            if (Application.isPlaying)
            {
                if (!isDepthSubsystemAvailable)
                {
                    // The AR session may not have produced its subsystems yet. Keep the requested mode
                    // pending and re-evaluate it later instead of latching Off for the whole session.
                    fallbackReason = "Depth subsystem chua san sang (AR session dang khoi dong)";
                    return desired;
                }

                if (desired == ArOcclusionQualityMode.HighQuality)
                {
                    if (!isStabilizationSupportedByGraphicsApi)
                    {
                        fallbackReason = "Occlusion Stabilization khong ho tro Vulkan; tu dong dung Balanced.";
                        return ArOcclusionQualityMode.Balanced;
                    }
                    if (!isMeshingSubsystemAvailable)
                    {
                        fallbackReason = "XRMeshSubsystem khong kha dung; tu dong dung Balanced.";
                        return ArOcclusionQualityMode.Balanced;
                    }
                }

                return desired;
            }

            // Outside play mode (editor tooling) the subsystems are not running, so only the
            // graphics API limitation can be evaluated.
            if (desired == ArOcclusionQualityMode.HighQuality && !isStabilizationSupportedByGraphicsApi)
            {
                fallbackReason = "Occlusion Stabilization khong ho tro Vulkan; tu dong dung Balanced.";
                return ArOcclusionQualityMode.Balanced;
            }

            return desired;
        }

        private void DisableAllOcclusion()
        {
            isBypassUpdatesConfigured = false;
            isStabilizationConfigured = false;
            depthModeName = "Disabled";
            depthTargetFrameRate = 0;
            distanceStabilizer.Reset();

            if (nsdkOcclusion != null)
            {
                SetNsdkField(NsdkRequestBypassField, false);
                SetNsdkField(NsdkRequestSmoothEdgesField, false);
                SetNsdkField(NsdkRequestStabilizationField, false);
                SetNsdkField(NsdkRequestSuppressionField, false);
                if (IsNsdkInitialized())
                {
                    nsdkOcclusion.Visualization = false;
                    nsdkOcclusion.IsOcclusionStabilizationEnabled = false;
                    nsdkOcclusion.IsOcclusionSuppressionEnabled = false;
                }
                nsdkOcclusion.enabled = false;
            }

            if (occlusionManager != null)
            {
                occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
                occlusionManager.enabled = false;
            }

            DisableRuntimeMeshing();
            DisableSemanticSuppression();
        }

        private void ConfigureFallbackMode()
        {
            isBypassUpdatesConfigured = false;
            isStabilizationConfigured = false;
            depthModeName = "Fastest (AR Foundation)";
            depthTargetFrameRate = 0;
            distanceStabilizer.Reset();

            DisableRuntimeMeshing();
            DisableSemanticSuppression();

            if (nsdkOcclusion != null)
            {
                SetNsdkField(NsdkRequestBypassField, false);
                SetNsdkField(NsdkRequestStabilizationField, false);
                SetNsdkField(NsdkRequestSuppressionField, false);
                if (IsNsdkInitialized())
                {
                    nsdkOcclusion.Visualization = false;
                    nsdkOcclusion.IsOcclusionStabilizationEnabled = false;
                    nsdkOcclusion.IsOcclusionSuppressionEnabled = false;
                }
                nsdkOcclusion.enabled = false;
            }

            occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
            occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            occlusionManager.enabled = true;
        }

        private void ConfigureNsdkOcclusionMode(ArOcclusionQualityMode mode)
        {
            if (nsdkOcclusion == null)
            {
                ConfigureFallbackMode();
                return;
            }

            // Configure AROcclusionManager for Environment Depth.
            // Balanced uses Medium instead of Fastest: the Fastest depth mode is the cheapest NSDK
            // depth model and its low resolution is the main source of edge bleeding and false
            // occlusion around object silhouettes.
            EnvironmentDepthMode depthMode = mode == ArOcclusionQualityMode.HighQuality
                ? EnvironmentDepthMode.Best
                : EnvironmentDepthMode.Medium;
            occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
            occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            occlusionManager.requestedEnvironmentDepthMode = depthMode;
            occlusionManager.enabled = true;
            depthModeName = depthMode.ToString();

            // Configure NsdkOcclusionExtension with optimal mobile settings:
            // - BypassOcclusionManagerUpdates = true eliminates redundant main/render thread sync stalls
            // - OverrideOcclusionManagerSettings = true lets NSDK own the depth pipeline cleanly
            nsdkOcclusion.OverrideOcclusionManagerSettings = true;
            depthTargetFrameRate = mode == ArOcclusionQualityMode.HighQuality
                ? HighQualityTargetFrameRate
                : BalancedTargetFrameRate;
            nsdkOcclusion.TargetFrameRate = depthTargetFrameRate;
            nsdkOcclusion.StabilizationThreshold = DefaultStabilizationThreshold;

            isBypassUpdatesConfigured = true;
            SetNsdkField(NsdkRequestBypassField, true);
            SetNsdkField(NsdkRequestSmoothEdgesField, preferSmoothEdges);
            if (IsNsdkInitialized())
            {
                nsdkOcclusion.BypassOcclusionManagerUpdates = true;
                nsdkOcclusion.PreferSmoothEdges = preferSmoothEdges;
            }

            if (!nsdkOcclusion.enabled)
            {
                nsdkOcclusion.enabled = true;
            }

            // Principal Occludee vs ClosestOccluder
            UpdatePrincipalOccludeeBinding();

            // Configure Semantic Occlusion Suppression (Sky + optional Ground)
            if (semanticSuppressionEnabled)
            {
                EnableSemanticSuppression();
            }
            else
            {
                DisableSemanticSuppression();
            }

            // Configure Mesh-Blended Occlusion Stabilization in HighQuality mode
            if (mode == ArOcclusionQualityMode.HighQuality && isStabilizationSupportedByGraphicsApi)
            {
                EnableRuntimeMeshingForStabilization();
            }
            else
            {
                DisableRuntimeMeshing();
            }
        }

        private void UpdatePrincipalOccludeeBinding()
        {
            if (nsdkOcclusion == null || !nsdkOcclusion.enabled)
            {
                lastPrincipalOccludeeActive = false;
                return;
            }

            bool active = principalOccludee != null && principalOccludee.gameObject.activeInHierarchy;
            lastPrincipalOccludeeActive = active;

            if (active)
            {
                nsdkOcclusion.TrackOccludee(principalOccludee);
            }
            else
            {
                nsdkOcclusion.OcclusionDistanceMode =
                    NsdkOcclusionExtension.OptimalOcclusionDistanceMode.ClosestOccluder;
            }
        }

        private void EnableSemanticSuppression()
        {
            if (arCamera == null || nsdkOcclusion == null) return;

            segmentationManager = arCamera.GetComponent<ARSceneSegmentationManager>();
            if (segmentationManager == null)
            {
                segmentationManager = arCamera.gameObject.AddComponent<ARSceneSegmentationManager>();
            }
            segmentationManager.TargetFrameRate = (uint)BalancedTargetFrameRate;
            segmentationManager.enabled = true;

            var channels = new List<SceneSegmentationChannel> { SceneSegmentationChannel.Sky };
            if (suppressGroundChannel)
            {
                channels.Add(SceneSegmentationChannel.Ground);
                channels.Add(SceneSegmentationChannel.ArtificialGround);
            }
            SetNsdkField(NsdkSuppressionChannelsField, channels);
            SetNsdkField(NsdkRequestSuppressionField, true);

            if (IsNsdkInitialized())
            {
                nsdkOcclusion.IsOcclusionSuppressionEnabled = true;
                nsdkOcclusion.AddSceneSegmentationSuppressionChannel(SceneSegmentationChannel.Sky);
                if (suppressGroundChannel)
                {
                    nsdkOcclusion.AddSceneSegmentationSuppressionChannel(SceneSegmentationChannel.Ground);
                    nsdkOcclusion.AddSceneSegmentationSuppressionChannel(SceneSegmentationChannel.ArtificialGround);
                }
                else
                {
                    nsdkOcclusion.RemoveSceneSegmentationSuppressionChannel(SceneSegmentationChannel.Ground);
                    nsdkOcclusion.RemoveSceneSegmentationSuppressionChannel(SceneSegmentationChannel.ArtificialGround);
                }
            }
        }

        private void DisableSemanticSuppression()
        {
            if (nsdkOcclusion != null)
            {
                SetNsdkField(NsdkRequestSuppressionField, false);
                // Clear the requested channel list as well, otherwise the channels accumulate on the
                // SDK side every time the feature is toggled.
                SetNsdkField(NsdkSuppressionChannelsField, new List<SceneSegmentationChannel>());
                if (IsNsdkInitialized())
                {
                    nsdkOcclusion.IsOcclusionSuppressionEnabled = false;
                    nsdkOcclusion.RemoveSceneSegmentationSuppressionChannel(SceneSegmentationChannel.Sky);
                    nsdkOcclusion.RemoveSceneSegmentationSuppressionChannel(SceneSegmentationChannel.Ground);
                    nsdkOcclusion.RemoveSceneSegmentationSuppressionChannel(SceneSegmentationChannel.ArtificialGround);
                }
            }
            if (segmentationManager != null)
            {
                segmentationManager.enabled = false;
            }
        }

        private void EnableRuntimeMeshingForStabilization()
        {
            if (xrOrigin == null || arCamera == null || nsdkOcclusion == null) return;

            EnsureRuntimeMeshManagerAndPrefab();
            if (runtimeMeshManager == null || runtimeMeshChunkPrefab == null)
            {
                isStabilizationConfigured = false;
                SetNsdkField(NsdkRequestStabilizationField, false);
                return;
            }

            runtimeMeshManager.enabled = true;
            if (nsdkMeshing != null)
            {
                nsdkMeshing.enabled = true;
            }

            isStabilizationConfigured = true;
            SetNsdkField(NsdkMeshManagerField, runtimeMeshManager);
            SetNsdkField(NsdkRequestStabilizationField, true);
            nsdkOcclusion.StabilizationThreshold = DefaultStabilizationThreshold;
            if (IsNsdkInitialized())
            {
                nsdkOcclusion.IsOcclusionStabilizationEnabled = true;
            }
        }

        private void DisableRuntimeMeshing()
        {
            isStabilizationConfigured = false;
            if (nsdkOcclusion != null)
            {
                SetNsdkField(NsdkRequestStabilizationField, false);
                if (IsNsdkInitialized())
                {
                    nsdkOcclusion.IsOcclusionStabilizationEnabled = false;
                }
            }
            if (nsdkMeshing != null)
            {
                nsdkMeshing.enabled = false;
            }
            if (runtimeMeshManager != null)
            {
                if (Application.isPlaying)
                {
                    runtimeMeshManager.DestroyAllMeshes();
                }
                runtimeMeshManager.enabled = false;
            }
            activeRuntimeMeshCount = 0;
        }

        private void EnsureRuntimeMeshManagerAndPrefab()
        {
            if (xrOrigin == null) return;

            // Create hidden runtime mesh chunk prefab on dedicated RuntimeOcclusionMeshLayer (Layer 8).
            // Kept strictly separate from LocationMeshManager (downloaded VPS Site Mesh).
            if (runtimeMeshChunkPrefab == null)
            {
                Shader meshShader = Shader.Find("Vps/AROcclusionStabilizationMesh");
                if (meshShader == null) meshShader = Shader.Find("Nsdk/OcclusionMesh");
                if (meshShader == null) meshShader = Shader.Find("Standard");

                runtimeMeshMaterial = new Material(meshShader)
                {
                    name = "RuntimeOcclusionStabilizationMeshMat"
                };
                if (runtimeMeshMaterial.HasProperty(ColorMaskPropId))
                {
                    runtimeMeshMaterial.SetFloat(ColorMaskPropId, runtimeMeshDebugEnabled ? 15f : 0f);
                }

                runtimeMeshChunkPrefab = new GameObject("NsdkRuntimeOcclusionMeshChunk");
                runtimeMeshChunkPrefab.layer = RuntimeOcclusionMeshLayer;
                runtimeMeshChunkPrefab.hideFlags = HideFlags.HideAndDontSave;
                runtimeMeshChunkPrefab.SetActive(false);

                runtimeMeshChunkPrefab.AddComponent<MeshFilter>();
                var meshRenderer = runtimeMeshChunkPrefab.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = runtimeMeshMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.On;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            if (runtimeMeshRootObject == null)
            {
                // ARMeshManager must be a direct child of XROrigin so meshes are generated in tracking space
                Transform existing = xrOrigin.transform.Find("Runtime Occlusion Stabilization Meshing");
                if (existing != null)
                {
                    runtimeMeshRootObject = existing.gameObject;
                }
                else
                {
                    runtimeMeshRootObject = new GameObject("Runtime Occlusion Stabilization Meshing");
                    runtimeMeshRootObject.layer = RuntimeOcclusionMeshLayer;
                    runtimeMeshRootObject.transform.SetParent(xrOrigin.transform, false);
                }
            }

            runtimeMeshManager = runtimeMeshRootObject.GetComponent<ARMeshManager>();
            if (runtimeMeshManager == null)
            {
                runtimeMeshManager = runtimeMeshRootObject.AddComponent<ARMeshManager>();
            }
            runtimeMeshManager.meshPrefab = runtimeMeshChunkPrefab.GetComponent<MeshFilter>();
            runtimeMeshManager.density = 0.5f;
            runtimeMeshManager.normals = true;
            runtimeMeshManager.tangents = false;
            runtimeMeshManager.textureCoordinates = false;
            runtimeMeshManager.colors = false;
            runtimeMeshManager.concurrentQueueSize = 4;

            nsdkMeshing = runtimeMeshRootObject.GetComponent<NsdkMeshingExtension>();
            if (nsdkMeshing == null)
            {
                nsdkMeshing = runtimeMeshRootObject.AddComponent<NsdkMeshingExtension>();
            }
            // Optimize meshing configuration for low mobile CPU/GPU overhead
            nsdkMeshing.TargetFrameRate = 10;
            nsdkMeshing.FuseKeyframesOnly = true;
            nsdkMeshing.MaximumIntegrationDistance = 4.5f;
            nsdkMeshing.VoxelSize = 0.04f;
            nsdkMeshing.EnableDistanceBasedVolumetricCleanup = true;
            nsdkMeshing.MeshBlockSize = 1.6f;
            nsdkMeshing.MeshCullingDistance = 8.0f;
            nsdkMeshing.EnableMeshDecimation = true;
        }

        private void ApplyDebugVisualizations()
        {
            if (nsdkOcclusion != null && IsNsdkInitialized())
            {
                bool canVisualizeDepth =
                    depthVisualizationEnabled &&
                    nsdkOcclusion.enabled &&
                    (effectiveMode == ArOcclusionQualityMode.Balanced || effectiveMode == ArOcclusionQualityMode.HighQuality);
                nsdkOcclusion.Visualization = canVisualizeDepth;
            }

            if (runtimeMeshMaterial != null && runtimeMeshMaterial.HasProperty(ColorMaskPropId))
            {
                runtimeMeshMaterial.SetFloat(ColorMaskPropId, runtimeMeshDebugEnabled ? 15f : 0f);
            }

            if (arCamera != null)
            {
                int layerBit = 1 << RuntimeOcclusionMeshLayer;
                if (runtimeMeshDebugEnabled)
                {
                    arCamera.cullingMask |= layerBit;
                }
                else
                {
                    arCamera.cullingMask &= ~layerBit;
                }
            }
        }

        /// <summary>
        /// Computes a robust occlusion reference distance and writes it into the NSDK's back projection
        /// plane. The NSDK estimates that distance as the CLOSEST depth sample in the field of view,
        /// which collapses towards the camera whenever a near object enters the frame and misaligns
        /// the whole depth buffer. This replaces it with a median based, temporally smoothed estimate.
        /// </summary>
        private void UpdateOcclusionReferenceDistance()
        {
            if (!isInitialized || nsdkOcclusion == null || !nsdkOcclusion.enabled)
            {
                distanceStabilizer.Reset();
                occludeeEyeDepth = 0f;
                return;
            }

            if (effectiveMode != ArOcclusionQualityMode.Balanced &&
                effectiveMode != ArOcclusionQualityMode.HighQuality)
            {
                distanceStabilizer.Reset();
                occludeeEyeDepth = 0f;
                return;
            }

            if (!useStabilizedOcclusionDistance)
            {
                // Fall back to the NSDK default (closest depth sample of the field of view).
                distanceStabilizer.Reset();
                occludeeEyeDepth = 0f;
                return;
            }

            Rect region = OcclusionDistanceStabilizer.TryGetScreenRect(principalOccludee, arCamera, out Rect occludeeRect)
                ? occludeeRect
                : DefaultSamplingRegion;

            if (!distanceStabilizer.TryEstimate(nsdkOcclusion, region, out float rawDistance))
            {
                // No usable depth this frame: keep the previous reference distance.
                return;
            }

            float smoothed = distanceStabilizer.Smooth(rawDistance, Time.unscaledDeltaTime);
            occludeeEyeDepth = smoothed;

            // Written after the NSDK extension has computed its own (minimum based) estimate for this
            // frame, so this value is the one consumed by the next depth reprojection.
            NianticSpatial.NSDK.AR.Utilities.XRDisplayContext.OccludeeEyeDepth = smoothed;
        }

        /// <summary>
        /// Central region used for the occlusion reference distance when no principal occludee is
        /// available. Matches the region the NSDK uses for its own ClosestOccluder sampling
        /// (k_FullScreenSampleBorder = 0.2).
        /// </summary>
        private static Rect DefaultSamplingRegion => new Rect(0.2f, 0.2f, 0.6f, 0.6f);

        private bool IsNsdkInitialized()
        {
            if (nsdkOcclusion == null || NsdkIsInitializedField == null || NsdkOcclusionComponentField == null) return false;
            object initVal = NsdkIsInitializedField.GetValue(nsdkOcclusion);
            object compVal = NsdkOcclusionComponentField.GetValue(nsdkOcclusion);
            return initVal is bool b && b && compVal != null;
        }

        private void SetNsdkField(FieldInfo field, object value)
        {
            if (nsdkOcclusion != null && field != null)
            {
                field.SetValue(nsdkOcclusion, value);
            }
        }

        private void RefreshTelemetryNow()
        {
            allocatedMemoryMb = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
            activeRuntimeMeshCount =
                Application.isPlaying &&
                runtimeMeshManager != null &&
                runtimeMeshManager.subsystem != null &&
                runtimeMeshManager.meshes != null
                    ? runtimeMeshManager.meshes.Count
                    : 0;

            UpdateDepthTelemetry();

            string modeLabel = GetModeDisplayName(effectiveMode);
            string subName = occlusionManager != null && occlusionManager.subsystem != null
                ? occlusionManager.subsystem.subsystemDescriptor.id
                : (isDepthSubsystemAvailable ? "Depth Ready" : "No Depth Subsystem");
            string occludeeState = lastPrincipalOccludeeActive ? "Occludee: Doraemon" : "Occludee: Closest";
            string stabState = IsStabilizationActive ? $"MeshStab: ON ({activeRuntimeMeshCount})" : "MeshStab: OFF";

            activeSubsystemSummary =
                $"{modeLabel} | {subName} | {occludeeState} | {stabState} | Depth {depthModeName} @ {depthTargetFrameRate}Hz" +
                $" | Res {depthTextureResolution.x}x{depthTextureResolution.y} | Plane {occludeeEyeDepth:0.00}m";
        }

        /// <summary>
        /// Samples the live depth image to report its resolution and to estimate how often new depth
        /// frames actually arrive.
        /// </summary>
        private void UpdateDepthTelemetry()
        {
            if (nsdkOcclusion == null || !nsdkOcclusion.enabled)
            {
                depthTextureResolution = Vector2Int.zero;
                return;
            }

            Texture2D depthTexture = nsdkOcclusion.DepthTexture;
            if (depthTexture != null)
            {
                depthTextureResolution = new Vector2Int(depthTexture.width, depthTexture.height);
            }

            Matrix4x4? extrinsics = nsdkOcclusion.LatestExtrinsicsMatrix;
            if (extrinsics.HasValue && (!lastDepthExtrinsics.HasValue || extrinsics.Value != lastDepthExtrinsics.Value))
            {
                lastDepthExtrinsics = extrinsics;
                depthFrameCounter++;
            }

            float now = Time.unscaledTime;
            if (depthFpsWindowStart <= 0f)
            {
                depthFpsWindowStart = now;
            }
            else if (now - depthFpsWindowStart >= 1f)
            {
                estimatedDepthFps = depthFrameCounter / (now - depthFpsWindowStart);
                depthFrameCounter = 0;
                depthFpsWindowStart = now;
            }
        }

        public static string GetModeDisplayName(ArOcclusionQualityMode mode)
        {
            switch (mode)
            {
                case ArOcclusionQualityMode.Off:
                    return "Tat (Off)";
                case ArOcclusionQualityMode.Fallback:
                    return "Co ban (Instant)";
                case ArOcclusionQualityMode.Balanced:
                    return "Can bang (NSDK)";
                case ArOcclusionQualityMode.HighQuality:
                    return "Cao cap (Mesh+Depth)";
                default:
                    return mode.ToString();
            }
        }

        private void OnDestroy()
        {
            ClearStabilizationMeshes();
            if (runtimeMeshMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeMeshMaterial);
                else DestroyImmediate(runtimeMeshMaterial);
                runtimeMeshMaterial = null;
            }
            if (runtimeMeshChunkPrefab != null)
            {
                if (Application.isPlaying) Destroy(runtimeMeshChunkPrefab);
                else DestroyImmediate(runtimeMeshChunkPrefab);
                runtimeMeshChunkPrefab = null;
            }
        }
    }
}
