using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Occlusion;
using NianticSpatial.NSDK.AR.Sites;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.XRSubsystems;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UIElements;
using Vps.UnityClient.Api;
using Vps.UnityClient.Diagnostics;
using Vps.UnityClient.Authoring;
using Vps.UnityClient.Bootstrap;
using Vps.UnityClient.Vps;

namespace Vps.UnityClient.Navigation
{
    /// <summary>Runs explicit, published Site Graph V2 navigation on the matching Niantic VPS2 anchor.</summary>
    public sealed class SiteGraphV2NavigationExperience : MonoBehaviour
    {
        public static bool IsAnyNavigating { get; private set; }

        private const string NsdkVersion = "4.1.0-26051913";
        private const double StableTrackingSeconds = 0.5d;
        private const double RouteRefreshSeconds = 0.35d;
        private const float MaximumSnapDistanceM = 2.5f;
        private const float DefaultRouteHue = 0.52f;
        /// <summary>Legacy route depth bias that is migrated to the new safe default on load.</summary>
        private const float LegacyRouteDepthBiasMeters = 0.18f;
        private const string SettingsPrefix = "VPS.Navigation.Settings.";

        private static readonly Color[] RouteColorOptions =
        {
            new Color(0.00f, 0.82f, 0.98f, 1f),
            new Color(0.15f, 0.40f, 1.00f, 1f),
            new Color(0.12f, 0.84f, 0.48f, 1f),
            new Color(1.00f, 0.68f, 0.12f, 1f),
            new Color(1.00f, 0.28f, 0.58f, 1f),
            Color.white
        };

        private static readonly string[] RouteColorNames =
        {
            "Xanh cyan", "Xanh dương", "Xanh lá", "Vàng", "Hồng", "Trắng"
        };

        private enum OcclusionToggle
        {
            SmoothEdges,
            StableOcclusionDistance,
            SemanticSuppression,
            SuppressGround,
            DepthVisualization,
            MeshDebug
        }

        [SerializeField] private NavigationBootstrapController bootstrap = null;
        [SerializeField] private NavigationApiClient apiClient = null;
        private readonly List<PublishedGraphPoiV2> activePois = new List<PublishedGraphPoiV2>();
        private readonly List<PublishedGraphPoiV2> visiblePois = new List<PublishedGraphPoiV2>();
        private string status = "Tải tuyến đã publish rồi nhấn Bắt đầu dẫn đường.";
        private string selectedPoiId = "";
        private string activeRoutedPoiId = null;
        private bool panelOpen;
        private bool settingsOpen;
        private bool startPending;
        private bool trackingRequestAccepted;
        private bool stableTracking;
        private string lastDiagnosticsTrackingState = string.Empty;
        private bool bindingVerified;
        private bool sitesBusy;
        private double trackedSince;
        private double lastRouteUpdate;
        private Camera arCamera;
        private ARSession arSession;
        private ARInputManager arInputManager;
        private XROrigin xrOrigin;
        private ARVps2Manager vps2Manager;
        private NsdkVpsLocalizationAdapter localizationAdapter;
        private SitesClientManager sitesClient;
        private Transform trackedAnchor;
        private LineRenderer routeLine;
        private Material ownedMaterial;
        private DoraemonCompanionGuide companionGuide;
        private ArOcclusionController occlusionController;
        private GoogleTextToSpeechService ttsService;
        private string lastAutoSpokenArrivalKey;
        private Vector3[] currentRoutePoints = Array.Empty<Vector3>();
        private bool lineFlatOnGround = true;
        private float routeLineWidth = 0.22f;
        private float routeLineOpacity = 0.95f;
        private Color routeLineColor = RouteColorOptions[0];
        private int routeColorIndex;
        private float routeHue = DefaultRouteHue;
        private Rect safeUiRect;
        private float uiScale = 1f;
        private Vector2 settingsScrollPosition;
        private string poiSearchQuery = string.Empty;
        private NianticAccessTokenProvider nianticAccessTokenProvider;

        // Offline AR authoring state (no backend required)
        private bool authoringMode;
        private bool isRecordingRoute;
        private readonly List<Vector3> recordedFloorPoints = new List<Vector3>();
        private float walkStartCamY;
        private float walkStartFloorY;
        private bool hasWalkStartCamY;
        private Vector3 pendingPoiFloorPoint;
        private bool hasPendingPoiLocation;
        private Vector3[] pendingWalkSnapshot = Array.Empty<Vector3>();
        private Vector3 currentAuthoringFloorPoint;
        private float currentAuthoringSnapDistance = float.MaxValue;
        private readonly List<LineRenderer> authoringExistingEdgeLines = new List<LineRenderer>();
        private LineRenderer authoringWalkTrailLine;
        private Material authoringExistingMaterial;
        private Material authoringWalkMaterial;
        private const float AuthoringWalkSpacingMeters = 0.35f;

        [Header("UI Presentation")]
        [SerializeField] private bool useUiToolkit = true;
        [SerializeField] private UIDocument uiDocument;
        private NavigationUiToolkitController uiToolkitController;

        public bool UseUiToolkit
        {
            get => useUiToolkit;
            set => useUiToolkit = value;
        }

        public NavigationUiToolkitController UiToolkitController => uiToolkitController;
        public static Color[] PaletteColors => RouteColorOptions;
        public static string[] PaletteColorNames => RouteColorNames;
        public static string Capitalize(string text) => CapitalizeFirst(text);
        public static string FormatPoiCategory(string category) => GetPoiCategoryLabel(category);

        public bool IsSitesBusy => sitesBusy;

        public float RouteLineWidth
        {
            get => routeLineWidth;
            set
            {
                routeLineWidth = Mathf.Clamp(value, 0.04f, 0.5f);
                ApplyLineSettings();
                SaveUserSettings(false);
            }
        }

        public float RouteHue
        {
            get => routeHue;
            set
            {
                routeHue = Mathf.Clamp01(value);
                routeColorIndex = -1;
                routeLineColor = Color.HSVToRGB(routeHue, 0.9f, 1f);
                routeLineColor.a = routeLineOpacity;
                ApplyLineSettings();
                SaveUserSettings(false);
            }
        }

        public int RouteColorIndex
        {
            get => routeColorIndex;
            set
            {
                routeColorIndex = Mathf.Clamp(value, -1, RouteColorOptions.Length - 1);
                if (routeColorIndex >= 0)
                {
                    Color.RGBToHSV(RouteColorOptions[routeColorIndex], out routeHue, out _, out _);
                    routeLineColor = RouteColorOptions[routeColorIndex];
                    routeLineColor.a = routeLineOpacity;
                }
                ApplyLineSettings();
                SaveUserSettings(false);
            }
        }

        public float RouteLineOpacity
        {
            get => routeLineOpacity;
            set
            {
                routeLineOpacity = Mathf.Clamp(value, 0.25f, 1f);
                routeLineColor.a = routeLineOpacity;
                ApplyLineSettings();
                SaveUserSettings(false);
            }
        }

        private DoraemonCompanionGuide EnsureCompanionGuide()
        {
            if (companionGuide == null)
            {
                companionGuide = GetComponent<DoraemonCompanionGuide>();
                if (companionGuide == null) companionGuide = gameObject.AddComponent<DoraemonCompanionGuide>();
            }
            return companionGuide;
        }

        private ArOcclusionController EnsureOcclusionController()
        {
            if (occlusionController == null)
            {
                occlusionController = GetComponent<ArOcclusionController>();
                if (occlusionController == null) occlusionController = gameObject.AddComponent<ArOcclusionController>();
            }
            return occlusionController;
        }

        public float RouteDepthBias
        {
            get
            {
                var occ = EnsureOcclusionController();
                return occ != null ? occ.RouteDepthBiasMeters : 0f;
            }
            set
            {
                var occ = EnsureOcclusionController();
                if (occ != null)
                {
                    occ.RouteDepthBiasMeters = Mathf.Clamp(value, 0f, 0.35f);
                    ApplyLineSettings();
                    SaveUserSettings(false);
                }
            }
        }

        public float CompanionModelScale
        {
            get
            {
                var guide = EnsureCompanionGuide();
                return guide != null ? guide.ModelScaleMultiplier : 1f;
            }
            set
            {
                var guide = EnsureCompanionGuide();
                if (guide != null)
                {
                    guide.ModelScaleMultiplier = Mathf.Clamp(value, 0.5f, 2f);
                    SaveUserSettings(false);
                }
            }
        }

        public float CompanionBrightness
        {
            get
            {
                var guide = EnsureCompanionGuide();
                return guide != null ? guide.ModelBrightness : DoraemonCompanionGuide.DefaultModelBrightness;
            }
            set
            {
                var guide = EnsureCompanionGuide();
                if (guide != null)
                {
                    guide.ModelBrightness = Mathf.Clamp(value, 0.2f, 2f);
                    SaveUserSettings(false);
                }
            }
        }

        public float CompanionLeadDistance
        {
            get
            {
                var guide = EnsureCompanionGuide();
                return guide != null ? guide.LeadDistance : DoraemonCompanionGuide.DefaultLeadDistance;
            }
            set
            {
                var guide = EnsureCompanionGuide();
                if (guide != null)
                {
                    guide.LeadDistance = Mathf.Clamp(value, 1.2f, 3f);
                    SaveUserSettings(false);
                }
            }
        }

        public float CompanionGroundDrop
        {
            get
            {
                var guide = EnsureCompanionGuide();
                return guide != null ? guide.GroundDropMeters : DoraemonCompanionGuide.DefaultGroundDrop;
            }
            set
            {
                var guide = EnsureCompanionGuide();
                if (guide != null)
                {
                    guide.GroundDropMeters = Mathf.Clamp(value, 0f, 0.15f);
                    SaveUserSettings(false);
                }
            }
        }

        public ArOcclusionQualityMode OcclusionMode
        {
            get
            {
                var occ = EnsureOcclusionController();
                return occ != null ? occ.RequestedMode : ArOcclusionQualityMode.Off;
            }
            set
            {
                var occ = EnsureOcclusionController();
                if (occ != null)
                {
                    occ.SetQualityMode(value);
                    SaveUserSettings(false);
                }
            }
        }

        /// <summary>
        /// Enables the app side robust occlusion reference distance (median + temporal smoothing).
        /// Disabling it restores the NSDK default (closest depth sample of the field of view).
        /// </summary>
        public bool StabilizedOcclusionDistance
        {
            get
            {
                var occ = EnsureOcclusionController();
                return occ != null && occ.UseStabilizedOcclusionDistance;
            }
            set
            {
                var occ = EnsureOcclusionController();
                if (occ != null)
                {
                    occ.UseStabilizedOcclusionDistance = value;
                    SaveUserSettings(false);
                }
            }
        }

        /// <summary>Live occlusion diagnostics (graphics API, depth mode, reference plane, fallback reason).</summary>
        public string OcclusionDiagnostics
        {
            get
            {
                var occ = EnsureOcclusionController();
                return occ != null ? occ.DiagnosticsReport : string.Empty;
            }
        }

        /// <summary>Reason why the effective occlusion mode differs from the requested one.</summary>
        public string OcclusionFallbackReason
        {
            get
            {
                var occ = EnsureOcclusionController();
                return occ != null ? occ.FallbackReason : string.Empty;
            }
        }

        /// <summary>The occlusion mode that is actually active (may differ from the requested one).</summary>
        public ArOcclusionQualityMode EffectiveOcclusionMode
        {
            get
            {
                var occ = EnsureOcclusionController();
                return occ != null ? occ.EffectiveMode : ArOcclusionQualityMode.Off;
            }
        }

        public bool SmoothEdgesOcclusion
        {
            get
            {
                EnsureOcclusionController();
                return GetOcclusionToggle(OcclusionToggle.SmoothEdges);
            }
            set
            {
                EnsureOcclusionController();
                SetOcclusionToggle(OcclusionToggle.SmoothEdges, value);
            }
        }

        public bool SemanticSuppressionOcclusion
        {
            get
            {
                EnsureOcclusionController();
                return GetOcclusionToggle(OcclusionToggle.SemanticSuppression);
            }
            set
            {
                EnsureOcclusionController();
                SetOcclusionToggle(OcclusionToggle.SemanticSuppression, value);
            }
        }

        public bool SuppressGroundOcclusion
        {
            get
            {
                EnsureOcclusionController();
                return GetOcclusionToggle(OcclusionToggle.SuppressGround);
            }
            set
            {
                EnsureOcclusionController();
                SetOcclusionToggle(OcclusionToggle.SuppressGround, value);
            }
        }

        public bool DepthVisualizationOcclusion
        {
            get
            {
                EnsureOcclusionController();
                return GetOcclusionToggle(OcclusionToggle.DepthVisualization);
            }
            set
            {
                EnsureOcclusionController();
                SetOcclusionToggle(OcclusionToggle.DepthVisualization, value);
            }
        }

        private GoogleTextToSpeechService EnsureTtsService()
        {
            if (ttsService == null)
            {
                ttsService = GetComponent<GoogleTextToSpeechService>();
                if (ttsService == null) ttsService = gameObject.AddComponent<GoogleTextToSpeechService>();
            }
            return ttsService;
        }

        public GoogleTextToSpeechService TtsService => EnsureTtsService();

        public bool TtsEnabled
        {
            get => EnsureTtsService().Enabled;
            set
            {
                EnsureTtsService().Enabled = value;
                SaveUserSettings(false);
            }
        }

        public float TtsSpeakingRate
        {
            get => EnsureTtsService().SpeakingRate;
            set
            {
                EnsureTtsService().SpeakingRate = value;
                SaveUserSettings(false);
            }
        }

        public string TtsCloudApiKey
        {
            get => EnsureTtsService().CloudApiKey;
            set
            {
                EnsureTtsService().CloudApiKey = value;
                SaveUserSettings(false);
            }
        }

        public string TtsStatusSummary => EnsureTtsService().StatusSummary;
        public bool IsTtsSpeaking => ttsService != null && ttsService.IsSpeaking;

        public static string BuildArrivalSpeechText(string destinationLabel, string destinationDescription)
        {
            string cleanLabel = string.IsNullOrWhiteSpace(destinationLabel) ? string.Empty : destinationLabel.Trim();
            if (!string.IsNullOrWhiteSpace(destinationDescription))
            {
                string cleanDesc = destinationDescription.Trim();
                return string.IsNullOrEmpty(cleanLabel) ? cleanDesc : $"Đã tới {cleanLabel}. {cleanDesc}";
            }

            return string.IsNullOrEmpty(cleanLabel) ? "Bạn đã tới điểm đích!" : $"Bạn đã đến {cleanLabel}!";
        }

        public void OnReplayArrivalSpeechRequested()
        {
            PublishedGraphPoiV2 destPoi = FindActivePoi(!string.IsNullOrEmpty(activeRoutedPoiId) ? activeRoutedPoiId : selectedPoiId);
            string destLabel = destPoi != null ? CapitalizeFirst(destPoi.Label) : "Điểm đến";
            string destDesc = destPoi != null ? destPoi.Description : null;
            string speechText = BuildArrivalSpeechText(destLabel, destDesc);
            EnsureTtsService().SpeakManual(speechText);
        }

        public void OnTestTtsRequested()
        {
            EnsureTtsService().SpeakManual("Xin chào, tôi là Robot dẫn đường. Hệ thống giọng nói Google đang sẵn sàng.");
        }

        public bool MeshDebugOcclusion
        {
            get
            {
                EnsureOcclusionController();
                return GetOcclusionToggle(OcclusionToggle.MeshDebug);
            }
            set
            {
                EnsureOcclusionController();
                SetOcclusionToggle(OcclusionToggle.MeshDebug, value);
            }
        }

        public void OnBackButtonClicked()
        {
            if (trackingRequestAccepted) StopNavigation();
            if (panelOpen)
            {
                panelOpen = false;
                uiToolkitController?.CloseDestinationPickerModal();
            }
        }

        public void OnOpenDestinationPickerClicked()
        {
            bool ready = bootstrap != null && bootstrap.State == NavigationBootstrapState.ManifestLoaded && activePois.Count > 0;
            if (ready)
            {
                panelOpen = true;
                poiSearchQuery = string.Empty;
                RefreshPois();
                uiToolkitController?.OpenDestinationPickerModal();
                uiToolkitController?.PopulatePois(visiblePois, selectedPoiId);
            }
            else if (bootstrap != null)
            {
                bootstrap.Reload();
            }
        }

        public void OnSearchQueryChanged(string query)
        {
            poiSearchQuery = query ?? string.Empty;
            RebuildVisiblePois();
            uiToolkitController?.PopulatePois(visiblePois, selectedPoiId);
        }

        public void OnPoiSelected(string poiId)
        {
            if (!string.Equals(selectedPoiId, poiId, StringComparison.Ordinal))
            {
                lastAutoSpokenArrivalKey = null;
            }
            selectedPoiId = poiId;
            VpsSystemDiagnostics.Record("INPUT", "destination row selected: " + poiId);
            uiToolkitController?.PopulatePois(visiblePois, selectedPoiId);
        }

        public void OnStartNavigationRequested()
        {
            VpsSystemDiagnostics.Record("INPUT", "button callback: Start navigation (UI Toolkit)");
            authoringMode = false;
            isRecordingRoute = false;
            recordedFloorPoints.Clear();
            panelOpen = false;
            lastAutoSpokenArrivalKey = null;
            ttsService?.Stop();
            companionGuide?.ResetNavigationState();
            uiToolkitController?.CloseDestinationPickerModal();
            StartCoroutine(RefreshAndTrack());
        }

        public void OnStopNavigationRequested()
        {
            StopNavigation();
        }

        public void OnResetSettingsRequested()
        {
            ResetUserSettings();
        }

        public void OnDestinationPickerClosed()
        {
            panelOpen = false;
        }

        public void OnSettingsClosed()
        {
            settingsOpen = false;
            SaveUserSettings(true);
        }

        public void OnOpenMapEditorClicked()
        {
            panelOpen = false;
            uiToolkitController?.CloseDestinationPickerModal();
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            RefreshPois();
            uiToolkitController?.OpenMapEditorModal(activePois, bootstrap != null && bootstrap.IsUsingLocalOverride);
        }

        public void OnMapEditorClosed()
        {
        }

        public void OnStartArAuthoringRequested()
        {
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            if (trackingRequestAccepted) StopNavigation();
            panelOpen = false;
            authoringMode = true;
            isRecordingRoute = false;
            recordedFloorPoints.Clear();
            hasWalkStartCamY = false;
            hasPendingPoiLocation = false;
            pendingWalkSnapshot = Array.Empty<Vector3>();
            status = "Đưa camera quanh phòng để định vị trước khi thêm điểm đến.";
            StartCoroutine(RefreshAndTrack());
        }

        public void OnStopArAuthoringRequested()
        {
            StopNavigation();
        }

        public void OnToggleAuthoringWalkRequested()
        {
            if (!authoringMode || !stableTracking || trackedAnchor == null || arCamera == null) return;
            if (isRecordingRoute)
            {
                isRecordingRoute = false;
                recordedFloorPoints.Clear();
                hasWalkStartCamY = false;
                status = "Đã hủy đoạn ghi đường. Bạn có thể ghi lại hoặc đặt điểm tại đây.";
                UpdateAuthoringPreviewRenderers();
                return;
            }

            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            Vector3 localCam = trackedAnchor.InverseTransformPoint(arCamera.transform.position);
            walkStartCamY = localCam.y;
            walkStartFloorY = LocalSiteGraphStore.EstimateFloorHeight(bootstrap != null ? bootstrap.Manifest : null, localCam);
            hasWalkStartCamY = true;
            isRecordingRoute = true;
            recordedFloorPoints.Clear();

            if (TryGetCurrentAuthoringFloorPoint(out Vector3 startFloor))
            {
                recordedFloorPoints.Add(startFloor);
            }
            status = "Đang ghi lộ trình: hãy đi bộ tới vị trí điểm đến mới.";
            UpdateAuthoringPreviewRenderers();
        }

        public void OnOpenMarkPoiFormRequested()
        {
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            hasPendingPoiLocation = TryGetCurrentAuthoringFloorPoint(out pendingPoiFloorPoint);
            pendingWalkSnapshot = isRecordingRoute ? recordedFloorPoints.ToArray() : Array.Empty<Vector3>();
            string summary = BuildAuthoringConnectionSummary();
            uiToolkitController?.OpenPoiFormForNew(summary);
        }

        public bool OnSaveNewPoiRequested(string label, string category, out string error)
        {
            return OnSaveNewPoiRequested(label, category, null, out error);
        }

        public bool OnSaveNewPoiRequested(string label, string category, string description, out string error)
        {
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            if (bootstrap == null || bootstrap.Manifest == null)
            {
                error = "Bản đồ chưa được tải.";
                return false;
            }

            Vector3 targetPoint;
            if (hasPendingPoiLocation)
            {
                targetPoint = pendingPoiFloorPoint;
            }
            else if (!TryGetCurrentAuthoringFloorPoint(out targetPoint))
            {
                error = "Chưa định vị xong không gian AR để lấy tọa độ điểm đến.";
                return false;
            }

            if (!LocalSiteGraphStore.AddPoiWithRoute(
                    bootstrap.Manifest,
                    label,
                    category,
                    description,
                    targetPoint,
                    pendingWalkSnapshot,
                    out PublishedSiteGraphManifest updated,
                    out PublishedGraphPoiV2 addedPoi,
                    out error))
            {
                return false;
            }

            if (!bootstrap.ApplyAndSaveLocalManifest(updated, out error))
            {
                return false;
            }

            isRecordingRoute = false;
            recordedFloorPoints.Clear();
            hasWalkStartCamY = false;
            hasPendingPoiLocation = false;
            pendingWalkSnapshot = Array.Empty<Vector3>();
            selectedPoiId = addedPoi.Id;
            RefreshPois();
            UpdateAuthoringPreviewRenderers();
            status = $"Đã lưu điểm đến '{CapitalizeFirst(addedPoi.Label)}'. Có thể thêm tiếp hoặc bấm Hoàn tất.";
            return true;
        }

        public bool OnUpdateExistingPoiRequested(string poiId, string label, string category, out string error)
        {
            return OnUpdateExistingPoiRequestedInternal(poiId, label, category, null, false, out error);
        }

        public bool OnUpdateExistingPoiRequested(string poiId, string label, string category, string description, out string error)
        {
            return OnUpdateExistingPoiRequestedInternal(poiId, label, category, description, true, out error);
        }

        private bool OnUpdateExistingPoiRequestedInternal(
            string poiId,
            string label,
            string category,
            string description,
            bool updateDescription,
            out string error)
        {
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            if (bootstrap == null || bootstrap.Manifest == null)
            {
                error = "Bản đồ chưa được tải.";
                return false;
            }

            bool ok = updateDescription
                ? LocalSiteGraphStore.UpdatePoi(bootstrap.Manifest, poiId, label, category, description, out PublishedSiteGraphManifest updated, out error)
                : LocalSiteGraphStore.UpdatePoi(bootstrap.Manifest, poiId, label, category, out updated, out error);

            if (!ok)
            {
                return false;
            }

            if (!bootstrap.ApplyAndSaveLocalManifest(updated, out error))
            {
                return false;
            }

            RefreshPois();
            uiToolkitController?.RefreshMapEditorList(
                activePois,
                bootstrap.IsUsingLocalOverride,
                $"Đã cập nhật điểm đến '{CapitalizeFirst(label.Trim())}'.",
                false);
            return true;
        }

        public void OnDeleteExistingPoiRequested(string poiId)
        {
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            if (bootstrap == null || bootstrap.Manifest == null) return;

            if (!LocalSiteGraphStore.DeletePoi(bootstrap.Manifest, poiId, out PublishedSiteGraphManifest updated, out string error))
            {
                uiToolkitController?.ShowMapEditorFeedback(error, true);
                return;
            }

            if (!bootstrap.ApplyAndSaveLocalManifest(updated, out error))
            {
                uiToolkitController?.ShowMapEditorFeedback(error, true);
                return;
            }

            RefreshPois();
            uiToolkitController?.RefreshMapEditorList(
                activePois,
                bootstrap.IsUsingLocalOverride,
                "Đã xóa điểm đến khỏi bản đồ trên máy.",
                false);
        }

        public void OnExportManifestJsonRequested()
        {
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            if (bootstrap == null || bootstrap.Manifest == null)
            {
                uiToolkitController?.ShowMapEditorFeedback("Chưa có dữ liệu bản đồ để sao chép.", true);
                return;
            }

            string json = LocalSiteGraphStore.SerializeManifestJson(bootstrap.Manifest, true);
            GUIUtility.systemCopyBuffer = json;
            uiToolkitController?.ShowMapEditorFeedback(
                $"Đã sao chép JSON ({activePois.Count} điểm đến) vào bộ nhớ tạm.",
                false);
        }

        public void OnResetLocalManifestRequested()
        {
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            if (bootstrap == null) return;

            bootstrap.ResetLocalManifestToDefault();
            RefreshPois();
            uiToolkitController?.RefreshMapEditorList(
                activePois,
                bootstrap.IsUsingLocalOverride,
                "Đã khôi phục bản đồ mặc định ban đầu.",
                false);
        }

        private string BuildDiagnosticsSummary()
        {
            if (occlusionController == null) return "Depth subsystem chưa kết nối";
            return string.Format(CultureInfo.InvariantCulture,
                "{0:0} FPS · {1:0} ms · {2:0} MB\nDepth: {3} ({4})\n{5}",
                occlusionController.CurrentFps,
                occlusionController.CurrentFrameTimeMs,
                occlusionController.AllocatedMemoryMb,
                occlusionController.IsDepthSubsystemAvailable ? "Sẵn sàng" : "Chưa khả dụng",
                GetQualityModeLabel(occlusionController.EffectiveMode),
                occlusionController.DiagnosticsReport);
        }

        private void Start()
        {
            InitializeUiToolkit();
        }

        public void InitializeUiToolkit()
        {
            if (!useUiToolkit) return;
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) uiDocument = gameObject.AddComponent<UIDocument>();

            if (uiDocument.panelSettings == null)
            {
                var loadedSettings = Resources.Load<PanelSettings>("UI/NavigationPanelSettings");
                if (loadedSettings != null)
                {
                    uiDocument.panelSettings = loadedSettings;
                }
                else
                {
                    var settings = ScriptableObject.CreateInstance<PanelSettings>();
                    settings.name = "RuntimeNavigationPanelSettings";
                    settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                    settings.referenceResolution = new Vector2Int(390, 844);
                    settings.match = 0.5f;
                    settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                    uiDocument.panelSettings = settings;
                }
            }

            if (uiDocument.visualTreeAsset == null)
            {
                uiDocument.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/NavigationUI");
            }

            if (uiDocument.visualTreeAsset != null && uiToolkitController == null)
            {
                uiToolkitController = new NavigationUiToolkitController(this);
                if (uiToolkitController.Initialize(uiDocument))
                {
                    uiToolkitController.PopulatePois(visiblePois, selectedPoiId);
                    uiToolkitController.SyncSettingsUi();
                }
            }
        }

        public void OnRetryBootstrapRequested()
        {
            bootstrap?.Reload();
        }

        private void Awake()
        {
            Debug.developerConsoleVisible = false;
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            if (apiClient == null) apiClient = GetComponent<NavigationApiClient>();
            nianticAccessTokenProvider = GetComponent<NianticAccessTokenProvider>();
            if (nianticAccessTokenProvider == null) nianticAccessTokenProvider = gameObject.AddComponent<NianticAccessTokenProvider>();
            nianticAccessTokenProvider.Initialize(apiClient);
            sitesClient = GetComponent<SitesClientManager>();
            if (sitesClient == null) sitesClient = gameObject.AddComponent<SitesClientManager>();
            sitesClient.RequestTimeoutSeconds = 20;
            companionGuide = GetComponent<DoraemonCompanionGuide>();
            if (companionGuide == null) companionGuide = gameObject.AddComponent<DoraemonCompanionGuide>();
            occlusionController = GetComponent<ArOcclusionController>();
            if (occlusionController == null) occlusionController = gameObject.AddComponent<ArOcclusionController>();
            ttsService = GetComponent<GoogleTextToSpeechService>();
            if (ttsService == null) ttsService = gameObject.AddComponent<GoogleTextToSpeechService>();
            LoadUserSettings();
            ttsService.Prewarm();
            RefreshPois();
        }

        private void OnEnable()
        {
            if (bootstrap != null) bootstrap.StateChanged += HandleBootstrapState;
        }

        private void OnDisable()
        {
            if (bootstrap != null) bootstrap.StateChanged -= HandleBootstrapState;
            startPending = false;
            StopNavigation();
            SaveUserSettings(true);
            IsAnyNavigating = false;
        }

        private void OnApplicationQuit()
        {
            SaveUserSettings(true);
        }

        private void Update()
        {
            bool editorModalOpen = uiToolkitController != null && (uiToolkitController.IsMapEditorOpen || uiToolkitController.IsPoiFormOpen);
            IsAnyNavigating = trackingRequestAccepted || panelOpen || settingsOpen || editorModalOpen;
            VpsSystemDiagnostics.SetState("Navigation UI", "destinationPicker=" + panelOpen + " settings=" + settingsOpen +
                " authoring=" + authoringMode + " busy=" + sitesBusy + " startPending=" + startPending +
                " trackingAccepted=" + trackingRequestAccepted + " stable=" + stableTracking);
            string trackingState = localizationAdapter == null ? "adapter missing" : localizationAdapter.CurrentTrackingState.ToString();
            if (!string.Equals(lastDiagnosticsTrackingState, trackingState, StringComparison.Ordinal))
            {
                lastDiagnosticsTrackingState = trackingState;
                VpsSystemDiagnostics.SetState("Navigation VPS", trackingState);
            }
            if (!trackingRequestAccepted) return;
            bool tracked = localizationAdapter != null &&
                localizationAdapter.CurrentTrackingState == NormalizedVpsTrackingState.TRACKED && trackedAnchor != null;
            if (!tracked)
            {
                if (stableTracking && occlusionController != null)
                {
                    occlusionController.ClearStabilizationMeshes();
                }
                trackedSince = 0d;
                stableTracking = false;
                SetRouteVisible(false);
                SetAuthoringPreviewVisible(false);
                status = "VPS2 Tracking bị gián đoạn; tuyến đã ẩn. Hãy lia camera quanh phòng để nhận diện lại.";
                return;
            }
            if (trackedSince <= 0d) trackedSince = Time.realtimeSinceStartupAsDouble;
            bool wasStable = stableTracking;
            stableTracking = Time.realtimeSinceStartupAsDouble - trackedSince >= StableTrackingSeconds;
            if (!stableTracking)
            {
                SetRouteVisible(false);
                SetAuthoringPreviewVisible(false);
                status = "Đang dò không gian... Hãy lia camera quanh phòng để chốt vị trí.";
                return;
            }
            if (authoringMode)
            {
                SetRouteVisible(false);
                if (!wasStable)
                {
                    status = isRecordingRoute
                        ? "Tracking ổn định trở lại. Tiếp tục đi tới điểm đến mới."
                        : "Đã khóa không gian. Bấm Bắt đầu ghi đường hoặc Đặt điểm tại đây.";
                }
                UpdateAuthoringSession();
                return;
            }
            if (!wasStable) lastRouteUpdate = 0d;
            if (Time.realtimeSinceStartupAsDouble - lastRouteUpdate >= RouteRefreshSeconds)
            {
                lastRouteUpdate = Time.realtimeSinceStartupAsDouble;
                UpdateRoute();
            }
            if (stableTracking && companionGuide != null && trackedAnchor != null && arCamera != null)
            {
                companionGuide.UpdateCompanion(trackedAnchor.InverseTransformPoint(arCamera.transform.position));
            }
            UpdateArrivalSpeechTrigger();
        }

        private void UpdateArrivalSpeechTrigger()
        {
            bool isNavigating = !authoringMode && trackingRequestAccepted && stableTracking && activeRoutedPoiId != null;
            bool isArrived = isNavigating && companionGuide != null && companionGuide.IsAtDestination;
            if (!isArrived)
            {
                if (!isNavigating || (companionGuide != null && !companionGuide.IsAtDestination))
                {
                    lastAutoSpokenArrivalKey = null;
                }
                return;
            }

            PublishedGraphPoiV2 destPoi = FindActivePoi(activeRoutedPoiId);
            string destLabel = destPoi != null ? CapitalizeFirst(destPoi.Label) : "Điểm đến";
            string destDesc = destPoi != null ? destPoi.Description : null;
            string speechText = BuildArrivalSpeechText(destLabel, destDesc);
            string arrivalKey = activeRoutedPoiId + "|" + speechText;
            if (string.Equals(lastAutoSpokenArrivalKey, arrivalKey, StringComparison.Ordinal))
            {
                return;
            }

            lastAutoSpokenArrivalKey = arrivalKey;
            if (TtsEnabled)
            {
                EnsureTtsService().Speak(speechText);
            }
        }

        private void LateUpdate()
        {
            if (useUiToolkit && uiToolkitController != null && uiToolkitController.IsActive)
            {
                bool isNavigating = !authoringMode && trackingRequestAccepted && stableTracking && activeRoutedPoiId != null;
                PublishedGraphPoiV2 destPoi = FindActivePoi(isNavigating ? activeRoutedPoiId : selectedPoiId);
                string destLabel = destPoi != null ? CapitalizeFirst(destPoi.Label) : "Điểm đến";
                string destDesc = destPoi != null ? destPoi.Description : null;
                bool isArrived = companionGuide != null && companionGuide.IsAtDestination;
                float totalLen = companionGuide != null ? companionGuide.TotalRouteLength : 0f;
                float userDist = companionGuide != null ? companionGuide.UserDistanceOnRoute : 0f;
                float remainingLen = companionGuide != null ? Mathf.Max(0f, totalLen - userDist) : 0f;
                string compMsg = companionGuide != null ? companionGuide.CompanionStatusMessage : "Đi theo Robot và dải dẫn hướng trên sàn.";
                if (isArrived && !string.IsNullOrWhiteSpace(destDesc))
                {
                    compMsg = $"Chào mừng bạn đến {destLabel}!";
                }

                bool hasRobotAnchor = false;
                bool robotInFront = false;
                Vector2 robotViewportPos = Vector2.zero;
                if (isNavigating && isArrived && arCamera != null && companionGuide != null &&
                    companionGuide.TryGetCompanionWorldBounds(out Bounds companionBounds) &&
                    companionGuide.TryGetSpeechAnchorWorldPosition(out Vector3 speechWorldPos))
                {
                    hasRobotAnchor = true;
                    robotInFront = DoraemonCompanionGuide.TryProjectSpeechBubbleInCameraView(
                        arCamera,
                        companionBounds,
                        speechWorldPos,
                        out robotViewportPos);
                }

                var state = new NavigationUiState
                {
                    IsTrackingAccepted = trackingRequestAccepted,
                    IsStableTracking = stableTracking,
                    IsNavigating = isNavigating,
                    DestinationLabel = destLabel,
                    DestinationDescription = destDesc,
                    RouteRemainingMeters = remainingLen,
                    TotalRouteMeters = totalLen,
                    CompanionMessage = compMsg,
                    IsCompanionWaiting = companionGuide != null && companionGuide.IsWaitingForUser,
                    IsCompanionRerouting = companionGuide != null && companionGuide.NeedsReroute,
                    IsArrived = isArrived,
                    DiagnosticsText = BuildDiagnosticsSummary(),
                    StatusMessage = status,
                    BootstrapState = bootstrap != null ? bootstrap.State : NavigationBootstrapState.ManifestLoaded,
                    BootstrapError = bootstrap != null ? bootstrap.ErrorMessage : null,
                    ActiveFloorId = bootstrap != null ? bootstrap.ActiveFloorId : null,
                    TotalPoiCount = activePois != null ? activePois.Count : (visiblePois != null ? visiblePois.Count : 0),
                    IsBusyConnecting = sitesBusy || startPending,
                    Fps = occlusionController != null ? occlusionController.CurrentFps : 0f,
                    FrameTimeMs = occlusionController != null ? occlusionController.CurrentFrameTimeMs : 0f,
                    MemoryMb = occlusionController != null ? occlusionController.AllocatedMemoryMb : 0f,
                    DepthAvailable = occlusionController != null && occlusionController.IsDepthSubsystemAvailable,
                    EffectiveOcclusionMode = occlusionController != null ? occlusionController.EffectiveMode : ArOcclusionQualityMode.Off,
                    IsAuthoringMode = authoringMode,
                    IsRecordingWalk = isRecordingRoute,
                    RecordedWalkMeters = ComputeRecordedWalkLength(),
                    RecordedPointCount = recordedFloorPoints.Count,
                    NearestGraphDistanceMeters = currentAuthoringSnapDistance,
                    CurrentAnchorLocalPos = currentAuthoringFloorPoint,
                    IsUsingLocalManifest = bootstrap != null && bootstrap.IsUsingLocalOverride,
                    HasRobotBubbleAnchor = hasRobotAnchor,
                    IsRobotInFrontOfCamera = robotInFront,
                    RobotBubbleViewportPos = robotViewportPos,
                    IsTtsSpeaking = ttsService != null && ttsService.IsSpeaking,
                    TtsStatusSummary = ttsService != null ? ttsService.StatusSummary : string.Empty,
                };

                uiToolkitController.UpdateState(in state);
            }
        }

        private readonly NavigationUiTheme theme = new NavigationUiTheme();
        private Vector2 poiScrollPos;
        private string normalizedPoiSearchQuery = string.Empty;

        private void OnGUI()
        {
            if (useUiToolkit && uiToolkitController != null && uiToolkitController.IsActive)
            {
                return;
            }

            theme.Initialize();

            uiScale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 900f, 0.9f, 1.35f);
            Rect physicalSafeArea = Screen.safeArea;
            if (physicalSafeArea.width < 1f || physicalSafeArea.height < 1f)
                physicalSafeArea = new Rect(0f, 0f, Screen.width, Screen.height);

            safeUiRect = new Rect(
                physicalSafeArea.x / uiScale,
                (Screen.height - physicalSafeArea.yMax) / uiScale,
                physicalSafeArea.width / uiScale,
                physicalSafeArea.height / uiScale);

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            if (!trackingRequestAccepted)
            {
                Rect fullScreen = new Rect(0f, 0f, Screen.width / uiScale, Screen.height / uiScale);
                GUI.DrawTexture(fullScreen, theme.LauncherBackground, ScaleMode.StretchToFill);
            }

            bool modalOpen = panelOpen || settingsOpen;
            bool previousEnabled = GUI.enabled;
            if (modalOpen) GUI.enabled = false;

            bool isNavigating = trackingRequestAccepted && stableTracking && activeRoutedPoiId != null;

            if (isNavigating)
            {
                DrawActiveNavigationUi();
            }
            else
            {
                DrawIdleOrSearchingUi();
            }

            GUI.enabled = previousEnabled;

            if (panelOpen)
            {
                DrawDestinationPickerModal();
            }
            if (settingsOpen)
            {
                DrawSettingsPanel();
            }

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private void DrawActiveNavigationUi()
        {
            PublishedGraphPoiV2 destPoi = FindActivePoi(selectedPoiId);
            string destination = destPoi != null ? CapitalizeFirst(destPoi.Label) : "Điểm đến";
            float cardWidth = Mathf.Min(680f, safeUiRect.width - 24f);
            float cardHeight = 84f;
            Rect card = new Rect(safeUiRect.center.x - cardWidth * 0.5f, safeUiRect.y + 12f, cardWidth, cardHeight);
            GUI.Box(card, GUIContent.none, theme.WhitePillBarStyle);

            float boxSize = 44f;
            float bx = card.x + 14f;
            float by = card.y + (card.height - boxSize) * 0.5f;
            if (theme.IconBoxBlue != null)
            {
                GUI.DrawTexture(new Rect(bx, by, boxSize, boxSize), theme.IconBoxBlue, ScaleMode.StretchToFill);
            }
            float pinSize = 22f;
            float px = bx + (boxSize - pinSize) * 0.5f;
            float py = by + (boxSize - pinSize) * 0.5f;
            Color oldColor = GUI.color;
            GUI.color = new Color(0.11f, 0.42f, 0.96f, 1f);
            GUI.DrawTexture(new Rect(px, py, pinSize, pinSize), theme.IconPin, ScaleMode.ScaleToFit);
            GUI.color = oldColor;

            float stopWidth = 92f;
            float stopHeight = 44f;
            Rect stopRect = new Rect(card.xMax - stopWidth - 14f, card.y + (card.height - stopHeight) * 0.5f, stopWidth, stopHeight);
            if (theme.DrawPillButton(stopRect, theme.IconClose, "Dừng", theme.DangerPillButtonStyle, 16f))
                StopNavigation();

            Rect settingsButton = new Rect(stopRect.x - 54f, stopRect.y, 44f, 44f);
            if (theme.DrawIconButton(settingsButton, theme.IconGear, theme.LightIconButtonStyle, 20f, NavigationUiPalette.BrandBlue))
                settingsOpen = true;

            float routeLength = companionGuide != null ? companionGuide.TotalRouteLength : 0f;
            float textX = bx + boxSize + 14f;
            float textWidth = settingsButton.x - textX - 10f;
            string breadcrumb = $"<color=#1e293b>AR Viewer</color>  <color=#94a3b8>›</color>  <color=#1d6bfe><b>{destination}</b></color>";
            GUI.Label(new Rect(textX, card.y + 16f, textWidth, 26f), breadcrumb, theme.HeaderBreadcrumbStyle);
            GUI.Label(new Rect(textX, card.y + 42f, textWidth, 22f), $"Lộ trình còn khoảng {routeLength:0} m", theme.HeaderSubtitleStyle);

            float companionWidth = Mathf.Min(680f, safeUiRect.width - 24f);
            const float companionHeight = 100f;
            Rect companionCard = new Rect(
                safeUiRect.center.x - companionWidth * 0.5f,
                safeUiRect.yMax - companionHeight - 16f,
                companionWidth,
                companionHeight);
            GUI.Box(companionCard, GUIContent.none, theme.LightDockCardStyle);
            GUI.DrawTexture(new Rect(companionCard.x + 18f, companionCard.y + 26f, 48f, 48f), theme.IconCompanion, ScaleMode.ScaleToFit);

            bool isArrived = companionGuide != null && companionGuide.IsAtDestination;
            float textAvailableWidth = isArrived ? companionCard.width - 210f : companionCard.width - 96f;
            GUI.Label(new Rect(companionCard.x + 78f, companionCard.y + 18f, textAvailableWidth, 22f), "ROBOT DẪN ĐƯỜNG", theme.LightEyebrowStyle);
            string companionMessage = companionGuide != null ? companionGuide.CompanionStatusMessage : "Đi theo Robot và đường sáng trên sàn.";
            GUI.Label(new Rect(companionCard.x + 78f, companionCard.y + 42f, textAvailableWidth, 50f), companionMessage, theme.LightBodyStyle);

            if (isArrived)
            {
                Rect endNavigation = new Rect(companionCard.xMax - 116f, companionCard.y + 25f, 102f, 50f);
                if (theme.DrawPillButton(endNavigation, theme.IconCheck, "Kết thúc", theme.PrimaryPillButtonStyle, 18f))
                    StopNavigation();
            }
        }

        private void DrawIdleOrSearchingUi()
        {
            if (trackingRequestAccepted && !stableTracking)
            {
                DrawBrandHeader("Định vị VPS", "Đang xác định vị trí của bạn");

                if (theme.BottomGradientTexture != null)
                {
                    float gradHeight = 220f;
                    Rect gradRect = new Rect(0f, Screen.height / uiScale - gradHeight, Screen.width / uiScale, gradHeight);
                    GUI.DrawTexture(gradRect, theme.BottomGradientTexture, ScaleMode.StretchToFill);
                }

                float stopWidth = Mathf.Min(340f, safeUiRect.width - 32f);
                Rect stopRect = new Rect(safeUiRect.center.x - stopWidth * 0.5f, safeUiRect.yMax - 78f, stopWidth, 58f);
                if (theme.DrawPillButton(stopRect, theme.IconClose, "Hủy định vị", theme.DangerPillButtonStyle, 18f))
                    StopNavigation();
                return;
            }

            DrawBrandHeader("Điều hướng", "Dẫn đường trong nhà");

            if (!panelOpen)
            {
                DrawReadinessCard();
                DrawDestinationActionDock();
            }
        }

        private void DrawBrandHeader(string section, string subtitle)
        {
            float width = Mathf.Min(680f, safeUiRect.width - 24f);
            const float height = 74f;
            Rect header = new Rect(safeUiRect.center.x - width * 0.5f, safeUiRect.y + 12f, width, height);
            GUI.Box(header, GUIContent.none, theme.WhitePillBarStyle);

            Rect backBtn = new Rect(header.x + 14f, header.y + 15f, 44f, 44f);
            if (theme.DrawIconButton(backBtn, theme.IconBackDark, GUIStyle.none, 22f))
            {
                if (trackingRequestAccepted) StopNavigation();
                if (panelOpen) panelOpen = false;
            }

            Rect settingsButton = new Rect(header.xMax - 58f, header.y + 15f, 44f, 44f);
            if (theme.DrawIconButton(settingsButton, theme.IconGear, theme.LightIconButtonStyle, 20f, NavigationUiPalette.BrandBlue))
                settingsOpen = true;

            string breadcrumb = $"<color=#1e293b>AR Viewer</color>  <color=#94a3b8>›</color>  <color=#1d6bfe><b>{section}</b></color>";
            GUI.Label(new Rect(header.x + 64f, header.y + 13f, header.width - 144f, 26f), breadcrumb, theme.HeaderBreadcrumbStyle);
            GUI.Label(new Rect(header.x + 64f, header.y + 38f, header.width - 144f, 22f), subtitle, theme.HeaderSubtitleStyle);
        }

        private void LoadUserSettings()
        {
            routeLineWidth = Mathf.Clamp(PlayerPrefs.GetFloat(SettingsPrefix + "RouteWidth", 0.22f), 0.04f, 0.5f);
            routeLineOpacity = Mathf.Clamp(PlayerPrefs.GetFloat(SettingsPrefix + "RouteOpacity", 0.95f), 0.25f, 1f);
            routeColorIndex = Mathf.Clamp(PlayerPrefs.GetInt(SettingsPrefix + "RouteColor", 0), -1, RouteColorOptions.Length - 1);
            routeHue = Mathf.Clamp01(PlayerPrefs.GetFloat(SettingsPrefix + "RouteHue", DefaultRouteHue));
            routeLineColor = routeColorIndex >= 0
                ? RouteColorOptions[routeColorIndex]
                : Color.HSVToRGB(routeHue, 0.9f, 1f);
            routeLineColor.a = routeLineOpacity;

            if (companionGuide != null)
            {
                companionGuide.ModelScaleMultiplier = PlayerPrefs.GetFloat(SettingsPrefix + "ModelScale", 1f);
                companionGuide.ModelBrightness = PlayerPrefs.GetFloat(SettingsPrefix + "ModelBrightness", DoraemonCompanionGuide.DefaultModelBrightness);

                float storedLeadDistance = PlayerPrefs.GetFloat(SettingsPrefix + "LeadDistance", DoraemonCompanionGuide.DefaultLeadDistance);
                if (Mathf.Abs(storedLeadDistance - DoraemonCompanionGuide.LegacyDefaultLeadDistance) < 0.0005f)
                {
                    storedLeadDistance = DoraemonCompanionGuide.DefaultLeadDistance;
                }
                companionGuide.LeadDistance = storedLeadDistance;

                // Migrate the legacy 8 cm ground drop: it sank the companion feet into the real floor,
                // where AR depth noise clipped them. Only a value the user explicitly changed is kept.
                float storedGroundDrop = PlayerPrefs.GetFloat(SettingsPrefix + "GroundDrop", DoraemonCompanionGuide.DefaultGroundDrop);
                if (Mathf.Abs(storedGroundDrop - DoraemonCompanionGuide.LegacyDefaultGroundDrop) < 0.0005f)
                {
                    storedGroundDrop = DoraemonCompanionGuide.DefaultGroundDrop;
                }
                companionGuide.GroundDropMeters = storedGroundDrop;
            }

            if (occlusionController != null)
            {
                int mode = Mathf.Clamp(PlayerPrefs.GetInt(SettingsPrefix + "OcclusionMode", (int)ArOcclusionQualityMode.Balanced),
                    (int)ArOcclusionQualityMode.Off, (int)ArOcclusionQualityMode.HighQuality);
                occlusionController.SetQualityMode((ArOcclusionQualityMode)mode);
                occlusionController.PreferSmoothEdges = PlayerPrefs.GetInt(SettingsPrefix + "SmoothEdges", 1) != 0;
                occlusionController.SemanticSuppressionEnabled = PlayerPrefs.GetInt(SettingsPrefix + "SemanticSuppression", 0) != 0;
                occlusionController.SuppressGroundChannel = PlayerPrefs.GetInt(SettingsPrefix + "SuppressGround", 0) != 0;
                occlusionController.DepthVisualizationEnabled = PlayerPrefs.GetInt(SettingsPrefix + "DepthVisualization", 0) != 0;
                occlusionController.RuntimeMeshDebugEnabled = PlayerPrefs.GetInt(SettingsPrefix + "MeshDebug", 0) != 0;

                // Migrate the legacy 18 cm route depth bias: it lifted the ribbon off the floor and let
                // it punch through low obstacles. Only a value the user explicitly changed is kept.
                float storedRouteBias = PlayerPrefs.GetFloat(SettingsPrefix + "RouteDepthBias", ArOcclusionController.DefaultRouteDepthBiasMeters);
                if (Mathf.Abs(storedRouteBias - LegacyRouteDepthBiasMeters) < 0.0005f)
                {
                    storedRouteBias = ArOcclusionController.DefaultRouteDepthBiasMeters;
                }
                occlusionController.RouteDepthBiasMeters = storedRouteBias;
                occlusionController.UseStabilizedOcclusionDistance = PlayerPrefs.GetInt(SettingsPrefix + "StableOcclusionDistance", 1) != 0;
            }

            if (ttsService != null)
            {
                ttsService.Enabled = PlayerPrefs.GetInt(SettingsPrefix + "TtsEnabled", 1) != 0;
                ttsService.SpeakingRate = Mathf.Clamp(
                    PlayerPrefs.GetFloat(SettingsPrefix + "TtsRate", GoogleTextToSpeechService.DefaultSpeakingRate),
                    GoogleTextToSpeechService.MinSpeakingRate,
                    GoogleTextToSpeechService.MaxSpeakingRate);
                ttsService.CloudApiKey = PlayerPrefs.GetString(SettingsPrefix + "TtsCloudApiKey", string.Empty);
            }
        }

        private void SaveUserSettings(bool flushToDisk)
        {
            PlayerPrefs.SetFloat(SettingsPrefix + "RouteWidth", routeLineWidth);
            PlayerPrefs.SetFloat(SettingsPrefix + "RouteOpacity", routeLineOpacity);
            PlayerPrefs.SetInt(SettingsPrefix + "RouteColor", routeColorIndex);
            PlayerPrefs.SetFloat(SettingsPrefix + "RouteHue", routeHue);
            if (companionGuide != null)
            {
                PlayerPrefs.SetFloat(SettingsPrefix + "ModelScale", companionGuide.ModelScaleMultiplier);
                PlayerPrefs.SetFloat(SettingsPrefix + "ModelBrightness", companionGuide.ModelBrightness);
                PlayerPrefs.SetFloat(SettingsPrefix + "LeadDistance", companionGuide.LeadDistance);
                PlayerPrefs.SetFloat(SettingsPrefix + "GroundDrop", companionGuide.GroundDropMeters);
            }
            if (occlusionController != null)
            {
                PlayerPrefs.SetInt(SettingsPrefix + "OcclusionMode", (int)occlusionController.RequestedMode);
                PlayerPrefs.SetInt(SettingsPrefix + "SmoothEdges", occlusionController.PreferSmoothEdges ? 1 : 0);
                PlayerPrefs.SetInt(SettingsPrefix + "SemanticSuppression", occlusionController.SemanticSuppressionEnabled ? 1 : 0);
                PlayerPrefs.SetInt(SettingsPrefix + "SuppressGround", occlusionController.SuppressGroundChannel ? 1 : 0);
                PlayerPrefs.SetInt(SettingsPrefix + "DepthVisualization", occlusionController.DepthVisualizationEnabled ? 1 : 0);
                PlayerPrefs.SetInt(SettingsPrefix + "MeshDebug", occlusionController.RuntimeMeshDebugEnabled ? 1 : 0);
                PlayerPrefs.SetFloat(SettingsPrefix + "RouteDepthBias", occlusionController.RouteDepthBiasMeters);
                PlayerPrefs.SetInt(SettingsPrefix + "StableOcclusionDistance", occlusionController.UseStabilizedOcclusionDistance ? 1 : 0);
            }
            if (ttsService != null)
            {
                PlayerPrefs.SetInt(SettingsPrefix + "TtsEnabled", ttsService.Enabled ? 1 : 0);
                PlayerPrefs.SetFloat(SettingsPrefix + "TtsRate", ttsService.SpeakingRate);
                PlayerPrefs.SetString(SettingsPrefix + "TtsCloudApiKey", ttsService.CloudApiKey ?? string.Empty);
            }
            if (flushToDisk) PlayerPrefs.Save();
        }

        private void DrawSettingsPanel()
        {
            float modalWidth = Mathf.Min(700f, safeUiRect.width - 24f);
            float modalHeight = Mathf.Min(840f, safeUiRect.height - 24f);
            Rect modalRect = new Rect(safeUiRect.center.x - modalWidth * 0.5f, safeUiRect.center.y - modalHeight * 0.5f, modalWidth, modalHeight);

            Event current = Event.current;
            if (current != null && current.type == EventType.MouseDown && !modalRect.Contains(current.mousePosition))
            {
                GUI.FocusControl(null);
                settingsOpen = false;
                SaveUserSettings(true);
                current.Use();
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0.04f, 0.08f, 0.16f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width / uiScale, Screen.height / uiScale), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Box(modalRect, GUIContent.none, theme.LightModalCardStyle);

            float padding = 20f;
            GUI.DrawTexture(new Rect(modalRect.center.x - 24f, modalRect.y + 10f, 48f, 5f), theme.SheetHandle);
            GUI.Label(new Rect(modalRect.x + padding, modalRect.y + 25f, modalRect.width - 100f, 32f), "Tùy chỉnh AR", theme.LightTitleStyle);
            GUI.Label(new Rect(modalRect.x + padding, modalRect.y + 57f, modalRect.width - 100f, 22f), "Đường dẫn, model và Depth Occlusion", theme.LightCaptionStyle);

            Rect closeRect = new Rect(modalRect.xMax - padding - 40f, modalRect.y + 24f, 40f, 40f);
            if (theme.DrawIconButton(closeRect, theme.IconCloseDark, theme.LightIconButtonStyle, 18f))
            {
                settingsOpen = false;
                SaveUserSettings(true);
            }

            Rect viewport = new Rect(modalRect.x + padding, modalRect.y + 88f,
                modalRect.width - padding * 2f, modalRect.height - 158f);
            float contentWidth = Mathf.Max(100f, viewport.width - 18f);
            Rect contentRect = new Rect(0f, 0f, contentWidth, 1360f);
            settingsScrollPosition = GUI.BeginScrollView(viewport, settingsScrollPosition, contentRect);
            float y = 0f;

            DrawSettingsSection(ref y, contentWidth, "ĐƯỜNG DẪN");
            float newValue = DrawSettingsSlider(contentWidth, y, "Độ dày đường", routeLineWidth, 0.04f, 0.5f, "0.00 m");
            if (!Mathf.Approximately(newValue, routeLineWidth))
            {
                routeLineWidth = newValue;
                ApplyLineSettings();
                SaveUserSettings(false);
            }
            y += 68f;

            string colorLabel = routeColorIndex >= 0 ? RouteColorNames[routeColorIndex] : "Tùy chỉnh";
            GUI.Label(new Rect(8f, y, contentWidth - 16f, 23f), "Màu tuyến · " + colorLabel, theme.LightCardTitleStyle);
            y += 26f;
            DrawRouteColorChoices(contentWidth, y);
            y += 54f;

            float hueDegrees = DrawSettingsSlider(contentWidth, y, "Sắc màu tùy chỉnh", routeHue * 360f, 0f, 360f, "°");
            if (!Mathf.Approximately(hueDegrees, routeHue * 360f))
            {
                routeHue = Mathf.Clamp01(hueDegrees / 360f);
                routeColorIndex = -1;
                routeLineColor = Color.HSVToRGB(routeHue, 0.9f, 1f);
                routeLineColor.a = routeLineOpacity;
                ApplyLineSettings();
                SaveUserSettings(false);
            }
            y += 68f;

            newValue = DrawSettingsSlider(contentWidth, y, "Độ mờ đường", routeLineOpacity, 0.25f, 1f, "0%");
            if (!Mathf.Approximately(newValue, routeLineOpacity))
            {
                routeLineOpacity = newValue;
                routeLineColor = RouteColorOptions[routeColorIndex];
                routeLineColor.a = routeLineOpacity;
                ApplyLineSettings();
                SaveUserSettings(false);
            }
            y += 68f;

            float depthBias = occlusionController != null ? occlusionController.RouteDepthBiasMeters : ArOcclusionController.DefaultRouteDepthBiasMeters;
            newValue = DrawSettingsSlider(contentWidth, y, "Bù độ sâu tuyến", depthBias, 0f, 0.15f, "0.00 m");
            if (!Mathf.Approximately(newValue, depthBias) && occlusionController != null)
            {
                occlusionController.RouteDepthBiasMeters = newValue;
                ApplyLineSettings();
                SaveUserSettings(false);
            }
            y += 68f;

            DrawSettingsSection(ref y, contentWidth, "MODEL DẪN ĐƯỜNG");
            float modelScale = companionGuide != null ? companionGuide.ModelScaleMultiplier : 1f;
            newValue = DrawSettingsSlider(contentWidth, y, "Kích thước model", modelScale, 0.5f, 2f, "0.00×");
            if (!Mathf.Approximately(newValue, modelScale) && companionGuide != null)
            {
                companionGuide.ModelScaleMultiplier = newValue;
                SaveUserSettings(false);
            }
            y += 68f;

            float modelBrightness = companionGuide != null ? companionGuide.ModelBrightness : DoraemonCompanionGuide.DefaultModelBrightness;
            newValue = DrawSettingsSlider(contentWidth, y, "Độ sáng model", modelBrightness, 0.20f, 2f, "0.00");
            if (!Mathf.Approximately(newValue, modelBrightness) && companionGuide != null)
            {
                companionGuide.ModelBrightness = newValue;
                SaveUserSettings(false);
            }
            y += 68f;

            float leadDistance = companionGuide != null ? companionGuide.LeadDistance : DoraemonCompanionGuide.DefaultLeadDistance;
            newValue = DrawSettingsSlider(contentWidth, y, "Cự ly dẫn trước", leadDistance, 1.4f, 3f, "0.00 m");
            if (!Mathf.Approximately(newValue, leadDistance) && companionGuide != null)
            {
                companionGuide.LeadDistance = newValue;
                SaveUserSettings(false);
            }
            y += 68f;

            float groundDrop = companionGuide != null ? companionGuide.GroundDropMeters : DoraemonCompanionGuide.DefaultGroundDrop;
            newValue = DrawSettingsSlider(contentWidth, y, "Độ hạ model", groundDrop, 0f, 0.15f, "0.00 m");
            if (!Mathf.Approximately(newValue, groundDrop) && companionGuide != null)
            {
                companionGuide.GroundDropMeters = newValue;
                SaveUserSettings(false);
            }
            y += 68f;

            DrawSettingsSection(ref y, contentWidth, "DEPTH OCCLUSION");
            if (occlusionController != null)
            {
                DrawOcclusionModeChoice(contentWidth, ref y, ArOcclusionQualityMode.Off, "Tắt", "Không che model sau vật thể thật");
                DrawOcclusionModeChoice(contentWidth, ref y, ArOcclusionQualityMode.Fallback, "Tiết kiệm", "Depth cơ bản, giảm xử lý NSDK");
                DrawOcclusionModeChoice(contentWidth, ref y, ArOcclusionQualityMode.Balanced, "Cân bằng", "Khuyến nghị cho điện thoại và tablet");
                DrawOcclusionModeChoice(contentWidth, ref y, ArOcclusionQualityMode.HighQuality, "Chất lượng cao", "Thêm ổn định bằng lưới, tốn tài nguyên hơn");

                DrawSettingsToggle(contentWidth, ref y, "Làm mượt viền depth", "Giảm răng cưa quanh vật thể bị che", OcclusionToggle.SmoothEdges);
                DrawSettingsToggle(contentWidth, ref y, "Ổn định khoảng cách che", "Tránh che sai khi có vật thể gần camera", OcclusionToggle.StableOcclusionDistance);
                DrawSettingsToggle(contentWidth, ref y, "Lọc vùng trời", "Loại bỏ vùng sky khỏi depth suppression", OcclusionToggle.SemanticSuppression);
                DrawSettingsToggle(contentWidth, ref y, "Lọc mặt đất", "Có thể giảm che khuất nhầm trên sàn", OcclusionToggle.SuppressGround);
                DrawSettingsToggle(contentWidth, ref y, "Hiện hình ảnh depth", "Chỉ hỗ trợ trong chế độ Cân bằng / Cao", OcclusionToggle.DepthVisualization);
                DrawSettingsToggle(contentWidth, ref y, "Hiện lưới debug", "Có dữ liệu lưới trong chế độ Chất lượng cao", OcclusionToggle.MeshDebug);

                string performance = string.Format(CultureInfo.InvariantCulture, "{0:0} FPS · {1:0} ms · {2:0} MB",
                    occlusionController.CurrentFps, occlusionController.CurrentFrameTimeMs, occlusionController.AllocatedMemoryMb);
                GUI.Label(new Rect(8f, y + 4f, contentWidth - 16f, 20f), performance, theme.LightCardTitleStyle);
                string depthState = "Depth " + (occlusionController.IsDepthSubsystemAvailable ? "sẵn sàng" : "chưa khả dụng") +
                    " · Đang dùng: " + GetQualityModeLabel(occlusionController.EffectiveMode) + "\n" + occlusionController.DiagnosticsReport;
                GUI.Label(new Rect(8f, y + 25f, contentWidth - 16f, 88f), depthState, theme.LightCaptionStyle);
                y += 118f;
            }
            GUI.EndScrollView();

            Rect resetRect = new Rect(modalRect.x + padding, modalRect.yMax - 54f, 156f, 40f);
            if (GUI.Button(resetRect, "Mặc định", theme.LightSecondaryButtonStyle))
                ResetUserSettings();

            Rect doneRect = new Rect(modalRect.xMax - padding - 132f, modalRect.yMax - 54f, 132f, 40f);
            if (theme.DrawPillButton(doneRect, null, "Xong", theme.PrimaryPillButtonStyle, 0f))
            {
                settingsOpen = false;
                SaveUserSettings(true);
            }
        }

        private void DrawSettingsSection(ref float y, float width, string title)
        {
            GUI.Label(new Rect(8f, y + 4f, width - 16f, 24f), title, theme.LightEyebrowStyle);
            y += 32f;
        }

        private float DrawSettingsSlider(float width, float y, string label, float value, float min, float max, string format)
        {
            GUI.Label(new Rect(8f, y, width - 104f, 24f), label, theme.LightCardTitleStyle);
            string displayValue = format == "0%"
                ? Mathf.RoundToInt(value * 100f) + "%"
                : value.ToString("0.00", CultureInfo.InvariantCulture) + format.Replace("0.00", string.Empty);
            GUI.Label(new Rect(width - 92f, y, 84f, 24f), displayValue, theme.LightCardSubtitleStyle);
            return GUI.HorizontalSlider(
                new Rect(8f, y + 26f, width - 16f, 24f),
                value,
                min,
                max,
                theme.LightSliderStyle,
                theme.LightSliderThumbStyle);
        }

        private void DrawRouteColorChoices(float width, float y)
        {
            const float gap = 8f;
            float chipWidth = Mathf.Min(48f, (width - 16f - gap * (RouteColorOptions.Length - 1)) / RouteColorOptions.Length);
            float totalWidth = chipWidth * RouteColorOptions.Length + gap * (RouteColorOptions.Length - 1);
            float x = (width - totalWidth) * 0.5f;

            for (int i = 0; i < RouteColorOptions.Length; i++)
            {
                Rect chipRect = new Rect(x + i * (chipWidth + gap), y, chipWidth, 38f);
                bool selected = routeColorIndex == i;
                GUI.Box(chipRect, GUIContent.none, selected ? theme.LightCardSelectedStyle : theme.LightCardNormalStyle);
                if (GUI.Button(chipRect, GUIContent.none, GUIStyle.none))
                {
                    routeColorIndex = i;
                    Color.RGBToHSV(RouteColorOptions[i], out routeHue, out _, out _);
                    routeLineColor = RouteColorOptions[i];
                    routeLineColor.a = routeLineOpacity;
                    ApplyLineSettings();
                    SaveUserSettings(false);
                }
                Color previousColor = GUI.color;
                GUI.color = RouteColorOptions[i];
                GUI.DrawTexture(new Rect(chipRect.x + 7f, chipRect.y + 7f, chipRect.width - 14f, chipRect.height - 14f), Texture2D.whiteTexture);
                GUI.color = previousColor;
            }
        }

        private void DrawOcclusionModeChoice(float width, ref float y, ArOcclusionQualityMode mode, string title, string detail)
        {
            bool selected = occlusionController.RequestedMode == mode;
            Rect rect = new Rect(4f, y, width - 8f, 46f);
            if (GUI.Button(rect, GUIContent.none, selected ? theme.LightCardSelectedStyle : theme.LightCardNormalStyle))
            {
                occlusionController.SetQualityMode(mode);
                SaveUserSettings(false);
            }
            GUI.Label(new Rect(rect.x + 12f, rect.y + 4f, rect.width - 34f, 19f), title, theme.LightCardTitleStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 23f, rect.width - 34f, 18f), detail, theme.LightCaptionStyle);
            if (selected && theme.IconCheck != null)
            {
                Color previousColor = GUI.color;
                GUI.color = NavigationUiPalette.BrandBlue;
                GUI.DrawTexture(new Rect(rect.xMax - 25f, rect.y + 13f, 14f, 14f), theme.IconCheck, ScaleMode.ScaleToFit);
                GUI.color = previousColor;
            }
            y += 50f;
        }

        private void DrawSettingsToggle(float width, ref float y, string title, string detail, OcclusionToggle setting)
        {
            bool enabled = GetOcclusionToggle(setting);
            Rect rect = new Rect(4f, y, width - 8f, 46f);
            if (GUI.Button(rect, GUIContent.none, enabled ? theme.LightCardSelectedStyle : theme.LightCardNormalStyle))
            {
                SetOcclusionToggle(setting, !enabled);
                SaveUserSettings(false);
            }
            GUI.Label(new Rect(rect.x + 12f, rect.y + 4f, rect.width - 42f, 19f), title, theme.LightCardTitleStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 23f, rect.width - 42f, 18f), detail, theme.LightCaptionStyle);
            if (enabled && theme.IconCheck != null)
            {
                Color previousColor = GUI.color;
                GUI.color = NavigationUiPalette.BrandBlue;
                GUI.DrawTexture(new Rect(rect.xMax - 25f, rect.y + 13f, 14f, 14f), theme.IconCheck, ScaleMode.ScaleToFit);
                GUI.color = previousColor;
            }
            y += 50f;
        }

        private bool GetOcclusionToggle(OcclusionToggle setting)
        {
            if (occlusionController == null) return false;
            switch (setting)
            {
                case OcclusionToggle.SmoothEdges: return occlusionController.PreferSmoothEdges;
                case OcclusionToggle.StableOcclusionDistance: return occlusionController.UseStabilizedOcclusionDistance;
                case OcclusionToggle.SemanticSuppression: return occlusionController.SemanticSuppressionEnabled;
                case OcclusionToggle.SuppressGround: return occlusionController.SuppressGroundChannel;
                case OcclusionToggle.DepthVisualization: return occlusionController.DepthVisualizationEnabled;
                case OcclusionToggle.MeshDebug: return occlusionController.RuntimeMeshDebugEnabled;
                default: return false;
            }
        }

        private void SetOcclusionToggle(OcclusionToggle setting, bool value)
        {
            if (occlusionController == null) return;
            switch (setting)
            {
                case OcclusionToggle.SmoothEdges: occlusionController.PreferSmoothEdges = value; break;
                case OcclusionToggle.StableOcclusionDistance: occlusionController.UseStabilizedOcclusionDistance = value; break;
                case OcclusionToggle.SemanticSuppression: occlusionController.SemanticSuppressionEnabled = value; break;
                case OcclusionToggle.SuppressGround: occlusionController.SuppressGroundChannel = value; break;
                case OcclusionToggle.DepthVisualization: occlusionController.DepthVisualizationEnabled = value; break;
                case OcclusionToggle.MeshDebug: occlusionController.RuntimeMeshDebugEnabled = value; break;
            }
        }

        private void ResetUserSettings()
        {
            routeLineWidth = 0.22f;
            routeLineOpacity = 0.95f;
            routeColorIndex = 0;
            routeHue = DefaultRouteHue;
            routeLineColor = RouteColorOptions[routeColorIndex];
            routeLineColor.a = routeLineOpacity;
            if (companionGuide != null)
            {
                companionGuide.ModelScaleMultiplier = 1f;
                companionGuide.ModelBrightness = DoraemonCompanionGuide.DefaultModelBrightness;
                companionGuide.LeadDistance = DoraemonCompanionGuide.DefaultLeadDistance;
                companionGuide.GroundDropMeters = DoraemonCompanionGuide.DefaultGroundDrop;
            }
            if (occlusionController != null)
            {
                occlusionController.SetQualityMode(ArOcclusionQualityMode.Balanced);
                occlusionController.PreferSmoothEdges = true;
                occlusionController.SemanticSuppressionEnabled = false;
                occlusionController.SuppressGroundChannel = false;
                occlusionController.DepthVisualizationEnabled = false;
                occlusionController.RuntimeMeshDebugEnabled = false;
                occlusionController.RouteDepthBiasMeters = ArOcclusionController.DefaultRouteDepthBiasMeters;
            }
            if (ttsService != null)
            {
                ttsService.Enabled = true;
                ttsService.SpeakingRate = GoogleTextToSpeechService.DefaultSpeakingRate;
            }
            ApplyLineSettings();
            SaveUserSettings(false);
        }

        private static string GetQualityModeLabel(ArOcclusionQualityMode mode)
        {
            switch (mode)
            {
                case ArOcclusionQualityMode.Off: return "Tắt";
                case ArOcclusionQualityMode.Fallback: return "Tiết kiệm";
                case ArOcclusionQualityMode.Balanced: return "Cân bằng";
                case ArOcclusionQualityMode.HighQuality: return "Chất lượng cao";
                default: return mode.ToString();
            }
        }

        private void DrawReadinessCard()
        {
            float width = Mathf.Min(560f, safeUiRect.width - 32f);
            const float height = 98f;
            float minimumY = safeUiRect.y + 104f;
            float maximumY = Mathf.Max(minimumY, safeUiRect.yMax - height - 210f);
            float y = Mathf.Clamp(safeUiRect.y + safeUiRect.height * 0.44f, minimumY, maximumY);
            Rect card = new Rect(safeUiRect.center.x - width * 0.5f, y, width, height);
            GUI.Box(card, GUIContent.none, theme.ReadinessMintCardStyle);

            float iconSize = 44f;
            float ix = card.x + 18f;
            float iy = card.y + (card.height - iconSize) * 0.5f;
            GUI.DrawTexture(new Rect(ix, iy, iconSize, iconSize), theme.IconSuccessBadge, ScaleMode.ScaleToFit);

            float textX = ix + iconSize + 16f;
            float textWidth = card.width - (textX - card.x) - 16f;
            GUI.Label(new Rect(textX, card.y + 18f, textWidth, 30f), "Sẵn sàng dẫn đường", theme.LightTitleStyle);
            GUI.Label(new Rect(textX, card.y + 48f, textWidth, 34f), "Chọn một phòng hoặc tạo lộ trình để bắt đầu.", theme.LightCaptionStyle);
        }

        private void DrawDestinationActionDock()
        {
            float width = Mathf.Min(620f, safeUiRect.width - 28f);
            const float height = 168f;
            Rect dock = new Rect(safeUiRect.center.x - width * 0.5f, safeUiRect.yMax - height - 16f, width, height);
            GUI.Box(dock, GUIContent.none, theme.LightDockCardStyle);

            float boxSize = 44f;
            float bx = dock.x + 18f;
            float by = dock.y + 18f;
            if (theme.IconBoxBlue != null)
            {
                GUI.DrawTexture(new Rect(bx, by, boxSize, boxSize), theme.IconBoxBlue, ScaleMode.StretchToFill);
            }
            float pinSize = 24f;
            float px = bx + (boxSize - pinSize) * 0.5f;
            float py = by + (boxSize - pinSize) * 0.5f;
            Color oldColor = GUI.color;
            GUI.color = new Color(0.11f, 0.42f, 0.96f, 1f);
            GUI.DrawTexture(new Rect(px, py, pinSize, pinSize), theme.IconPin, ScaleMode.ScaleToFit);
            GUI.color = oldColor;

            float textX = bx + boxSize + 14f;
            float textWidth = dock.width - (textX - dock.x) - 18f;
            GUI.Label(new Rect(textX, dock.y + 15f, textWidth, 26f), "Bạn muốn đi đâu?", theme.LightTitleStyle);
            GUI.Label(new Rect(textX, dock.y + 41f, textWidth, 22f), "Chọn phòng hoặc điểm đến để bắt đầu.", theme.LightCaptionStyle);

            bool ready = bootstrap != null && bootstrap.State == NavigationBootstrapState.ManifestLoaded && activePois.Count > 0;
            bool loading = bootstrap != null && (bootstrap.State == NavigationBootstrapState.FetchingConfig || bootstrap.State == NavigationBootstrapState.FetchingManifest);
            Rect button = new Rect(dock.x + 18f, dock.y + 86f, dock.width - 36f, 58f);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = !loading && !startPending && bootstrap != null;
            if (theme.DrawPillButton(button, theme.IconNavArrow, "Chọn điểm đến", theme.PrimaryPillButtonStyle, 22f))
            {
                if (ready)
                {
                    panelOpen = true;
                    poiSearchQuery = string.Empty;
                    RefreshPois();
                }
                else if (bootstrap != null)
                {
                    bootstrap.Reload();
                }
            }
            GUI.enabled = oldEnabled;
        }

        private void DrawDestinationPickerModal()
        {
            float modalWidth = Mathf.Min(680f, safeUiRect.width - 24f);
            float modalHeight = Mathf.Min(780f, safeUiRect.height - 24f);
            Rect modalRect = new Rect(safeUiRect.center.x - modalWidth * 0.5f, safeUiRect.center.y - modalHeight * 0.5f, modalWidth, modalHeight);

            Event current = Event.current;
            if (current != null && current.type == EventType.MouseDown && !modalRect.Contains(current.mousePosition))
            {
                GUI.FocusControl(null);
                panelOpen = false;
                current.Use();
                return;
            }

            Color oldColor = GUI.color;
            GUI.color = new Color(0.04f, 0.08f, 0.16f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width / uiScale, Screen.height / uiScale), Texture2D.whiteTexture);
            GUI.color = oldColor;

            GUI.Box(modalRect, GUIContent.none, theme.LightModalCardStyle);

            float padding = 24f;
            float contentWidth = modalRect.width - padding * 2f;
            GUI.DrawTexture(new Rect(modalRect.center.x - 24f, modalRect.y + 12f, 48f, 5f), theme.SheetHandle);
            GUI.Label(new Rect(modalRect.x + padding, modalRect.y + 28f, contentWidth - 56f, 32f), "Chọn điểm đến", theme.LightTitleStyle);
            GUI.Label(new Rect(modalRect.x + padding, modalRect.y + 60f, contentWidth - 56f, 22f), "Tìm phòng, lối đi hoặc địa điểm.", theme.LightCaptionStyle);

            Rect closeRect = new Rect(modalRect.xMax - padding - 40f, modalRect.y + 26f, 40f, 40f);
            if (theme.DrawIconButton(closeRect, theme.IconCloseDark, theme.LightIconButtonStyle, 18f))
            {
                GUI.FocusControl(null);
                panelOpen = false;
            }

            float searchY = modalRect.y + 92f;
            Rect searchRect = new Rect(modalRect.x + padding, searchY, contentWidth, 50f);
            string previousSearchQuery = poiSearchQuery;
            GUI.SetNextControlName("DestinationSearch");
            poiSearchQuery = GUI.TextField(searchRect, poiSearchQuery, theme.LightSearchFieldStyle);
            if (!string.Equals(previousSearchQuery, poiSearchQuery, StringComparison.Ordinal))
                RebuildVisiblePois();
            Color oldSearchIconColor = GUI.color;
            GUI.color = new Color(0.11f, 0.42f, 0.96f, 1f);
            GUI.DrawTexture(new Rect(searchRect.x + 16f, searchRect.y + 14f, 22f, 22f), theme.IconSearch, ScaleMode.ScaleToFit);
            GUI.color = oldSearchIconColor;
            if (string.IsNullOrEmpty(poiSearchQuery))
                GUI.Label(new Rect(searchRect.x + 48f, searchRect.y, searchRect.width - 60f, searchRect.height), "Tìm điểm đến…", theme.LightCaptionStyle);

            int visibleCount = visiblePois.Count;
            float listY = searchRect.yMax + 14f;
            const float footerHeight = 112f;
            float listHeight = Mathf.Max(60f, modalRect.yMax - footerHeight - 14f - listY);
            Rect listRect = new Rect(modalRect.x + padding, listY, contentWidth, listHeight);

            if (visibleCount == 0)
            {
                GUI.Box(listRect, GUIContent.none, theme.LightInsetPanelStyle);
                string emptyTitle = activePois.Count == 0 ? "Chưa có điểm đến" : "Không tìm thấy kết quả";
                string emptyDetail = activePois.Count == 0
                    ? "Sơ đồ hiện chưa có phòng hoặc tiện ích để chọn."
                    : "Thử tìm bằng tên phòng hoặc loại tiện ích khác.";
                GUI.Label(new Rect(listRect.x + 18f, listRect.y + 18f, listRect.width - 36f, 28f), emptyTitle, theme.LightCardTitleStyle);
                GUI.Label(new Rect(listRect.x + 18f, listRect.y + 50f, listRect.width - 36f, 48f), emptyDetail, theme.LightSubtitleStyle);
            }
            else
            {
                const float rowHeight = 74f;
                const float rowGap = 10f;
                float contentHeight = visibleCount * (rowHeight + rowGap);
                Rect viewRect = new Rect(0f, 0f, contentWidth - 18f, contentHeight);
                poiScrollPos = GUI.BeginScrollView(listRect, poiScrollPos, viewRect);
                int rowIndex = 0;
                for (int i = 0; i < visiblePois.Count; i++)
                {
                    PublishedGraphPoiV2 poi = visiblePois[i];

                    bool isSelected = poi.Id == selectedPoiId;
                    Texture2D icon = poi.Category == "room" ? theme.IconDoor : theme.IconPin;
                    Rect itemRect = new Rect(0f, rowIndex * (rowHeight + rowGap), contentWidth - 18f, rowHeight);
                    string label = CapitalizeFirst(poi.Label);
                    if (theme.DrawCardItem(itemRect, icon, label, GetPoiCategoryLabel(poi.Category), isSelected, true))
                    {
                        selectedPoiId = poi.Id;
                        VpsSystemDiagnostics.Record("INPUT", "destination row selected");
                    }
                    rowIndex++;
                }
                GUI.EndScrollView();
            }

            float footerY = modalRect.yMax - footerHeight - 8f;
            PublishedGraphPoiV2 selectedPoi = FindActivePoi(selectedPoiId);
            string selectedLabel = selectedPoi == null ? "Đã chọn: 0" : "Đã chọn: 1";
            GUI.Label(new Rect(modalRect.x + padding, footerY, contentWidth, 24f), selectedLabel, theme.LightCaptionStyle);
            Rect startRect = new Rect(modalRect.x + padding, footerY + 27f, contentWidth, 58f);

            bool oldEnabled = GUI.enabled;
            bool startEnabled = selectedPoi != null && !sitesBusy;
            string startDisabledReason = selectedPoi == null ? "destination missing" : sitesBusy ? "Sites operation busy" : "ready";
            VpsSystemDiagnostics.SetState("Start navigation button", (startEnabled ? "enabled" : "disabled") + " reason=" + startDisabledReason);
            GUI.enabled = startEnabled;
            string startLabel = sitesBusy ? "Đang kết nối với VPS…" : "Bắt đầu dẫn đường";
            if (theme.DrawPillButton(startRect, theme.IconNavArrow, startLabel, theme.PrimaryPillButtonStyle, 22f))
            {
                VpsSystemDiagnostics.Record("INPUT", "button callback: Start navigation");
                GUI.FocusControl(null);
                panelOpen = false;
                StartCoroutine(RefreshAndTrack());
            }
            GUI.enabled = oldEnabled;
        }

        private static string CapitalizeFirst(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length == 1) return text.ToUpper();
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        private bool IsPoiVisible(PublishedGraphPoiV2 poi)
        {
            if (poi == null) return false;
            if (string.IsNullOrEmpty(normalizedPoiSearchQuery)) return true;
            string query = normalizedPoiSearchQuery;
            return (!string.IsNullOrEmpty(poi.Label) && poi.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   GetPoiCategoryLabel(poi.Category).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RebuildVisiblePois()
        {
            normalizedPoiSearchQuery = poiSearchQuery.Trim();
            visiblePois.Clear();
            for (int i = 0; i < activePois.Count; i++)
            {
                PublishedGraphPoiV2 poi = activePois[i];
                if (IsPoiVisible(poi)) visiblePois.Add(poi);
            }
        }

        private static string GetPoiCategoryLabel(string category)
        {
            switch (category)
            {
                case "room": return "Phòng";
                case "service": return "Dịch vụ";
                case "entrance": return "Lối vào";
                case "elevator": return "Thang máy";
                case "stairs": return "Cầu thang";
                case "restroom": return "Nhà vệ sinh";
                default: return "Địa điểm";
            }
        }

        private IEnumerator RefreshAndTrack()
        {
            if (startPending) yield break;
            startPending = true;
            yield return StartCoroutine(RefreshAndTrackCore());
            startPending = false;
        }

        private IEnumerator RefreshAndTrackCore()
        {
            VpsSystemDiagnostics.Record("NAV", "start flow entered");
            if (bootstrap == null)
            {
                VpsSystemDiagnostics.Record("NAV", "start flow stopped: bootstrap missing");
                status = "Scene chưa có bootstrap V2.";
                yield break;
            }
            if (VpsWorkflowCoordinator.IsOwnedByOther(this))
            {
                VpsSystemDiagnostics.Record("NAV", "start flow stopped: another VPS workflow owns the anchor");
                status = "Đang có một quy trình khác sử dụng Site anchor. Hãy dừng quy trình đó trước.";
                yield break;
            }
            bootstrap.Reload();
            VpsSystemDiagnostics.SetState("Navigation start flow", "refreshing public config and manifest");
            float waited = 0f;
            while ((bootstrap.State == NavigationBootstrapState.FetchingConfig || bootstrap.State == NavigationBootstrapState.FetchingManifest) && waited < 45f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (bootstrap.State != NavigationBootstrapState.ManifestLoaded || bootstrap.Manifest == null)
            {
                VpsSystemDiagnostics.Record("NAV", "start flow stopped: manifest unavailable state=" + bootstrap.State);
                status = bootstrap.ErrorMessage ?? "Không có graph V2 đã publish để dẫn đường.";
                yield break;
            }
            PublishedSiteGraphManifest manifest = bootstrap.Manifest;

            sitesBusy = true;
            bool tokenReady = false;
            string tokenError = null;
            string adminBearer = string.Empty;
            yield return nianticAccessTokenProvider.EnsureAccessToken(adminBearer, (success, error) =>
            {
                tokenReady = success;
                tokenError = error;
            });
            if (!tokenReady)
            {
                VpsSystemDiagnostics.Record("NAV", "start flow stopped: Niantic token unavailable");
                sitesBusy = false;
                status = string.IsNullOrWhiteSpace(tokenError)
                    ? "Không lấy được access token Niantic để xác minh Site."
                    : tokenError;
                yield break;
            }

            if (!EnsureArHost())
            {
                VpsSystemDiagnostics.Record("NAV", "start flow stopped: AR host initialization failed");
                sitesBusy = false;
                status = "AR host chưa sẵn sàng. Kiểm tra XR loader và quyền camera.";
                yield break;
            }
            float arWait = 0f;
            while (ARSession.state != ARSessionState.SessionTracking && arWait < 30f)
            {
                arWait += Time.unscaledDeltaTime;
                yield return null;
            }
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                VpsSystemDiagnostics.Record("NAV", "start flow stopped: ARSession timeout state=" + ARSession.state);
                sitesBusy = false;
                status = "AR session không đạt SessionTracking.";
                yield break;
            }

            Task<AssetResult> task;
            float siteQueryStartedAt = Time.realtimeSinceStartup;
            bool siteQuerySlowReported = false;
            VpsSystemDiagnostics.Record("SITES", "navigation asset verification started");
            try { task = sitesClient.GetAssetsForSiteAsync(manifest.SiteBinding.SiteId); }
            catch (Exception exception)
            {
                VpsSystemDiagnostics.Record("SITES", "navigation asset request threw " + exception.GetType().Name);
                sitesBusy = false;
                status = "Không thể xác minh Production VPS asset qua Niantic Sites API.";
                yield break;
            }
            while (task != null && !task.IsCompleted)
            {
                if (!siteQuerySlowReported && Time.realtimeSinceStartup - siteQueryStartedAt >= 10f)
                {
                    siteQuerySlowReported = true;
                    VpsSystemDiagnostics.Record("SITES", "navigation asset request still pending after 10000ms");
                    VpsSystemDiagnostics.SetState("Navigation Sites request", "pending > 10s");
                }
                yield return null;
            }
            sitesBusy = false;
            if (task == null || task.IsFaulted || task.IsCanceled || task.Result.Status != SitesRequestStatus.Success)
            {
                VpsSystemDiagnostics.Record("SITES", "navigation asset verification failed taskStatus=" +
                    (task == null ? "missing" : task.Status.ToString()) + " requestStatus=" +
                    (task == null || task.IsFaulted || task.IsCanceled ? "unavailable" : task.Result.Status + " error=" + task.Result.Error));
                status = "Niantic Sites API không xác nhận được Site/asset đã publish.";
                yield break;
            }
            VpsSystemDiagnostics.Record("SITES", "navigation asset verification succeeded returned=" +
                (task.Result.Assets == null ? 0 : task.Result.Assets.Length) + " elapsedMs=" +
                Mathf.RoundToInt((Time.realtimeSinceStartup - siteQueryStartedAt) * 1000f));
            VpsSystemDiagnostics.SetState("Navigation Sites request", "success");
            AssetInfo selectedAsset = default;
            bool found = false;
            foreach (AssetInfo asset in task.Result.Assets ?? Array.Empty<AssetInfo>())
            {
                if (!ProductionVpsAssetFilter.IsEligible(asset, manifest.SiteBinding.SiteId) ||
                    !string.Equals(asset.Id, manifest.SiteBinding.ProductionAssetId, StringComparison.Ordinal)) continue;
                selectedAsset = asset;
                found = true;
                break;
            }
            if (!found)
            {
                VpsSystemDiagnostics.Record("SITES", "bound Production VPS asset not found in current Site result");
                status = "Site hiện không còn Production VPS asset đang hoạt động như phiên bản đã publish.";
                yield break;
            }
            string payload = selectedAsset.VpsData.Value.AnchorPayload;
            string fingerprint = Fingerprint(payload);
            if (!SiteGraphV2NavigationGate.BindingMatches(manifest, selectedAsset.SiteId, selectedAsset.Id, fingerprint))
            {
                VpsSystemDiagnostics.Record("NAV", "published Site/asset/payload binding mismatch");
                status = "Site/asset/payload hiện tại không khớp với graph đã publish.";
                yield break;
            }
            if (!VpsWorkflowCoordinator.TryAcquire(this))
            {
                VpsSystemDiagnostics.Record("VPS", "navigation tracking rejected: another workflow owns the anchor");
                status = "Một quy trình khác đã chiếm Site anchor.";
                yield break;
            }
            if (localizationAdapter == null || !localizationAdapter.TryTrackAnchor(payload, out trackedAnchor) || trackedAnchor == null)
            {
                VpsSystemDiagnostics.Record("VPS", "navigation tracking request rejected by adapter/NSDK");
                VpsWorkflowCoordinator.Release(this);
                status = "NSDK không nhận yêu cầu VPS2 tracking cho Site này.";
                yield break;
            }
            bindingVerified = true;
            trackingRequestAccepted = true;
            VpsSystemDiagnostics.SetState("Navigation start flow", "VPS2 request accepted; waiting for stable Tracking");
            VpsSystemDiagnostics.Record("NAV", "VPS2 tracking request accepted");
            stableTracking = false;
            trackedSince = 0d;
            status = "Yêu cầu VPS2 đã được nhận; hãy lia camera quanh phòng để chốt vị trí.";
        }

        private void UpdateRoute()
        {
            PublishedSiteGraphManifest manifest = bootstrap == null ? null : bootstrap.Manifest;
            PublishedGraphPoiV2 destination = FindActivePoi(selectedPoiId);
            if (!SiteGraphV2NavigationGate.CanRender(stableTracking, bindingVerified, destination != null) ||
                manifest == null || trackedAnchor == null || arCamera == null)
            {
                SetRouteVisible(false);
                return;
            }
            Vector3 localCamera = trackedAnchor.InverseTransformPoint(arCamera.transform.position);
            if (companionGuide != null && companionGuide.IsAtDestination &&
                string.Equals(activeRoutedPoiId, destination.Id, StringComparison.Ordinal) &&
                destination.AnchorLocal != null)
            {
                float distToDest2D = Vector2.Distance(
                    new Vector2(localCamera.x, localCamera.z),
                    new Vector2(destination.AnchorLocal.X, destination.AnchorLocal.Z));
                if (distToDest2D <= DoraemonCompanionGuide.DestinationUnlockDistanceM)
                {
                    companionGuide.SetVisible(true);
                    if (routeLine != null && currentRoutePoints.Length >= 2) routeLine.enabled = true;
                    return;
                }
            }
            SiteGraphV2Route route;
            string error;
            if (!SiteGraphV2Pathfinder.TryFindRoute(manifest, localCamera, destination.NodeId, MaximumSnapDistanceM, out route, out error))
            {
                SetRouteVisible(false);
                status = "Không thể tạo tuyến từ vị trí hiện tại: " + error;
                return;
            }
            currentRoutePoints = route.AnchorLocalPoints;
            activeRoutedPoiId = destination.Id;
            if (!EnsureRouteRenderer())
            {
                SetRouteVisible(false);
                status = "Không thể tạo LineRenderer cho tuyến AR.";
                return;
            }
            if (companionGuide != null)
            {
                companionGuide.Initialize(trackedAnchor, arCamera);
                companionGuide.SetRoute(currentRoutePoints);
                // The ribbon is drawn on the raw route plane (the real floor level), not on the
                // companion's ground-dropped path: dropping the ribbon by the companion sink made it
                // sink into the floor, and the old 18 cm vertex pull pushed it back up so the line
                // floated ~10 cm above the floor and punched through low obstacles.
                RenderRoutePositions(currentRoutePoints);
                companionGuide.SetVisible(true);
                if (occlusionController != null)
                {
                    occlusionController.SetPrincipalOccludee(companionGuide.PrimaryRenderer);
                }
            }
            else
            {
                RenderRoutePositions(currentRoutePoints);
            }
            routeLine.enabled = true;
            status = "Đang dẫn tới " + destination.Label + " - " + route.EdgeIds.Length + " cạnh - " + route.WeightedCost.ToString("0.0", CultureInfo.InvariantCulture) + " m chi phí.";
        }

        private bool EnsureArHost()
        {
            try
            {
                arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
                if (arSession == null) arSession = new GameObject("AR Session").AddComponent<ARSession>();
                arInputManager = arSession.GetComponent<ARInputManager>();
                if (arInputManager == null) arInputManager = arSession.gameObject.AddComponent<ARInputManager>();
                xrOrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
                if (xrOrigin == null)
                {
                    GameObject originObject = new GameObject("XR Origin");
                    originObject.SetActive(false);
                    xrOrigin = originObject.AddComponent<XROrigin>();
                    GameObject offset = new GameObject("Camera Floor Offset");
                    offset.transform.SetParent(originObject.transform, false);
                    xrOrigin.CameraFloorOffsetObject = offset;
                }
                arCamera = xrOrigin.Camera;
                if (arCamera == null)
                {
                    Transform parent = xrOrigin.CameraFloorOffsetObject != null ? xrOrigin.CameraFloorOffsetObject.transform : xrOrigin.transform;
                    GameObject cameraObject = new GameObject("AR Camera");
                    cameraObject.transform.SetParent(parent, false);
                    cameraObject.tag = "MainCamera";
                    arCamera = cameraObject.AddComponent<Camera>();
                    xrOrigin.Camera = arCamera;
                }
                arCamera.clearFlags = CameraClearFlags.Color;
                arCamera.gameObject.SetActive(true);
                arCamera.enabled = true;
                arCamera.backgroundColor = Color.black;
                arCamera.nearClipPlane = 0.1f;
                arCamera.farClipPlane = 50f;

                var camMgr = arCamera.GetComponent<ARCameraManager>();
                if (camMgr == null) camMgr = arCamera.gameObject.AddComponent<ARCameraManager>();
                camMgr.enabled = true;

                var camBg = arCamera.GetComponent<ARCameraBackground>();
                if (camBg == null) camBg = arCamera.gameObject.AddComponent<ARCameraBackground>();
                camBg.enabled = true;

                foreach (var extraCam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (extraCam != arCamera)
                    {
                        extraCam.enabled = false;
                    }
                }
                AddTrackedPoseDriver(arCamera);

                if (occlusionController == null)
                {
                    occlusionController = GetComponent<ArOcclusionController>();
                    if (occlusionController == null) occlusionController = gameObject.AddComponent<ArOcclusionController>();
                }
                occlusionController.Initialize(arCamera, xrOrigin);
                vps2Manager = FindFirstObjectByType<ARVps2Manager>();
                if (vps2Manager == null) vps2Manager = xrOrigin.gameObject.AddComponent<ARVps2Manager>();
                localizationAdapter = vps2Manager.GetComponent<NsdkVpsLocalizationAdapter>();
                if (localizationAdapter == null) localizationAdapter = vps2Manager.gameObject.AddComponent<NsdkVpsLocalizationAdapter>();
                xrOrigin.gameObject.SetActive(true);
                arSession.gameObject.SetActive(true);
                arSession.enabled = true;
                arInputManager.enabled = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void AddTrackedPoseDriver(Camera camera)
        {
            if (camera.GetComponent<TrackedPoseDriver>() != null) return;
            TrackedPoseDriver driver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            var position = new InputAction("AR Camera Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            position.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotation = new InputAction("AR Camera Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);
        }

        private void ApplyLineSettings()
        {
            if (routeLine == null) return;
            routeLine.widthMultiplier = routeLineWidth;
            routeLine.textureMode = LineTextureMode.Tile;
            float tileUnitMeters = Mathf.Clamp(routeLineWidth, 0.14f, 0.45f);
            routeLine.textureScale = new Vector2(1.0f / tileUnitMeters, 1.0f);
            routeLine.startColor = Color.white;
            routeLine.endColor = Color.white;
            if (lineFlatOnGround)
            {
                routeLine.alignment = LineAlignment.TransformZ;
                routeLine.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            }
            else
            {
                routeLine.alignment = LineAlignment.View;
            }

            Material material = routeLine.sharedMaterial;
            if (material != null && material.HasProperty("_Color"))
                material.SetColor("_Color", routeLineColor);
            if (occlusionController != null && material != null)
                occlusionController.ApplyRouteMaterialDepthSettings(material);
        }

        public static Vector3[] BuildSmoothRenderRoutePoints(Vector3[] rawPoints)
        {
            if (rawPoints == null || rawPoints.Length < 3)
            {
                return rawPoints ?? Array.Empty<Vector3>();
            }

            var smoothed = new List<Vector3>(rawPoints.Length * 4);
            AppendUniqueRoutePoint(smoothed, rawPoints[0]);

            const int cornerSteps = 4;
            for (int i = 1; i < rawPoints.Length - 1; i++)
            {
                Vector3 prev = rawPoints[i - 1];
                Vector3 curr = rawPoints[i];
                Vector3 next = rawPoints[i + 1];

                Vector3 segIn = curr - prev;
                Vector3 segOut = next - curr;
                float lenIn = segIn.magnitude;
                float lenOut = segOut.magnitude;
                if (lenIn < 0.15f || lenOut < 0.15f)
                {
                    AppendUniqueRoutePoint(smoothed, curr);
                    continue;
                }

                Vector3 dIn = segIn / lenIn;
                Vector3 dOut = segOut / lenOut;
                float dot = Vector3.Dot(dIn, dOut);
                // Fillet noticeable turns (dot < ~13 deg) without folding sharp hairpins
                if (dot >= 0.975f || dot <= -0.95f)
                {
                    AppendUniqueRoutePoint(smoothed, curr);
                    continue;
                }

                float cut = Mathf.Min(0.45f, lenIn * 0.35f, lenOut * 0.35f);
                Vector3 arcStart = curr - dIn * cut;
                Vector3 arcEnd = curr + dOut * cut;
                for (int step = 0; step <= cornerSteps; step++)
                {
                    float t = step / (float)cornerSteps;
                    float inv = 1f - t;
                    Vector3 arcPt = (inv * inv) * arcStart + (2f * inv * t) * curr + (t * t) * arcEnd;
                    AppendUniqueRoutePoint(smoothed, arcPt);
                }
            }

            AppendUniqueRoutePoint(smoothed, rawPoints[rawPoints.Length - 1]);
            return smoothed.ToArray();
        }

        private static void AppendUniqueRoutePoint(List<Vector3> points, Vector3 candidate)
        {
            if (points.Count == 0 || (points[points.Count - 1] - candidate).sqrMagnitude > 0.000025f)
            {
                points.Add(candidate);
            }
        }

        private void RenderRoutePositions(Vector3[] anchorLocalPoints)
        {
            if (routeLine == null || anchorLocalPoints == null || anchorLocalPoints.Length == 0) return;
            ApplyLineSettings();
            Vector3[] renderPoints = BuildSmoothRenderRoutePoints(anchorLocalPoints);
            float routeLengthMeters = 0f;
            for (int i = 1; i < renderPoints.Length; i++)
            {
                routeLengthMeters += Vector3.Distance(renderPoints[i - 1], renderPoints[i]);
            }
            Material mat = routeLine.sharedMaterial;
            if (mat != null && mat.HasProperty("_TotalUvLength"))
            {
                mat.SetFloat("_TotalUvLength", routeLengthMeters * routeLine.textureScale.x);
            }
            if (routeLine.useWorldSpace && trackedAnchor != null)
            {
                Vector3[] worldPoints = new Vector3[renderPoints.Length];
                for (int i = 0; i < renderPoints.Length; i++)
                {
                    worldPoints[i] = trackedAnchor.TransformPoint(renderPoints[i]);
                }
                routeLine.positionCount = worldPoints.Length;
                routeLine.SetPositions(worldPoints);
            }
            else
            {
                routeLine.positionCount = renderPoints.Length;
                routeLine.SetPositions(renderPoints);
            }
        }

        private bool EnsureRouteRenderer()
        {
            if (trackedAnchor == null) return false;
            if (routeLine == null)
            {
                GameObject routeObject = new GameObject("Site Graph V2 Route");
                routeLine = routeObject.AddComponent<LineRenderer>();
                routeLine.useWorldSpace = true;
                routeLine.loop = false;
                routeLine.textureMode = LineTextureMode.Tile;
                routeLine.textureScale = Vector2.one;
                routeLine.numCornerVertices = 4;
                routeLine.numCapVertices = 4;
                routeLine.startColor = Color.white;
                routeLine.endColor = Color.white;
                ApplyLineSettings();
            }
            if (routeLine.sharedMaterial == null || routeLine.sharedMaterial.shader == null || routeLine.sharedMaterial.shader.name != "Vps/ARRouteLine")
            {
                ownedMaterial = CreateRouteMaterial(routeLineColor);
                if (ownedMaterial == null) return false;
                routeLine.sharedMaterial = ownedMaterial;
            }
            ApplyLineSettings();
            return true;
        }

        private Material CreateRouteMaterial(Color color)
        {
            Shader shader = Shader.Find("Vps/ARRouteLine");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            var material = new Material(shader) { color = color };
            if (occlusionController != null)
            {
                occlusionController.ApplyRouteMaterialDepthSettings(material);
            }
            return material;
        }

        private void StopNavigation()
        {
            VpsSystemDiagnostics.Record("NAV", "StopNavigation called; wasTracking=" + trackingRequestAccepted);
            bool wasTracking = trackingRequestAccepted;
            trackingRequestAccepted = false;
            stableTracking = false;
            bindingVerified = false;
            trackedSince = 0d;
            authoringMode = false;
            isRecordingRoute = false;
            recordedFloorPoints.Clear();
            hasWalkStartCamY = false;
            hasPendingPoiLocation = false;
            pendingWalkSnapshot = Array.Empty<Vector3>();
            currentAuthoringSnapDistance = float.MaxValue;
            if (wasTracking && localizationAdapter != null) localizationAdapter.StopTracking();
            activeRoutedPoiId = null;
            lastAutoSpokenArrivalKey = null;
            ttsService?.Stop();
            currentRoutePoints = Array.Empty<Vector3>();
            trackedAnchor = null;
            SetRouteVisible(false);
            SetAuthoringPreviewVisible(false);
            if (companionGuide != null)
            {
                companionGuide.ResetNavigationState();
                companionGuide.SetVisible(false);
            }
            if (occlusionController != null)
            {
                occlusionController.SetPrincipalOccludee(null);
                occlusionController.ClearStabilizationMeshes();
            }
            VpsWorkflowCoordinator.Release(this);
            IsAnyNavigating = false;
            if (nianticAccessTokenProvider != null) nianticAccessTokenProvider.Logout();
            if (bootstrap != null && bootstrap.Manifest != null) status = "Đã dừng dẫn đường.";
        }

        private void UpdateAuthoringSession()
        {
            if (bootstrap == null) bootstrap = GetComponent<NavigationBootstrapController>();
            if (bootstrap == null || bootstrap.Manifest == null || trackedAnchor == null || arCamera == null) return;

            if (TryGetCurrentAuthoringFloorPoint(out Vector3 floorPoint))
            {
                currentAuthoringFloorPoint = floorPoint;
                GraphSnapTarget snap = LocalSiteGraphStore.FindNearestGraphConnection(bootstrap.Manifest, floorPoint);
                currentAuthoringSnapDistance = snap.IsValid ? snap.Distance : float.MaxValue;

                if (isRecordingRoute)
                {
                    if (recordedFloorPoints.Count == 0 ||
                        Vector3.Distance(recordedFloorPoints[recordedFloorPoints.Count - 1], floorPoint) >= AuthoringWalkSpacingMeters)
                    {
                        recordedFloorPoints.Add(floorPoint);
                    }
                    status = "Đang ghi lộ trình: đi bộ tới điểm đến mới rồi bấm Đặt điểm tại đây.";
                }
                else if (snap.IsValid && snap.Distance > 2.5f)
                {
                    status = string.Format(CultureInfo.InvariantCulture,
                        "Đang cách tuyến có sẵn {0:0.0} m — nên lại gần tuyến trước khi bấm Bắt đầu ghi đường.",
                        snap.Distance);
                }
                else
                {
                    status = "Sẵn sàng: bấm Bắt đầu ghi đường rồi đi tới điểm mới, hoặc Đặt điểm tại đây.";
                }
            }

            UpdateAuthoringPreviewRenderers();
        }

        private bool TryGetCurrentAuthoringFloorPoint(out Vector3 floorPoint)
        {
            floorPoint = Vector3.zero;
            if (trackedAnchor == null || arCamera == null || bootstrap == null || bootstrap.Manifest == null)
                return false;

            Vector3 localCam = trackedAnchor.InverseTransformPoint(arCamera.transform.position);
            float baseFloorY = LocalSiteGraphStore.EstimateFloorHeight(bootstrap.Manifest, localCam);
            float floorY = baseFloorY;

            if (isRecordingRoute && hasWalkStartCamY)
            {
                float deltaY = localCam.y - walkStartCamY;
                floorY = Mathf.Abs(deltaY) > 0.55f ? walkStartFloorY + deltaY : walkStartFloorY;
            }

            floorPoint = new Vector3(localCam.x, floorY, localCam.z);
            return true;
        }

        private string BuildAuthoringConnectionSummary()
        {
            if (bootstrap == null || bootstrap.Manifest == null)
                return "Chưa có dữ liệu bản đồ.";

            Vector3 probe = pendingWalkSnapshot != null && pendingWalkSnapshot.Length > 0
                ? pendingWalkSnapshot[0]
                : (hasPendingPoiLocation ? pendingPoiFloorPoint : currentAuthoringFloorPoint);
            GraphSnapTarget snap = LocalSiteGraphStore.FindNearestGraphConnection(bootstrap.Manifest, probe);
            string targetDesc = DescribeSnapTarget(bootstrap.Manifest, snap);

            if (isRecordingRoute && pendingWalkSnapshot != null && pendingWalkSnapshot.Length > 0)
            {
                float len = ComputeRecordedWalkLength();
                return string.Format(CultureInfo.InvariantCulture,
                    "Tuyến đi bộ {0:0.0} m ({1} điểm), nối từ {2}.",
                    len, pendingWalkSnapshot.Length, targetDesc);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "Nối thẳng từ vị trí hiện tại tới {0} (cách {1:0.0} m).",
                targetDesc, snap.IsValid ? snap.Distance : 0f);
        }

        private static string DescribeSnapTarget(PublishedSiteGraphManifest manifest, GraphSnapTarget snap)
        {
            if (!snap.IsValid) return "đồ thị hiện tại";
            if (!string.IsNullOrEmpty(snap.NodeId) && manifest.Graph != null && manifest.Graph.Pois != null)
            {
                for (int i = 0; i < manifest.Graph.Pois.Length; i++)
                {
                    PublishedGraphPoiV2 poi = manifest.Graph.Pois[i];
                    if (poi != null && poi.IsActive && string.Equals(poi.NodeId, snap.NodeId, StringComparison.Ordinal))
                    {
                        return $"'{CapitalizeFirst(poi.Label)}'";
                    }
                }
                return "nút giao trên tuyến";
            }
            return "điểm nối trên đoạn đường gần nhất";
        }

        private float ComputeRecordedWalkLength()
        {
            if (recordedFloorPoints.Count < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < recordedFloorPoints.Count; i++)
            {
                total += Vector3.Distance(recordedFloorPoints[i - 1], recordedFloorPoints[i]);
            }
            return total;
        }

        private void UpdateAuthoringPreviewRenderers()
        {
            if (!authoringMode || !stableTracking || trackedAnchor == null || bootstrap == null || bootstrap.Manifest == null)
            {
                SetAuthoringPreviewVisible(false);
                return;
            }

            EnsureAuthoringMaterials();

            PublishedGraphEdgeV2[] edges = bootstrap.Manifest.Graph != null && bootstrap.Manifest.Graph.Edges != null
                ? bootstrap.Manifest.Graph.Edges
                : Array.Empty<PublishedGraphEdgeV2>();

            int activeLineIndex = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                PublishedGraphEdgeV2 edge = edges[i];
                if (edge == null || !edge.Enabled || edge.Geometry == null || edge.Geometry.Length < 2) continue;

                LineRenderer lr = GetOrCreateAuthoringEdgeLine(activeLineIndex++);
                lr.positionCount = edge.Geometry.Length;
                for (int p = 0; p < edge.Geometry.Length; p++)
                {
                    lr.SetPosition(p, trackedAnchor.TransformPoint(edge.Geometry[p].ToVector3()));
                }
                lr.enabled = true;
            }

            for (int i = activeLineIndex; i < authoringExistingEdgeLines.Count; i++)
            {
                if (authoringExistingEdgeLines[i] != null) authoringExistingEdgeLines[i].enabled = false;
            }

            if (isRecordingRoute && recordedFloorPoints.Count > 0)
            {
                if (authoringWalkTrailLine == null)
                {
                    authoringWalkTrailLine = CreateAuthoringLineRenderer("Authoring Walk Trail", 0.14f, authoringWalkMaterial);
                }

                GraphSnapTarget snap = LocalSiteGraphStore.FindNearestGraphConnection(bootstrap.Manifest, recordedFloorPoints[0]);
                var previewPts = new List<Vector3>(recordedFloorPoints.Count + 2);
                if (snap.IsValid) previewPts.Add(snap.SnapPoint);
                for (int i = 0; i < recordedFloorPoints.Count; i++) previewPts.Add(recordedFloorPoints[i]);
                if (Vector3.Distance(previewPts[previewPts.Count - 1], currentAuthoringFloorPoint) > 0.05f)
                {
                    previewPts.Add(currentAuthoringFloorPoint);
                }

                authoringWalkTrailLine.positionCount = previewPts.Count;
                for (int i = 0; i < previewPts.Count; i++)
                {
                    authoringWalkTrailLine.SetPosition(i, trackedAnchor.TransformPoint(previewPts[i]));
                }
                authoringWalkTrailLine.enabled = previewPts.Count >= 2;
            }
            else if (authoringWalkTrailLine != null)
            {
                authoringWalkTrailLine.enabled = false;
            }
        }

        private LineRenderer GetOrCreateAuthoringEdgeLine(int index)
        {
            while (authoringExistingEdgeLines.Count <= index)
            {
                authoringExistingEdgeLines.Add(
                    CreateAuthoringLineRenderer("Authoring Existing Edge " + authoringExistingEdgeLines.Count, 0.08f, authoringExistingMaterial));
            }
            return authoringExistingEdgeLines[index];
        }

        private LineRenderer CreateAuthoringLineRenderer(string objectName, float width, Material mat)
        {
            var go = new GameObject(objectName);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = false;
            lr.numCornerVertices = 4;
            lr.numCapVertices = 4;
            lr.widthMultiplier = width;
            lr.startColor = Color.white;
            lr.endColor = Color.white;
            lr.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            if (mat != null) lr.sharedMaterial = mat;
            return lr;
        }

        private void EnsureAuthoringMaterials()
        {
            if (authoringExistingMaterial == null)
            {
                authoringExistingMaterial = CreateRouteMaterial(new Color(0.22f, 0.74f, 0.97f, 0.65f));
            }
            if (authoringWalkMaterial == null)
            {
                authoringWalkMaterial = CreateRouteMaterial(new Color(0.06f, 0.85f, 0.50f, 0.95f));
            }
        }

        private void SetAuthoringPreviewVisible(bool visible)
        {
            for (int i = 0; i < authoringExistingEdgeLines.Count; i++)
            {
                if (authoringExistingEdgeLines[i] != null) authoringExistingEdgeLines[i].enabled = visible;
            }
            if (authoringWalkTrailLine != null) authoringWalkTrailLine.enabled = visible && isRecordingRoute;
        }

        private void DestroyAuthoringPreviewRenderers()
        {
            for (int i = 0; i < authoringExistingEdgeLines.Count; i++)
            {
                if (authoringExistingEdgeLines[i] != null) Destroy(authoringExistingEdgeLines[i].gameObject);
            }
            authoringExistingEdgeLines.Clear();
            if (authoringWalkTrailLine != null)
            {
                Destroy(authoringWalkTrailLine.gameObject);
                authoringWalkTrailLine = null;
            }
            if (authoringExistingMaterial != null)
            {
                Destroy(authoringExistingMaterial);
                authoringExistingMaterial = null;
            }
            if (authoringWalkMaterial != null)
            {
                Destroy(authoringWalkMaterial);
                authoringWalkMaterial = null;
            }
        }

        private void RefreshPois()
        {
            activePois.Clear();
            if (bootstrap != null && bootstrap.Manifest != null && bootstrap.Manifest.Graph != null && bootstrap.Manifest.Graph.Pois != null)
            {
                foreach (PublishedGraphPoiV2 poi in bootstrap.Manifest.Graph.Pois)
                    if (poi != null && poi.IsActive) activePois.Add(poi);
            }
            if (activePois.Count > 0 && FindActivePoi(selectedPoiId) == null)
                selectedPoiId = activePois[0].Id;
            RebuildVisiblePois();
            uiToolkitController?.PopulatePois(visiblePois, selectedPoiId);
        }

        private PublishedGraphPoiV2 FindActivePoi(string poiId)
        {
            if (string.IsNullOrEmpty(poiId)) return null;
            for (int i = 0; i < activePois.Count; i++)
            {
                PublishedGraphPoiV2 poi = activePois[i];
                if (poi != null && string.Equals(poi.Id, poiId, StringComparison.Ordinal)) return poi;
            }
            return null;
        }

        private void HandleBootstrapState(NavigationBootstrapState state)
        {
            if (state == NavigationBootstrapState.ManifestLoaded) RefreshPois();
            else if (state == NavigationBootstrapState.Error || state == NavigationBootstrapState.NoActiveMap)
            {
                SetRouteVisible(false);
                if (trackingRequestAccepted) StopNavigation();
            }
        }

        private void SetRouteVisible(bool visible)
        {
            if (routeLine != null) routeLine.enabled = visible;
            if (companionGuide != null) companionGuide.SetVisible(visible);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveUserSettings(true);
                ttsService?.Stop();
                SetRouteVisible(false);
                SetAuthoringPreviewVisible(false);
                trackedSince = 0d;
                stableTracking = false;
            }
        }

        private void OnDestroy()
        {
            StopNavigation();
            DestroyAuthoringPreviewRenderers();
            if (ownedMaterial != null) Destroy(ownedMaterial);
        }

        private static string Fingerprint(string payload)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }
}
