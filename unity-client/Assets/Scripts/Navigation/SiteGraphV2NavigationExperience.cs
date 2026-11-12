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
            LoadUserSettings();
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
            IsAnyNavigating = trackingRequestAccepted || panelOpen || settingsOpen;
            VpsSystemDiagnostics.SetState("Navigation UI", "destinationPicker=" + panelOpen + " settings=" + settingsOpen +
                " busy=" + sitesBusy + " startPending=" + startPending + " trackingAccepted=" + trackingRequestAccepted +
                " stable=" + stableTracking);
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
                status = "VPS2 Tracking bị gián đoạn; tuyến đã ẩn. Hãy lia camera quanh phòng để nhận diện lại.";
                return;
            }
            if (trackedSince <= 0d) trackedSince = Time.realtimeSinceStartupAsDouble;
            bool wasStable = stableTracking;
            stableTracking = Time.realtimeSinceStartupAsDouble - trackedSince >= StableTrackingSeconds;
            if (!stableTracking)
            {
                SetRouteVisible(false);
                status = "Đang dò không gian... Hãy lia camera quanh phòng để chốt vị trí.";
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
        }

        private readonly NavigationUiTheme theme = new NavigationUiTheme();
        private Vector2 poiScrollPos;
        private string normalizedPoiSearchQuery = string.Empty;

        private void OnGUI()
        {
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
            GUI.Label(new Rect(companionCard.x + 78f, companionCard.y + 18f, companionCard.width - 190f, 22f), "ROBOT DẪN ĐƯỜNG", theme.LightEyebrowStyle);
            string companionMessage = companionGuide != null ? companionGuide.CompanionStatusMessage : "Đi theo Robot và đường sáng trên sàn.";
            GUI.Label(new Rect(companionCard.x + 78f, companionCard.y + 42f, companionCard.width - 190f, 50f), companionMessage, theme.LightBodyStyle);
            Rect endNavigation = new Rect(companionCard.xMax - 106f, companionCard.y + 25f, 90f, 50f);
            if (theme.DrawPillButton(endNavigation, null, "Kết thúc", theme.DangerPillButtonStyle, 0f))
                StopNavigation();
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
                companionGuide.LeadDistance = PlayerPrefs.GetFloat(SettingsPrefix + "LeadDistance", DoraemonCompanionGuide.DefaultLeadDistance);
                companionGuide.GroundDropMeters = PlayerPrefs.GetFloat(SettingsPrefix + "GroundDrop", DoraemonCompanionGuide.DefaultGroundDrop);
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
                occlusionController.RouteDepthBiasMeters = PlayerPrefs.GetFloat(SettingsPrefix + "RouteDepthBias", ArOcclusionController.DefaultRouteDepthBiasMeters);
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
            Rect contentRect = new Rect(0f, 0f, contentWidth, 1240f);
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
            y += 62f;

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
            y += 62f;

            newValue = DrawSettingsSlider(contentWidth, y, "Độ mờ đường", routeLineOpacity, 0.25f, 1f, "0%");
            if (!Mathf.Approximately(newValue, routeLineOpacity))
            {
                routeLineOpacity = newValue;
                routeLineColor = RouteColorOptions[routeColorIndex];
                routeLineColor.a = routeLineOpacity;
                ApplyLineSettings();
                SaveUserSettings(false);
            }
            y += 62f;

            float depthBias = occlusionController != null ? occlusionController.RouteDepthBiasMeters : ArOcclusionController.DefaultRouteDepthBiasMeters;
            newValue = DrawSettingsSlider(contentWidth, y, "Bù độ sâu tuyến", depthBias, 0f, 0.35f, "0.00 m");
            if (!Mathf.Approximately(newValue, depthBias) && occlusionController != null)
            {
                occlusionController.RouteDepthBiasMeters = newValue;
                ApplyLineSettings();
                SaveUserSettings(false);
            }
            y += 62f;

            DrawSettingsSection(ref y, contentWidth, "MODEL DẪN ĐƯỜNG");
            float modelScale = companionGuide != null ? companionGuide.ModelScaleMultiplier : 1f;
            newValue = DrawSettingsSlider(contentWidth, y, "Kích thước model", modelScale, 0.5f, 2f, "0.00×");
            if (!Mathf.Approximately(newValue, modelScale) && companionGuide != null)
            {
                companionGuide.ModelScaleMultiplier = newValue;
                SaveUserSettings(false);
            }
            y += 62f;

            float modelBrightness = companionGuide != null ? companionGuide.ModelBrightness : DoraemonCompanionGuide.DefaultModelBrightness;
            newValue = DrawSettingsSlider(contentWidth, y, "Độ sáng model", modelBrightness, 0f, 2f, "0.00");
            if (!Mathf.Approximately(newValue, modelBrightness) && companionGuide != null)
            {
                companionGuide.ModelBrightness = newValue;
                SaveUserSettings(false);
            }
            y += 62f;

            float leadDistance = companionGuide != null ? companionGuide.LeadDistance : DoraemonCompanionGuide.DefaultLeadDistance;
            newValue = DrawSettingsSlider(contentWidth, y, "Cự ly dẫn trước", leadDistance, 0.8f, 3f, "0.00 m");
            if (!Mathf.Approximately(newValue, leadDistance) && companionGuide != null)
            {
                companionGuide.LeadDistance = newValue;
                SaveUserSettings(false);
            }
            y += 62f;

            float groundDrop = companionGuide != null ? companionGuide.GroundDropMeters : DoraemonCompanionGuide.DefaultGroundDrop;
            newValue = DrawSettingsSlider(contentWidth, y, "Độ hạ model", groundDrop, 0f, 0.4f, "0.00 m");
            if (!Mathf.Approximately(newValue, groundDrop) && companionGuide != null)
            {
                companionGuide.GroundDropMeters = newValue;
                SaveUserSettings(false);
            }
            y += 62f;

            DrawSettingsSection(ref y, contentWidth, "DEPTH OCCLUSION");
            if (occlusionController != null)
            {
                DrawOcclusionModeChoice(contentWidth, ref y, ArOcclusionQualityMode.Off, "Tắt", "Không che model sau vật thể thật");
                DrawOcclusionModeChoice(contentWidth, ref y, ArOcclusionQualityMode.Fallback, "Tiết kiệm", "Depth cơ bản, giảm xử lý NSDK");
                DrawOcclusionModeChoice(contentWidth, ref y, ArOcclusionQualityMode.Balanced, "Cân bằng", "Khuyến nghị cho điện thoại và tablet");
                DrawOcclusionModeChoice(contentWidth, ref y, ArOcclusionQualityMode.HighQuality, "Chất lượng cao", "Thêm ổn định bằng lưới, tốn tài nguyên hơn");

                DrawSettingsToggle(contentWidth, ref y, "Làm mượt viền depth", "Giảm răng cưa quanh vật thể bị che", OcclusionToggle.SmoothEdges);
                DrawSettingsToggle(contentWidth, ref y, "Lọc vùng trời", "Loại bỏ vùng sky khỏi depth suppression", OcclusionToggle.SemanticSuppression);
                DrawSettingsToggle(contentWidth, ref y, "Lọc mặt đất", "Có thể giảm che khuất nhầm trên sàn", OcclusionToggle.SuppressGround);
                DrawSettingsToggle(contentWidth, ref y, "Hiện hình ảnh depth", "Chỉ hỗ trợ trong chế độ Cân bằng / Cao", OcclusionToggle.DepthVisualization);
                DrawSettingsToggle(contentWidth, ref y, "Hiện lưới debug", "Có dữ liệu lưới trong chế độ Chất lượng cao", OcclusionToggle.MeshDebug);

                string performance = string.Format(CultureInfo.InvariantCulture, "{0:0} FPS · {1:0} ms · {2:0} MB",
                    occlusionController.CurrentFps, occlusionController.CurrentFrameTimeMs, occlusionController.AllocatedMemoryMb);
                GUI.Label(new Rect(8f, y + 4f, contentWidth - 16f, 20f), performance, theme.LightCardTitleStyle);
                string depthState = "Depth " + (occlusionController.IsDepthSubsystemAvailable ? "sẵn sàng" : "chưa khả dụng") +
                    " · Đang dùng: " + GetQualityModeLabel(occlusionController.EffectiveMode) + "\n" + occlusionController.ActiveSubsystemSummary;
                if (!string.IsNullOrEmpty(occlusionController.LastWarningOrError))
                    depthState += "\n" + occlusionController.LastWarningOrError;
                GUI.Label(new Rect(8f, y + 25f, contentWidth - 16f, 48f), depthState, theme.LightCaptionStyle);
                y += 78f;
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
                new Rect(8f, y + 26f, width - 16f, 32f),
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
                RenderRoutePositions(companionGuide.GroundPoints);
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

        private void RenderRoutePositions(Vector3[] anchorLocalPoints)
        {
            if (routeLine == null || anchorLocalPoints == null || anchorLocalPoints.Length == 0) return;
            ApplyLineSettings();
            if (routeLine.useWorldSpace && trackedAnchor != null)
            {
                Vector3[] worldPoints = new Vector3[anchorLocalPoints.Length];
                for (int i = 0; i < anchorLocalPoints.Length; i++)
                {
                    worldPoints[i] = trackedAnchor.TransformPoint(anchorLocalPoints[i]);
                }
                routeLine.positionCount = worldPoints.Length;
                routeLine.SetPositions(worldPoints);
            }
            else
            {
                routeLine.positionCount = anchorLocalPoints.Length;
                routeLine.SetPositions(anchorLocalPoints);
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
                routeLine.numCornerVertices = 6;
                routeLine.numCapVertices = 6;
                routeLine.startColor = Color.white;
                routeLine.endColor = Color.white;
                ApplyLineSettings();
            }
            if (routeLine.sharedMaterial == null || routeLine.sharedMaterial.shader == null || routeLine.sharedMaterial.shader.name != "Vps/ARRouteLine")
            {
                Shader shader = Shader.Find("Vps/ARRouteLine");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) return false;
                ownedMaterial = new Material(shader);
                ownedMaterial.color = routeLineColor;
                if (occlusionController != null)
                {
                    occlusionController.ApplyRouteMaterialDepthSettings(ownedMaterial);
                }
                routeLine.sharedMaterial = ownedMaterial;
            }
            ApplyLineSettings();
            return true;
        }

        private void StopNavigation()
        {
            VpsSystemDiagnostics.Record("NAV", "StopNavigation called; wasTracking=" + trackingRequestAccepted);
            bool wasTracking = trackingRequestAccepted;
            trackingRequestAccepted = false;
            stableTracking = false;
            bindingVerified = false;
            trackedSince = 0d;
            if (wasTracking && localizationAdapter != null) localizationAdapter.StopTracking();
            activeRoutedPoiId = null;
            currentRoutePoints = Array.Empty<Vector3>();
            trackedAnchor = null;
            SetRouteVisible(false);
            if (companionGuide != null) companionGuide.SetVisible(false);
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
                SetRouteVisible(false);
                trackedSince = 0d;
                stableTracking = false;
            }
        }

        private void OnDestroy()
        {
            StopNavigation();
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
