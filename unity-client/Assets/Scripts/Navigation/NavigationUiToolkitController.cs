using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vps.UnityClient.Api;
using Vps.UnityClient.Bootstrap;

namespace Vps.UnityClient.Navigation
{
    public struct NavigationUiState
    {
        public bool IsTrackingAccepted;
        public bool IsStableTracking;
        public bool IsNavigating;
        public string DestinationLabel;
        public string DestinationDescription;
        public float RouteRemainingMeters;
        public float TotalRouteMeters;
        public string CompanionMessage;
        public bool IsCompanionWaiting;
        public bool IsCompanionRerouting;
        public bool IsArrived;
        public string DiagnosticsText;
        public string StatusMessage;
        public NavigationBootstrapState BootstrapState;
        public string BootstrapError;
        public string ActiveFloorId;
        public int TotalPoiCount;
        public bool IsBusyConnecting;
        public float Fps;
        public float FrameTimeMs;
        public float MemoryMb;
        public bool DepthAvailable;
        public ArOcclusionQualityMode EffectiveOcclusionMode;
        public bool IsAuthoringMode;
        public bool IsRecordingWalk;
        public float RecordedWalkMeters;
        public int RecordedPointCount;
        public float NearestGraphDistanceMeters;
        public Vector3 CurrentAnchorLocalPos;
        public bool IsUsingLocalManifest;
        public bool HasRobotBubbleAnchor;
        public bool IsRobotInFrontOfCamera;
        public Vector2 RobotBubbleViewportPos;
        public bool IsTtsSpeaking;
        public string TtsStatusSummary;
    }

    /// <summary>
    /// Modern responsive UI Toolkit controller bound to NavigationUI.uxml &amp; NavigationStyle.uss.
    /// Zero per-frame layout allocation, crisp SDF vector iconography, and safe-area aware layout.
    /// </summary>
    public sealed class NavigationUiToolkitController
    {
        private const string AllCategoriesKey = "__ALL__";
        private readonly SiteGraphV2NavigationExperience experience;

        private VisualElement rootElement;
        private VisualElement launcherBackground;
        private VisualElement localizingReticle;
        private VisualElement safeAreaContainer;

        // Top HUD elements
        private VisualElement idleHeader;
        private VisualElement localizingHeader;
        private VisualElement navigatingHeader;
        private VisualElement authoringHeader;
        private Button btnOpenMapEditor;
        private Button btnIdleSettings;
        private Button btnLocalizingBack;
        private Button btnLocalizingSettings;
        private Label localizingTargetLabel;
        private VisualElement navPinBadge;
        private Label navStateEyebrow;
        private Label navDestinationName;
        private Label navDistanceRemaining;
        private VisualElement navProgressFill;
        private VisualElement distancePill;
        private Button btnNavSettings;
        private Button btnNavStop;
        private Label authoringCoordsLabel;
        private Label authoringSnapPill;
        private Button btnAuthoringDone;

        // Center Readiness / Launcher Hero
        private VisualElement readinessCard;
        private Label readinessSubtitle;
        private VisualElement readinessErrorRow;
        private Label lblReadinessError;
        private Button btnRetryBootstrap;

        // Bottom Thumb-Zone Docks
        private VisualElement destinationDock;
        private VisualElement idleQuickPicks;
        private VisualElement idleQuickPicksContainer;
        private Button btnOpenDestinationPicker;
        private Label lblLauncherPoiSummary;
        private Label lblLauncherPoiCount;

        private VisualElement localizingDock;
        private Label lblLocalizingStatus;
        private Label lblLocalizingHint;
        private Button btnCancelLocalizing;

        private VisualElement companionDock;
        private VisualElement companionAvatarBox;
        private Label companionStateBadge;
        private Label companionStatusMessage;
        private VisualElement robotSpeechLayer;
        private VisualElement robotSpeechBubble;
        private Label lblRobotBubbleTitle;
        private Label lblRobotBubbleBody;
        private Button btnRobotBubbleSpeak;
        private VisualElement robotSpeechTail;
        private bool wasRobotBubbleVisible;
        private Vector2 smoothedRobotBubblePos;
        private Button btnChangeDestination;
        private Button btnFinishNavigation;

        private VisualElement authoringDock;
        private VisualElement authoringAvatarBox;
        private Label authoringStateBadge;
        private Label authoringStatusMessage;
        private Button btnAuthoringToggleWalk;
        private Button btnAuthoringMarkPoi;

        // Destination Picker Bottom Sheet
        private VisualElement modalDestinationPicker;
        private VisualElement destinationFooter;
        private Button btnPickerManagePois;
        private Button btnCloseDestinationPicker;
        private VisualElement destinationSearchBox;
        private TextField destinationSearchInput;
        private Button btnClearSearch;
        private VisualElement destinationFilterContainer;
        private ScrollView destinationScrollView;
        private VisualElement destinationItemsContainer;
        private VisualElement destinationEmptyState;
        private Button btnResetPoiFilter;
        private Label destinationTotalBadge;
        private Label destinationSelectionLabel;
        private Button btnStartNavigation;
        private Label lblStartNavigation;

        // Settings Bottom Sheet
        private VisualElement modalSettings;
        private ScrollView settingsScrollView;
        private VisualElement settingsFooter;
        private Button btnCloseSettings;
        private VisualElement routePreviewLine;
        private VisualElement routePreviewEnd;
        private VisualElement containerColorSwatches;
        private VisualElement containerOcclusionModes;
        private Slider sliderRouteHue;
        private Label valRouteHue;
        private Slider sliderRouteWidth;
        private Label valRouteWidth;
        private Slider sliderRouteOpacity;
        private Label valRouteOpacity;
        private Slider sliderRouteDepthBias;
        private Label valRouteDepthBias;
        private Slider sliderModelScale;
        private Label valModelScale;
        private Slider sliderModelBrightness;
        private Label valModelBrightness;
        private Slider sliderLeadDistance;
        private Label valLeadDistance;
        private Slider sliderGroundDrop;
        private Label valGroundDrop;

        private Toggle toggleSmoothEdges;
        private Toggle toggleStableOcclusionDistance;
        private Toggle toggleSemantic;
        private Toggle toggleSuppressGround;
        private Toggle toggleDepthVis;
        private Toggle toggleMeshDebug;
        private Toggle toggleTtsEnabled;
        private Slider sliderTtsRate;
        private Label valTtsRate;
        private VisualElement ttsApiKeyBox;
        private TextField ttsApiKeyInput;
        private Label lblTtsStatus;
        private Button btnTtsTest;

        private Label lblMetricFps;
        private Label lblMetricFrame;
        private Label lblMetricRam;
        private Label lblMetricDepth;
        private Label lblDiagnostics;
        private Label lblOcclusionStatus;
        private Button btnSettingsReset;
        private Button btnSettingsDone;

        // Offline Map Editor Bottom Sheet
        private VisualElement modalMapEditor;
        private VisualElement mapEditorFooter;
        private Button btnCloseMapEditor;
        private Label mapEditorCountBadge;
        private Label mapEditorCustomBadge;
        private VisualElement mapEditorFeedback;
        private Label lblMapEditorFeedback;
        private ScrollView mapEditorScrollView;
        private VisualElement mapEditorItemsContainer;
        private Button btnStartArAuthoring;
        private Button btnExportManifestJson;
        private Button btnResetLocalManifest;

        // Add / Edit POI Form Modal
        private VisualElement modalPoiForm;
        private ScrollView poiFormScrollView;
        private VisualElement poiFormFooter;
        private Button btnClosePoiForm;
        private Label lblPoiFormTitle;
        private Label lblPoiFormSubtitle;
        private VisualElement poiFormConnectionBox;
        private Label lblPoiFormConnection;
        private Label lblPoiFormError;
        private VisualElement poiFormInputBox;
        private TextField poiFormLabelInput;
        private VisualElement poiFormCategoryContainer;
        private VisualElement poiFormDescriptionBox;
        private TextField poiFormDescriptionInput;
        private Button btnPoiFormCancel;
        private Button btnPoiFormSave;

        // Cached State
        private readonly List<VisualElement> colorChipElements = new List<VisualElement>();
        private readonly List<VisualElement> occlusionCardElements = new List<VisualElement>();
        private readonly List<Button> poiFormCategoryButtons = new List<Button>();
        private readonly Dictionary<string, VisualElement> poiRowById = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private readonly List<PublishedGraphPoiV2> latestPois = new List<PublishedGraphPoiV2>();
        private readonly List<string> knownCategories = new List<string>();

        private string currentSelectedPoiId;
        private string activeCategoryFilter = AllCategoriesKey;
        private bool isSyncingSettings;
        private bool isPickerOpen;
        private bool isSettingsOpen;
        private bool isMapEditorOpen;
        private bool isPoiFormOpen;
        private bool isBusyConnecting;
        private bool resetLocalConfirmPending;
        private string editingPoiId;
        private string poiFormSelectedCategory = "room";

        // Safe-area geometry cache (avoids touching styles every frame)
        private Rect lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private float lastPanelW = -1f;
        private float lastPanelH = -1f;
        private int lastScreenW = -1;
        private int lastScreenH = -1;

        public bool IsActive => rootElement != null;
        public bool IsPickerOpen => isPickerOpen;
        public bool IsSettingsOpen => isSettingsOpen;
        public bool IsMapEditorOpen => isMapEditorOpen;
        public bool IsPoiFormOpen => isPoiFormOpen;

        public NavigationUiToolkitController(SiteGraphV2NavigationExperience navExperience)
        {
            experience = navExperience;
        }

        public bool Initialize(UIDocument uiDocument)
        {
            if (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                return false;
            }

            rootElement = uiDocument.rootVisualElement;
            QueryElements();
            if (safeAreaContainer == null || destinationItemsContainer == null)
            {
                rootElement = null;
                return false;
            }

            BindVectorIcons();
            ConfigureModernScrollView(destinationScrollView);
            ConfigureModernScrollView(settingsScrollView);
            ConfigureModernScrollView(mapEditorScrollView);
            ConfigureModernScrollView(poiFormScrollView);
            BuildColorSwatches();
            BuildOcclusionModeCards();
            BuildPoiFormCategoryChips();
            RegisterEvents();

            rootElement.RegisterCallback<GeometryChangedEvent>(_ => ApplySafeArea());
            ApplySafeArea();
            SyncSettingsUi();
            return true;
        }

        private void QueryElements()
        {
            launcherBackground = rootElement.Q<VisualElement>("launcher-background");
            localizingReticle = rootElement.Q<VisualElement>("localizing-reticle");
            safeAreaContainer = rootElement.Q<VisualElement>("safe-area-container");

            idleHeader = rootElement.Q<VisualElement>("idle-header");
            localizingHeader = rootElement.Q<VisualElement>("localizing-header");
            navigatingHeader = rootElement.Q<VisualElement>("navigating-header");
            authoringHeader = rootElement.Q<VisualElement>("authoring-header");

            btnOpenMapEditor = rootElement.Q<Button>("btn-open-map-editor");
            btnIdleSettings = rootElement.Q<Button>("btn-idle-settings");
            btnLocalizingBack = rootElement.Q<Button>("btn-localizing-back");
            btnLocalizingSettings = rootElement.Q<Button>("btn-localizing-settings");
            localizingTargetLabel = rootElement.Q<Label>("localizing-target-label");

            navPinBadge = rootElement.Q<VisualElement>("nav-pin-badge");
            navStateEyebrow = rootElement.Q<Label>("nav-state-eyebrow");
            navDestinationName = rootElement.Q<Label>("nav-destination-name");
            navDistanceRemaining = rootElement.Q<Label>("nav-distance-remaining");
            navProgressFill = rootElement.Q<VisualElement>("nav-progress-fill");
            distancePill = navDistanceRemaining != null ? navDistanceRemaining.parent : null;
            btnNavSettings = rootElement.Q<Button>("btn-nav-settings");
            btnNavStop = rootElement.Q<Button>("btn-nav-stop");

            authoringCoordsLabel = rootElement.Q<Label>("authoring-coords-label");
            authoringSnapPill = rootElement.Q<Label>("authoring-snap-pill");
            btnAuthoringDone = rootElement.Q<Button>("btn-authoring-done");

            readinessCard = rootElement.Q<VisualElement>("readiness-card");
            readinessSubtitle = rootElement.Q<Label>("readiness-subtitle");
            readinessErrorRow = rootElement.Q<VisualElement>("readiness-error-row");
            lblReadinessError = rootElement.Q<Label>("lbl-readiness-error");
            btnRetryBootstrap = rootElement.Q<Button>("btn-retry-bootstrap");

            destinationDock = rootElement.Q<VisualElement>("destination-dock");
            idleQuickPicks = rootElement.Q<VisualElement>("idle-quick-picks");
            idleQuickPicksContainer = rootElement.Q<VisualElement>("idle-quick-picks-container");
            btnOpenDestinationPicker = rootElement.Q<Button>("btn-open-destination-picker");
            lblLauncherPoiSummary = rootElement.Q<Label>("lbl-launcher-poi-summary");
            lblLauncherPoiCount = rootElement.Q<Label>("lbl-launcher-poi-count");

            localizingDock = rootElement.Q<VisualElement>("localizing-dock");
            lblLocalizingStatus = rootElement.Q<Label>("lbl-localizing-status");
            lblLocalizingHint = rootElement.Q<Label>("lbl-localizing-hint");
            btnCancelLocalizing = rootElement.Q<Button>("btn-cancel-localizing");

            companionDock = rootElement.Q<VisualElement>("companion-dock");
            companionAvatarBox = rootElement.Q<VisualElement>("companion-avatar-box");
            companionStateBadge = rootElement.Q<Label>("companion-state-badge");
            companionStatusMessage = rootElement.Q<Label>("companion-status-message");
            robotSpeechLayer = rootElement.Q<VisualElement>("robot-speech-layer");
            robotSpeechBubble = rootElement.Q<VisualElement>("robot-speech-bubble");
            lblRobotBubbleTitle = rootElement.Q<Label>("lbl-robot-bubble-title");
            lblRobotBubbleBody = rootElement.Q<Label>("lbl-robot-bubble-body");
            btnRobotBubbleSpeak = rootElement.Q<Button>("btn-robot-bubble-speak");
            robotSpeechTail = rootElement.Q<VisualElement>("robot-speech-tail");
            btnChangeDestination = rootElement.Q<Button>("btn-change-destination");
            btnFinishNavigation = rootElement.Q<Button>("btn-finish-navigation");

            authoringDock = rootElement.Q<VisualElement>("authoring-dock");
            authoringAvatarBox = rootElement.Q<VisualElement>("authoring-avatar-box");
            authoringStateBadge = rootElement.Q<Label>("authoring-state-badge");
            authoringStatusMessage = rootElement.Q<Label>("authoring-status-message");
            btnAuthoringToggleWalk = rootElement.Q<Button>("btn-authoring-toggle-walk");
            btnAuthoringMarkPoi = rootElement.Q<Button>("btn-authoring-mark-poi");

            modalDestinationPicker = rootElement.Q<VisualElement>("modal-destination-picker");
            destinationFooter = modalDestinationPicker != null ? modalDestinationPicker.Q<VisualElement>(className: "modal-footer--picker") : null;
            btnPickerManagePois = rootElement.Q<Button>("btn-picker-manage-pois");
            btnCloseDestinationPicker = rootElement.Q<Button>("btn-close-destination-picker");
            destinationSearchBox = rootElement.Q<VisualElement>("destination-search-box");
            destinationSearchInput = rootElement.Q<TextField>("destination-search-input");
            btnClearSearch = rootElement.Q<Button>("btn-clear-search");
            destinationFilterContainer = rootElement.Q<VisualElement>("destination-filter-container");
            destinationScrollView = rootElement.Q<ScrollView>("destination-scroll-view");
            destinationItemsContainer = rootElement.Q<VisualElement>("destination-items-container");
            destinationEmptyState = rootElement.Q<VisualElement>("destination-empty-state");
            btnResetPoiFilter = rootElement.Q<Button>("btn-reset-poi-filter");
            destinationTotalBadge = rootElement.Q<Label>("destination-total-badge");
            destinationSelectionLabel = rootElement.Q<Label>("destination-selection-label");
            btnStartNavigation = rootElement.Q<Button>("btn-start-navigation");
            lblStartNavigation = rootElement.Q<Label>("lbl-start-navigation");

            modalSettings = rootElement.Q<VisualElement>("modal-settings");
            settingsScrollView = rootElement.Q<ScrollView>("settings-scroll-view");
            settingsFooter = modalSettings != null ? modalSettings.Q<VisualElement>(className: "modal-footer--settings") : null;
            btnCloseSettings = rootElement.Q<Button>("btn-close-settings");
            routePreviewLine = rootElement.Q<VisualElement>("route-preview-line");
            routePreviewEnd = rootElement.Q<VisualElement>("route-preview-end");
            containerColorSwatches = rootElement.Q<VisualElement>("container-color-swatches");
            containerOcclusionModes = rootElement.Q<VisualElement>("container-occlusion-modes");

            sliderRouteHue = rootElement.Q<Slider>("slider-route-hue");
            valRouteHue = rootElement.Q<Label>("val-route-hue");
            sliderRouteWidth = rootElement.Q<Slider>("slider-route-width");
            valRouteWidth = rootElement.Q<Label>("val-route-width");
            sliderRouteOpacity = rootElement.Q<Slider>("slider-route-opacity");
            valRouteOpacity = rootElement.Q<Label>("val-route-opacity");
            sliderRouteDepthBias = rootElement.Q<Slider>("slider-route-depth-bias");
            valRouteDepthBias = rootElement.Q<Label>("val-route-depth-bias");

            sliderModelScale = rootElement.Q<Slider>("slider-model-scale");
            valModelScale = rootElement.Q<Label>("val-model-scale");
            sliderModelBrightness = rootElement.Q<Slider>("slider-model-brightness");
            valModelBrightness = rootElement.Q<Label>("val-model-brightness");
            sliderLeadDistance = rootElement.Q<Slider>("slider-lead-distance");
            valLeadDistance = rootElement.Q<Label>("val-lead-distance");
            sliderGroundDrop = rootElement.Q<Slider>("slider-ground-drop");
            valGroundDrop = rootElement.Q<Label>("val-ground-drop");

            toggleSmoothEdges = rootElement.Q<Toggle>("toggle-smooth-edges");
            toggleStableOcclusionDistance = rootElement.Q<Toggle>("toggle-stable-occlusion-distance");
            toggleSemantic = rootElement.Q<Toggle>("toggle-semantic");
            toggleSuppressGround = rootElement.Q<Toggle>("toggle-suppress-ground");
            toggleDepthVis = rootElement.Q<Toggle>("toggle-depth-vis");
            toggleMeshDebug = rootElement.Q<Toggle>("toggle-mesh-debug");
            toggleTtsEnabled = rootElement.Q<Toggle>("toggle-tts-enabled");
            sliderTtsRate = rootElement.Q<Slider>("slider-tts-rate");
            valTtsRate = rootElement.Q<Label>("val-tts-rate");
            ttsApiKeyBox = rootElement.Q<VisualElement>("tts-api-key-box");
            ttsApiKeyInput = rootElement.Q<TextField>("tts-api-key-input");
            if (ttsApiKeyInput != null) ttsApiKeyInput.isPasswordField = true;
            lblTtsStatus = rootElement.Q<Label>("lbl-tts-status");
            btnTtsTest = rootElement.Q<Button>("btn-tts-test");

            lblMetricFps = rootElement.Q<Label>("lbl-metric-fps");
            lblMetricFrame = rootElement.Q<Label>("lbl-metric-frame");
            lblMetricRam = rootElement.Q<Label>("lbl-metric-ram");
            lblMetricDepth = rootElement.Q<Label>("lbl-metric-depth");
            lblDiagnostics = rootElement.Q<Label>("lbl-diagnostics");
            lblOcclusionStatus = rootElement.Q<Label>("lbl-occlusion-status");
            btnSettingsReset = rootElement.Q<Button>("btn-settings-reset");
            btnSettingsDone = rootElement.Q<Button>("btn-settings-done");

            modalMapEditor = rootElement.Q<VisualElement>("modal-map-editor");
            mapEditorFooter = rootElement.Q<VisualElement>("map-editor-footer");
            btnCloseMapEditor = rootElement.Q<Button>("btn-close-map-editor");
            mapEditorCountBadge = rootElement.Q<Label>("map-editor-count-badge");
            mapEditorCustomBadge = rootElement.Q<Label>("map-editor-custom-badge");
            mapEditorFeedback = rootElement.Q<VisualElement>("map-editor-feedback");
            lblMapEditorFeedback = rootElement.Q<Label>("lbl-map-editor-feedback");
            mapEditorScrollView = rootElement.Q<ScrollView>("map-editor-scroll-view");
            mapEditorItemsContainer = rootElement.Q<VisualElement>("map-editor-items-container");
            btnStartArAuthoring = rootElement.Q<Button>("btn-start-ar-authoring");
            btnExportManifestJson = rootElement.Q<Button>("btn-export-manifest-json");
            btnResetLocalManifest = rootElement.Q<Button>("btn-reset-local-manifest");

            modalPoiForm = rootElement.Q<VisualElement>("modal-poi-form");
            poiFormScrollView = rootElement.Q<ScrollView>("poi-form-scroll-view");
            poiFormFooter = rootElement.Q<VisualElement>("poi-form-footer");
            btnClosePoiForm = rootElement.Q<Button>("btn-close-poi-form");
            lblPoiFormTitle = rootElement.Q<Label>("lbl-poi-form-title");
            lblPoiFormSubtitle = rootElement.Q<Label>("lbl-poi-form-subtitle");
            poiFormConnectionBox = rootElement.Q<VisualElement>("poi-form-connection-box");
            lblPoiFormConnection = rootElement.Q<Label>("lbl-poi-form-connection");
            lblPoiFormError = rootElement.Q<Label>("lbl-poi-form-error");
            poiFormInputBox = rootElement.Q<VisualElement>("poi-form-input-box");
            poiFormLabelInput = rootElement.Q<TextField>("poi-form-label-input");
            poiFormCategoryContainer = rootElement.Q<VisualElement>("poi-form-category-container");
            poiFormDescriptionBox = rootElement.Q<VisualElement>("poi-form-description-box");
            poiFormDescriptionInput = rootElement.Q<TextField>("poi-form-description-input");
            if (poiFormDescriptionInput != null) poiFormDescriptionInput.multiline = true;
            btnPoiFormCancel = rootElement.Q<Button>("btn-poi-form-cancel");
            btnPoiFormSave = rootElement.Q<Button>("btn-poi-form-save");
        }

        private void BindVectorIcons()
        {
            BindBrandLogo();
            SetIcon("icon-nav-compass", NavigationUiVectorIcons.IconKind.Compass);
            SetIcon("icon-idle-add-poi", NavigationUiVectorIcons.IconKind.Plus);
            SetIcon("icon-idle-settings", NavigationUiVectorIcons.IconKind.Settings);
            SetIcon("icon-localizing-back", NavigationUiVectorIcons.IconKind.Back);
            SetIcon("icon-localizing-settings", NavigationUiVectorIcons.IconKind.Settings);
            SetIcon("icon-nav-pin", NavigationUiVectorIcons.IconKind.Pin);
            SetIcon("icon-nav-settings", NavigationUiVectorIcons.IconKind.Settings);
            SetIcon("icon-authoring-pin", NavigationUiVectorIcons.IconKind.RoutePath);
            SetIcon("icon-authoring-route", NavigationUiVectorIcons.IconKind.RoutePath);
            SetIcon("icon-authoring-mark", NavigationUiVectorIcons.IconKind.Plus);

            SetIcon("icon-readiness-error", NavigationUiVectorIcons.IconKind.AlertCircle);
            SetIcon("icon-retry-bootstrap", NavigationUiVectorIcons.IconKind.Refresh);
            SetIcon("icon-step-pin", NavigationUiVectorIcons.IconKind.Pin);
            SetIcon("icon-step-scan", NavigationUiVectorIcons.IconKind.ScanCamera);
            SetIcon("icon-step-robot", NavigationUiVectorIcons.IconKind.Robot);

            SetIcon("icon-launcher-search", NavigationUiVectorIcons.IconKind.Search);
            SetIcon("icon-launcher-chevron", NavigationUiVectorIcons.IconKind.ChevronRight);
            SetIcon("icon-localizing-scan", NavigationUiVectorIcons.IconKind.ScanCamera);
            SetIcon("icon-companion-robot", NavigationUiVectorIcons.IconKind.Robot);
            SetIcon("icon-robot-bubble", NavigationUiVectorIcons.IconKind.Robot);
            SetIcon("icon-robot-bubble-speak", NavigationUiVectorIcons.IconKind.VolumeOn);
            SetIcon("icon-finish-check", NavigationUiVectorIcons.IconKind.Check);

            SetIcon("icon-close-picker", NavigationUiVectorIcons.IconKind.Close);
            SetIcon("icon-search-prefix", NavigationUiVectorIcons.IconKind.Search);
            SetIcon("icon-clear-search", NavigationUiVectorIcons.IconKind.Close);
            SetIcon("icon-empty-search", NavigationUiVectorIcons.IconKind.Search);
            SetIcon("icon-footer-pin", NavigationUiVectorIcons.IconKind.Pin);
            SetIcon("icon-start-compass", NavigationUiVectorIcons.IconKind.Compass);

            SetIcon("icon-settings-header", NavigationUiVectorIcons.IconKind.Settings);
            SetIcon("icon-close-settings", NavigationUiVectorIcons.IconKind.Close);
            SetIcon("icon-sec-route", NavigationUiVectorIcons.IconKind.RoutePath);
            SetIcon("icon-sec-companion", NavigationUiVectorIcons.IconKind.Robot);
            SetIcon("icon-sec-tts", NavigationUiVectorIcons.IconKind.VolumeOn);
            SetIcon("icon-tts-test", NavigationUiVectorIcons.IconKind.VolumeOn);
            SetIcon("icon-sec-occlusion", NavigationUiVectorIcons.IconKind.Layers);
            SetIcon("icon-sec-diag", NavigationUiVectorIcons.IconKind.Activity);

            SetIcon("icon-close-map-editor", NavigationUiVectorIcons.IconKind.Close);
            SetIcon("icon-start-ar-authoring", NavigationUiVectorIcons.IconKind.Plus);
            SetIcon("icon-export-json", NavigationUiVectorIcons.IconKind.Copy);
            SetIcon("icon-close-poi-form", NavigationUiVectorIcons.IconKind.Close);
        }

        private void BindBrandLogo()
        {
            var el = rootElement?.Q<VisualElement>("icon-brand-compass");
            if (el == null) return;

            var logoTex = Resources.Load<Texture2D>("UI/AppLogo");
            if (logoTex != null)
            {
                el.style.backgroundImage = new StyleBackground(logoTex);
                el.style.unityBackgroundImageTintColor = StyleKeyword.Null;
                return;
            }

            SetIcon("icon-brand-compass", NavigationUiVectorIcons.IconKind.Compass);
        }

        private void SetIcon(string elementName, NavigationUiVectorIcons.IconKind kind)
        {
            var el = rootElement?.Q<VisualElement>(elementName);
            if (el == null) return;
            var tex = NavigationUiVectorIcons.Get(kind);
            if (tex != null)
            {
                el.style.backgroundImage = new StyleBackground(tex);
            }
        }

        private static void ConfigureModernScrollView(ScrollView scrollView)
        {
            if (scrollView == null) return;
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;

            var scroller = scrollView.verticalScroller;
            if (scroller == null) return;

            var lowBtn = scroller.Q<VisualElement>(className: "unity-scroller__low-button");
            if (lowBtn != null) lowBtn.style.display = DisplayStyle.None;

            var highBtn = scroller.Q<VisualElement>(className: "unity-scroller__high-button");
            if (highBtn != null) highBtn.style.display = DisplayStyle.None;
        }

        private void BuildColorSwatches()
        {
            if (containerColorSwatches == null) return;
            containerColorSwatches.Clear();
            colorChipElements.Clear();

            Color[] colors = SiteGraphV2NavigationExperience.PaletteColors;
            var checkTex = NavigationUiVectorIcons.Get(NavigationUiVectorIcons.IconKind.Check);

            for (int i = 0; i < colors.Length; i++)
            {
                int idx = i;
                Color c = colors[i];

                var chip = new VisualElement();
                chip.AddToClassList("color-swatch-chip");
                chip.pickingMode = PickingMode.Position;

                var circle = new VisualElement();
                circle.AddToClassList("color-swatch-circle");
                circle.style.backgroundColor = c;
                circle.pickingMode = PickingMode.Ignore;

                var checkIcon = new VisualElement();
                checkIcon.name = "swatch-check";
                checkIcon.AddToClassList("vec-icon");
                checkIcon.AddToClassList("vec-icon--14");
                if (checkTex != null)
                {
                    checkIcon.style.backgroundImage = new StyleBackground(checkTex);
                }
                float luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                checkIcon.style.unityBackgroundImageTintColor = luma > 0.72f
                    ? new Color(0.06f, 0.09f, 0.16f, 1f)
                    : Color.white;
                checkIcon.style.display = DisplayStyle.None;
                checkIcon.pickingMode = PickingMode.Ignore;
                circle.Add(checkIcon);

                chip.Add(circle);
                chip.RegisterCallback<ClickEvent>(_ =>
                {
                    if (experience == null) return;
                    experience.RouteColorIndex = idx;
                    SyncSettingsUi();
                });

                containerColorSwatches.Add(chip);
                colorChipElements.Add(chip);
            }
        }

        private void BuildOcclusionModeCards()
        {
            if (containerOcclusionModes == null) return;
            containerOcclusionModes.Clear();
            occlusionCardElements.Clear();

            var options = new[]
            {
                (mode: ArOcclusionQualityMode.Off, title: "Tắt", desc: "Tiết kiệm tài nguyên tối đa", recommended: false),
                (mode: ArOcclusionQualityMode.Fallback, title: "Cơ bản (Mượt)", desc: "Depth chuẩn AR, nhẹ máy", recommended: false),
                (mode: ArOcclusionQualityMode.Balanced, title: "Cân bằng", desc: "Tối ưu FPS & che khuất", recommended: true),
                (mode: ArOcclusionQualityMode.HighQuality, title: "Chi tiết cao", desc: "Làm nét viền & meshing", recommended: false),
            };

            foreach (var opt in options)
            {
                var card = new VisualElement();
                card.userData = opt.mode;
                card.AddToClassList("occlusion-card");
                card.pickingMode = PickingMode.Position;

                var topRow = new VisualElement();
                topRow.AddToClassList("occlusion-card-top");
                topRow.pickingMode = PickingMode.Ignore;

                var titleLbl = new Label(opt.title);
                titleLbl.AddToClassList("occlusion-card-title");
                titleLbl.pickingMode = PickingMode.Ignore;
                topRow.Add(titleLbl);

                if (opt.recommended)
                {
                    var recBadge = new Label("Khuyên dùng");
                    recBadge.AddToClassList("occlusion-badge-rec");
                    recBadge.pickingMode = PickingMode.Ignore;
                    topRow.Add(recBadge);
                }

                var descLbl = new Label(opt.desc);
                descLbl.AddToClassList("occlusion-card-desc");
                descLbl.pickingMode = PickingMode.Ignore;

                card.Add(topRow);
                card.Add(descLbl);

                var selectedMode = opt.mode;
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    if (experience == null) return;
                    experience.OcclusionMode = selectedMode;
                    SyncSettingsUi();
                });

                containerOcclusionModes.Add(card);
                occlusionCardElements.Add(card);
            }
        }

        private void BuildPoiFormCategoryChips()
        {
            if (poiFormCategoryContainer == null) return;
            poiFormCategoryContainer.Clear();
            poiFormCategoryButtons.Clear();

            string[] cats = LocalSiteGraphStore.SupportedCategories;
            for (int i = 0; i < cats.Length; i++)
            {
                string catKey = cats[i];
                var chip = new Button { text = FormatCategoryLabel(catKey) };
                chip.userData = catKey;
                chip.AddToClassList("filter-chip");
                chip.AddToClassList("poi-form-category-chip");
                chip.clicked += () =>
                {
                    poiFormSelectedCategory = catKey;
                    UpdatePoiFormCategoryHighlights();
                };
                poiFormCategoryContainer.Add(chip);
                poiFormCategoryButtons.Add(chip);
            }
            UpdatePoiFormCategoryHighlights();
        }

        private void RegisterEvents()
        {
            if (btnOpenMapEditor != null) btnOpenMapEditor.clicked += () => experience?.OnOpenMapEditorClicked();
            if (btnPickerManagePois != null) btnPickerManagePois.clicked += () => experience?.OnOpenMapEditorClicked();
            if (btnIdleSettings != null) btnIdleSettings.clicked += OpenSettingsModal;
            if (btnLocalizingSettings != null) btnLocalizingSettings.clicked += OpenSettingsModal;
            if (btnNavSettings != null) btnNavSettings.clicked += OpenSettingsModal;

            if (btnAuthoringDone != null) btnAuthoringDone.clicked += () => experience?.OnStopArAuthoringRequested();
            if (btnAuthoringToggleWalk != null) btnAuthoringToggleWalk.clicked += () => experience?.OnToggleAuthoringWalkRequested();
            if (btnAuthoringMarkPoi != null) btnAuthoringMarkPoi.clicked += () => experience?.OnOpenMarkPoiFormRequested();

            if (btnLocalizingBack != null)
            {
                btnLocalizingBack.clicked += () => experience?.OnBackButtonClicked();
            }

            if (btnRetryBootstrap != null)
            {
                btnRetryBootstrap.clicked += () => experience?.OnRetryBootstrapRequested();
            }

            if (btnOpenDestinationPicker != null)
            {
                btnOpenDestinationPicker.clicked += () => experience?.OnOpenDestinationPickerClicked();
            }

            if (btnChangeDestination != null)
            {
                btnChangeDestination.clicked += () => experience?.OnOpenDestinationPickerClicked();
            }

            if (btnCancelLocalizing != null)
            {
                btnCancelLocalizing.clicked += () => experience?.OnStopNavigationRequested();
            }

            if (btnNavStop != null)
            {
                btnNavStop.clicked += () => experience?.OnStopNavigationRequested();
            }

            if (btnFinishNavigation != null)
            {
                btnFinishNavigation.clicked += () => experience?.OnStopNavigationRequested();
            }

            if (btnCloseDestinationPicker != null)
            {
                btnCloseDestinationPicker.clicked += () =>
                {
                    CloseDestinationPickerModal();
                    experience?.OnDestinationPickerClosed();
                };
            }

            if (btnStartNavigation != null)
            {
                btnStartNavigation.clicked += () =>
                {
                    if (isBusyConnecting) return;
                    experience?.OnStartNavigationRequested();
                };
            }

            if (destinationSearchInput != null)
            {
                destinationSearchInput.RegisterValueChangedCallback(evt =>
                {
                    UpdateClearSearchVisibility(evt.newValue);
                    experience?.OnSearchQueryChanged(evt.newValue);
                });

                destinationSearchInput.RegisterCallback<FocusInEvent>(_ =>
                {
                    destinationSearchBox?.AddToClassList("search-field-container--focused");
                });
                destinationSearchInput.RegisterCallback<FocusOutEvent>(_ =>
                {
                    destinationSearchBox?.RemoveFromClassList("search-field-container--focused");
                });
            }

            if (btnClearSearch != null)
            {
                btnClearSearch.clicked += () =>
                {
                    if (destinationSearchInput != null)
                    {
                        destinationSearchInput.value = string.Empty;
                        destinationSearchInput.Focus();
                    }
                    else
                    {
                        experience?.OnSearchQueryChanged(string.Empty);
                    }
                };
            }

            if (btnResetPoiFilter != null)
            {
                btnResetPoiFilter.clicked += () =>
                {
                    activeCategoryFilter = AllCategoriesKey;
                    if (destinationSearchInput != null && !string.IsNullOrEmpty(destinationSearchInput.value))
                    {
                        destinationSearchInput.value = string.Empty;
                    }
                    else
                    {
                        experience?.OnSearchQueryChanged(string.Empty);
                        RebuildCategoryFilterChips();
                        RenderFilteredPois();
                    }
                };
            }

            // Tap outside bottom sheet closes overlay cleanly
            if (modalDestinationPicker != null)
            {
                modalDestinationPicker.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target == modalDestinationPicker)
                    {
                        CloseDestinationPickerModal();
                        experience?.OnDestinationPickerClosed();
                    }
                });
            }

            if (modalSettings != null)
            {
                modalSettings.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target == modalSettings)
                    {
                        CloseSettingsModal();
                        experience?.OnSettingsClosed();
                    }
                });
            }

            if (btnCloseSettings != null)
            {
                btnCloseSettings.clicked += () =>
                {
                    CloseSettingsModal();
                    experience?.OnSettingsClosed();
                };
            }

            if (btnSettingsDone != null)
            {
                btnSettingsDone.clicked += () =>
                {
                    CloseSettingsModal();
                    experience?.OnSettingsClosed();
                };
            }

            if (btnSettingsReset != null)
            {
                btnSettingsReset.clicked += () =>
                {
                    experience?.OnResetSettingsRequested();
                    SyncSettingsUi();
                };
            }

            // Sliders
            RegisterSlider(sliderRouteHue, val =>
            {
                if (experience != null) experience.RouteHue = val;
                if (valRouteHue != null) valRouteHue.text = $"{val:0}°";
                UpdateSwatchSelectionVisuals();
                UpdateRoutePreview();
            });

            RegisterSlider(sliderRouteWidth, val =>
            {
                if (experience != null) experience.RouteLineWidth = val;
                if (valRouteWidth != null) valRouteWidth.text = $"{val:0.00} m";
                UpdateRoutePreview();
            });

            RegisterSlider(sliderRouteOpacity, val =>
            {
                if (experience != null) experience.RouteLineOpacity = val;
                if (valRouteOpacity != null) valRouteOpacity.text = $"{Mathf.RoundToInt(val * 100f)}%";
                UpdateRoutePreview();
            });

            RegisterSlider(sliderRouteDepthBias, val =>
            {
                if (experience != null) experience.RouteDepthBias = val;
                if (valRouteDepthBias != null) valRouteDepthBias.text = $"{val:0.00} m";
            });

            RegisterSlider(sliderModelScale, val =>
            {
                if (experience != null) experience.CompanionModelScale = val;
                if (valModelScale != null) valModelScale.text = $"{val:0.00}×";
            });

            RegisterSlider(sliderModelBrightness, val =>
            {
                if (experience != null) experience.CompanionBrightness = val;
                if (valModelBrightness != null) valModelBrightness.text = $"{val:0.00}";
            });

            RegisterSlider(sliderLeadDistance, val =>
            {
                if (experience != null) experience.CompanionLeadDistance = val;
                if (valLeadDistance != null) valLeadDistance.text = $"{val:0.00} m";
            });

            RegisterSlider(sliderGroundDrop, val =>
            {
                if (experience != null) experience.CompanionGroundDrop = val;
                if (valGroundDrop != null) valGroundDrop.text = $"{val:0.00} m";
            });

            // Toggles
            RegisterToggle(toggleSmoothEdges, val => { if (experience != null) experience.SmoothEdgesOcclusion = val; });
            RegisterToggle(toggleStableOcclusionDistance, val => { if (experience != null) experience.StabilizedOcclusionDistance = val; });
            RegisterToggle(toggleSemantic, val => { if (experience != null) experience.SemanticSuppressionOcclusion = val; });
            RegisterToggle(toggleSuppressGround, val => { if (experience != null) experience.SuppressGroundOcclusion = val; });
            RegisterToggle(toggleDepthVis, val => { if (experience != null) experience.DepthVisualizationOcclusion = val; });
            RegisterToggle(toggleMeshDebug, val => { if (experience != null) experience.MeshDebugOcclusion = val; });

            RegisterToggle(toggleTtsEnabled, val =>
            {
                if (experience != null)
                {
                    experience.TtsEnabled = val;
                    UpdateTtsStatusLabel(experience.TtsStatusSummary);
                }
            });
            RegisterSlider(sliderTtsRate, val =>
            {
                if (experience != null) experience.TtsSpeakingRate = val;
                if (valTtsRate != null) valTtsRate.text = $"{val:0.00}×";
            });
            if (ttsApiKeyInput != null)
            {
                ttsApiKeyInput.RegisterValueChangedCallback(evt =>
                {
                    if (isSyncingSettings || experience == null) return;
                    experience.TtsCloudApiKey = evt.newValue;
                    UpdateTtsStatusLabel(experience.TtsStatusSummary);
                });
                ttsApiKeyInput.RegisterCallback<FocusInEvent>(_ => ttsApiKeyBox?.AddToClassList("search-field-container--focused"));
                ttsApiKeyInput.RegisterCallback<FocusOutEvent>(_ => ttsApiKeyBox?.RemoveFromClassList("search-field-container--focused"));
            }
            if (btnTtsTest != null)
            {
                btnTtsTest.clicked += () =>
                {
                    experience?.OnTestTtsRequested();
                    UpdateTtsStatusLabel(experience?.TtsStatusSummary);
                };
            }
            if (btnRobotBubbleSpeak != null)
            {
                btnRobotBubbleSpeak.clicked += () => experience?.OnReplayArrivalSpeechRequested();
            }

            // Map Editor modal
            if (btnCloseMapEditor != null)
            {
                btnCloseMapEditor.clicked += () =>
                {
                    CloseMapEditorModal();
                    experience?.OnMapEditorClosed();
                };
            }
            if (modalMapEditor != null)
            {
                modalMapEditor.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target == modalMapEditor)
                    {
                        CloseMapEditorModal();
                        experience?.OnMapEditorClosed();
                    }
                });
            }
            if (btnStartArAuthoring != null)
            {
                btnStartArAuthoring.clicked += () =>
                {
                    CloseMapEditorModal();
                    CloseDestinationPickerModal();
                    experience?.OnStartArAuthoringRequested();
                };
            }
            if (btnExportManifestJson != null)
            {
                btnExportManifestJson.clicked += () => experience?.OnExportManifestJsonRequested();
            }
            if (btnResetLocalManifest != null)
            {
                btnResetLocalManifest.clicked += () =>
                {
                    if (!resetLocalConfirmPending)
                    {
                        resetLocalConfirmPending = true;
                        btnResetLocalManifest.text = "Bấm lại để xác nhận";
                        ShowMapEditorFeedback("Bấm lại nút Khôi phục một lần nữa để xóa các điểm tự thêm và quay về bản đồ gốc.", false);
                        return;
                    }
                    resetLocalConfirmPending = false;
                    btnResetLocalManifest.text = "Khôi phục gốc";
                    experience?.OnResetLocalManifestRequested();
                };
            }

            // Add / Edit POI Form modal
            if (btnClosePoiForm != null) btnClosePoiForm.clicked += ClosePoiFormModal;
            if (btnPoiFormCancel != null) btnPoiFormCancel.clicked += ClosePoiFormModal;
            if (modalPoiForm != null)
            {
                modalPoiForm.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target == modalPoiForm) ClosePoiFormModal();
                });
            }
            if (poiFormLabelInput != null)
            {
                poiFormLabelInput.RegisterCallback<FocusInEvent>(_ => poiFormInputBox?.AddToClassList("search-field-container--focused"));
                poiFormLabelInput.RegisterCallback<FocusOutEvent>(_ => poiFormInputBox?.RemoveFromClassList("search-field-container--focused"));
            }
            if (poiFormDescriptionInput != null)
            {
                poiFormDescriptionInput.RegisterCallback<FocusInEvent>(_ => poiFormDescriptionBox?.AddToClassList("search-field-container--focused"));
                poiFormDescriptionInput.RegisterCallback<FocusOutEvent>(_ => poiFormDescriptionBox?.RemoveFromClassList("search-field-container--focused"));
            }
            if (btnPoiFormSave != null)
            {
                btnPoiFormSave.clicked += HandlePoiFormSubmit;
            }
        }

        private void RegisterSlider(Slider slider, Action<float> onChanged)
        {
            if (slider == null) return;
            slider.RegisterValueChangedCallback(evt =>
            {
                if (isSyncingSettings) return;
                onChanged?.Invoke(evt.newValue);
            });
        }

        private void RegisterToggle(Toggle toggle, Action<bool> onChanged)
        {
            if (toggle == null) return;
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (isSyncingSettings) return;
                onChanged?.Invoke(evt.newValue);
            });
        }

        public void ApplySafeArea()
        {
            if (safeAreaContainer == null) return;

            Rect safe = Screen.safeArea;
            int screenW = Mathf.Max(1, Screen.width);
            int screenH = Mathf.Max(1, Screen.height);
            float panelW = safeAreaContainer.panel != null ? safeAreaContainer.panel.visualTree.layout.width : screenW;
            float panelH = safeAreaContainer.panel != null ? safeAreaContainer.panel.visualTree.layout.height : screenH;

            if (float.IsNaN(panelW) || panelW <= 1f) panelW = screenW;
            if (float.IsNaN(panelH) || panelH <= 1f) panelH = screenH;

            if (safe == lastSafeArea &&
                screenW == lastScreenW &&
                screenH == lastScreenH &&
                Mathf.Abs(panelW - lastPanelW) < 0.5f &&
                Mathf.Abs(panelH - lastPanelH) < 0.5f)
            {
                return;
            }

            lastSafeArea = safe;
            lastScreenW = screenW;
            lastScreenH = screenH;
            lastPanelW = panelW;
            lastPanelH = panelH;

            if (safe.width <= 0f || safe.height <= 0f) return;

            float scaleX = panelW / screenW;
            float scaleY = panelH / screenH;

            float padLeft = Mathf.Max(16f, safe.x * scaleX);
            float padRight = Mathf.Max(16f, (screenW - safe.xMax) * scaleX);
            float padTop = Mathf.Max(14f, (screenH - safe.yMax) * scaleY);
            float padBottom = Mathf.Max(20f, safe.y * scaleY);

            safeAreaContainer.style.paddingLeft = padLeft;
            safeAreaContainer.style.paddingRight = padRight;
            safeAreaContainer.style.paddingTop = padTop;
            safeAreaContainer.style.paddingBottom = padBottom;

            if (destinationFooter != null)
            {
                destinationFooter.style.paddingBottom = padBottom;
            }
            if (settingsFooter != null)
            {
                settingsFooter.style.paddingBottom = padBottom;
            }
            if (mapEditorFooter != null)
            {
                mapEditorFooter.style.paddingBottom = padBottom;
            }
            if (poiFormFooter != null)
            {
                poiFormFooter.style.paddingBottom = padBottom;
            }
        }

        /// <summary>Backward-compatible state update signature used by tests and simple callers.</summary>
        public void UpdateState(
            bool isTrackingAccepted,
            bool isStableTracking,
            bool isNavigating,
            string destinationLabel,
            float routeRemainingMeters,
            string companionMessage,
            bool isArrived,
            string diagnosticsInfo)
        {
            var richState = new NavigationUiState
            {
                IsTrackingAccepted = isTrackingAccepted,
                IsStableTracking = isStableTracking,
                IsNavigating = isNavigating,
                DestinationLabel = destinationLabel,
                RouteRemainingMeters = routeRemainingMeters,
                TotalRouteMeters = Mathf.Max(routeRemainingMeters, 1f),
                CompanionMessage = companionMessage,
                IsArrived = isArrived,
                DiagnosticsText = diagnosticsInfo,
                BootstrapState = NavigationBootstrapState.ManifestLoaded,
                TotalPoiCount = latestPois.Count,
                EffectiveOcclusionMode = experience != null ? experience.OcclusionMode : ArOcclusionQualityMode.Off,
            };
            UpdateState(in richState);
        }

        public void UpdateState(in NavigationUiState state)
        {
            if (rootElement == null) return;

            ApplySafeArea();

            bool showIdle = !state.IsTrackingAccepted;
            bool showLocalizing = state.IsTrackingAccepted && !state.IsStableTracking;
            bool showNavigating = state.IsTrackingAccepted && state.IsStableTracking && state.IsNavigating && !state.IsAuthoringMode;
            bool showAuthoring = state.IsTrackingAccepted && state.IsStableTracking && state.IsAuthoringMode;

            SetDisplay(launcherBackground, showIdle);
            SetDisplay(idleHeader, showIdle);
            SetDisplay(destinationDock, showIdle);
            SetDisplay(readinessCard, showIdle);

            SetDisplay(localizingHeader, showLocalizing);
            SetDisplay(localizingDock, showLocalizing);
            SetDisplay(localizingReticle, showLocalizing);

            SetDisplay(navigatingHeader, showNavigating);
            SetDisplay(companionDock, showNavigating);

            SetDisplay(authoringHeader, showAuthoring);
            SetDisplay(authoringDock, showAuthoring);

            isBusyConnecting = state.IsBusyConnecting;

            if (showIdle)
            {
                UpdateIdleReadiness(in state);
            }

            if (showLocalizing)
            {
                if (localizingTargetLabel != null)
                {
                    if (state.IsAuthoringMode)
                    {
                        localizingTargetLabel.text = "Định vị để thêm điểm đến";
                    }
                    else
                    {
                        localizingTargetLabel.text = string.IsNullOrEmpty(state.DestinationLabel)
                            ? "Đang khóa không gian 3D…"
                            : $"Đích: {state.DestinationLabel}";
                    }
                }

                if (lblLocalizingStatus != null)
                {
                    lblLocalizingStatus.text = !string.IsNullOrEmpty(state.StatusMessage)
                        ? state.StatusMessage
                        : "Đang đối chiếu không gian VPS…";
                }

                if (lblLocalizingHint != null)
                {
                    lblLocalizingHint.text = state.IsBusyConnecting
                        ? "Đang đồng bộ dữ liệu vị trí từ máy chủ…"
                        : "Di chuyển chậm camera về phía hành lang hoặc biển hiệu";
                }
            }

            if (showNavigating)
            {
                UpdateNavigatingHud(in state);
            }

            UpdateRobotSpeechBubble(in state, showNavigating);

            if (showAuthoring)
            {
                UpdateAuthoringHud(in state);
            }

            UpdateStartButtonState();
            UpdateDiagnosticsAndTelemetry(in state);
        }

        private void UpdateAuthoringHud(in NavigationUiState state)
        {
            if (authoringCoordsLabel != null)
            {
                authoringCoordsLabel.text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "X: {0:0.00} m · Z: {1:0.00} m",
                    state.CurrentAnchorLocalPos.x,
                    state.CurrentAnchorLocalPos.z);
            }

            if (authoringSnapPill != null)
            {
                authoringSnapPill.text = state.NearestGraphDistanceMeters < 99f
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "Cách mạng {0:0.0} m", state.NearestGraphDistanceMeters)
                    : "Chưa có điểm nối";
            }

            if (authoringStateBadge != null)
            {
                authoringStateBadge.text = state.IsRecordingWalk
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "ĐANG GHI LỘ TRÌNH · {0:0.0} m ({1} điểm)", state.RecordedWalkMeters, state.RecordedPointCount)
                    : "SẴN SÀNG THÊM ĐIỂM";
                authoringStateBadge.EnableInClassList("companion-state-badge--arrived", state.IsRecordingWalk);
                authoringStateBadge.EnableInClassList("companion-state-badge--walking", !state.IsRecordingWalk);
            }

            if (authoringAvatarBox != null)
            {
                authoringAvatarBox.EnableInClassList("companion-avatar-box--arrived", state.IsRecordingWalk);
                authoringAvatarBox.EnableInClassList("companion-avatar-box--walking", !state.IsRecordingWalk);
            }

            if (authoringStatusMessage != null)
            {
                authoringStatusMessage.text = !string.IsNullOrEmpty(state.StatusMessage)
                    ? state.StatusMessage
                    : (state.IsRecordingWalk
                        ? "Đi bộ tới vị trí điểm đến mới rồi bấm Đặt điểm tại đây."
                        : "Bấm Bắt đầu ghi đường rồi đi tới nơi, hoặc Đặt điểm tại đây.");
            }

            if (btnAuthoringToggleWalk != null)
            {
                btnAuthoringToggleWalk.text = state.IsRecordingWalk ? "Hủy đoạn ghi" : "Bắt đầu ghi đường";
                btnAuthoringToggleWalk.EnableInClassList("btn-danger-glass", state.IsRecordingWalk);
                btnAuthoringToggleWalk.EnableInClassList("btn-secondary-light", !state.IsRecordingWalk);
            }
        }

        private void UpdateIdleReadiness(in NavigationUiState state)
        {
            if (lblLauncherPoiCount != null)
            {
                int count = Mathf.Max(state.TotalPoiCount, latestPois.Count);
                lblLauncherPoiCount.text = $"{count} điểm";
            }

            bool hasError = state.BootstrapState == NavigationBootstrapState.Error ||
                            state.BootstrapState == NavigationBootstrapState.NoActiveMap;

            SetDisplay(readinessErrorRow, hasError);
            if (hasError && lblReadinessError != null)
            {
                lblReadinessError.text = !string.IsNullOrEmpty(state.BootstrapError)
                    ? state.BootstrapError
                    : "Chưa có bản đồ hoạt động cho khu vực này.";
            }

            if (readinessSubtitle != null && !string.IsNullOrEmpty(state.StatusMessage) && state.IsBusyConnecting)
            {
                readinessSubtitle.text = state.StatusMessage;
            }
            else if (readinessSubtitle != null)
            {
                readinessSubtitle.text = "Chọn điểm đến để bật camera định vị không gian VPS, sau đó đi theo Robot và dải dẫn hướng 3D trên sàn.";
            }
        }

        private void UpdateNavigatingHud(in NavigationUiState state)
        {
            if (navDestinationName != null)
            {
                navDestinationName.text = string.IsNullOrEmpty(state.DestinationLabel)
                    ? "Điểm đến"
                    : state.DestinationLabel;
            }

            if (navDistanceRemaining != null)
            {
                if (state.IsArrived)
                {
                    navDistanceRemaining.text = "Tại đích";
                }
                else
                {
                    float remaining = Mathf.Max(0f, state.RouteRemainingMeters);
                    navDistanceRemaining.text = remaining < 10f
                        ? $"{remaining:0.0} m"
                        : $"{remaining:0} m";
                }
            }

            // Header arrival accent transitions
            if (navPinBadge != null)
            {
                navPinBadge.EnableInClassList("nav-pin-badge--arrived", state.IsArrived);
            }
            if (distancePill != null)
            {
                distancePill.EnableInClassList("distance-pill--arrived", state.IsArrived);
            }
            if (navDistanceRemaining != null)
            {
                navDistanceRemaining.EnableInClassList("distance-pill-text--arrived", state.IsArrived);
            }
            if (navStateEyebrow != null)
            {
                navStateEyebrow.text = state.IsArrived ? "ĐÃ TỚI ĐIỂM ĐÍCH" : "ĐANG DẪN ĐƯỜNG";
                navStateEyebrow.EnableInClassList("eyebrow-tag--emerald", state.IsArrived);
                navStateEyebrow.EnableInClassList("eyebrow-tag--cyan", !state.IsArrived);
            }

            // Route progress bar
            if (navProgressFill != null)
            {
                float progress01;
                if (state.IsArrived)
                {
                    progress01 = 1f;
                }
                else if (state.TotalRouteMeters > 0.25f)
                {
                    progress01 = Mathf.Clamp(1f - (state.RouteRemainingMeters / state.TotalRouteMeters), 0.06f, 0.96f);
                }
                else
                {
                    progress01 = 0.08f;
                }
                navProgressFill.style.width = new Length(progress01 * 100f, LengthUnit.Percent);
                navProgressFill.EnableInClassList("nav-progress-fill--arrived", state.IsArrived);
            }

            // Companion status & dynamic state badge
            if (companionStatusMessage != null)
            {
                companionStatusMessage.text = string.IsNullOrEmpty(state.CompanionMessage)
                    ? "Đi theo Robot và dải dẫn hướng trên sàn."
                    : state.CompanionMessage;
            }

            UpdateCompanionVisualState(in state);

            SetDisplay(btnFinishNavigation, state.IsArrived);
            SetDisplay(btnChangeDestination, !state.IsArrived);
        }

        private void UpdateRobotSpeechBubble(in NavigationUiState state, bool showNavigating)
        {
            // Keep notification strictly hidden when camera is not pointing at the Robot.
            bool visible = showNavigating && state.IsArrived && state.HasRobotBubbleAnchor && state.IsRobotInFrontOfCamera;
            SetDisplay(robotSpeechLayer, visible);
            if (!visible || robotSpeechBubble == null)
            {
                wasRobotBubbleVisible = false;
                return;
            }

            if (btnRobotBubbleSpeak != null)
            {
                btnRobotBubbleSpeak.EnableInClassList("robot-speak-btn--speaking", state.IsTtsSpeaking);
            }

            bool hasDescription = !string.IsNullOrWhiteSpace(state.DestinationDescription);
            if (lblRobotBubbleTitle != null)
            {
                lblRobotBubbleTitle.text = hasDescription
                    ? (string.IsNullOrEmpty(state.DestinationLabel) ? "ROBOT GIỚI THIỆU" : $"ROBOT · {state.DestinationLabel.ToUpperInvariant()}")
                    : "ĐÃ TỚI ĐIỂM ĐÍCH";
            }

            if (lblRobotBubbleBody != null)
            {
                lblRobotBubbleBody.text = hasDescription
                    ? state.DestinationDescription.Trim()
                    : (string.IsNullOrEmpty(state.DestinationLabel)
                        ? "Chuyến đi đã hoàn tất!"
                        : $"Bạn đã đến {state.DestinationLabel}!");
            }

            float panelW = rootElement != null ? rootElement.layout.width : 390f;
            float panelH = rootElement != null ? rootElement.layout.height : 844f;
            if (float.IsNaN(panelW) || panelW <= 1f) panelW = 390f;
            if (float.IsNaN(panelH) || panelH <= 1f) panelH = 844f;

            float bubbleW = robotSpeechBubble.layout.width;
            float bubbleH = robotSpeechBubble.layout.height;
            if (float.IsNaN(bubbleW) || bubbleW <= 1f) bubbleW = 240f;
            if (float.IsNaN(bubbleH) || bubbleH <= 1f) bubbleH = 92f;

            const float marginX = 14f;
            float minTop = 96f;
            float maxTop = Mathf.Max(minTop, panelH - bubbleH - 134f);

            float anchorX = state.RobotBubbleViewportPos.x * panelW;
            float anchorY = state.RobotBubbleViewportPos.y * panelH;

            float targetLeft = Mathf.Clamp(anchorX - bubbleW * 0.5f, marginX, Mathf.Max(marginX, panelW - bubbleW - marginX));
            float targetTop = Mathf.Clamp(anchorY - bubbleH - 12f, minTop, maxTop);

            if (!wasRobotBubbleVisible || Time.deltaTime <= 0f)
            {
                smoothedRobotBubblePos = new Vector2(targetLeft, targetTop);
            }
            else
            {
                float t = Mathf.Clamp01(Time.deltaTime * 16f);
                smoothedRobotBubblePos = Vector2.Lerp(smoothedRobotBubblePos, new Vector2(targetLeft, targetTop), t);
            }
            wasRobotBubbleVisible = true;

            robotSpeechBubble.style.left = smoothedRobotBubblePos.x;
            robotSpeechBubble.style.top = smoothedRobotBubblePos.y;

            SetDisplay(robotSpeechTail, true);
            if (robotSpeechTail != null)
            {
                float tailLeft = Mathf.Clamp(anchorX - smoothedRobotBubblePos.x - 6f, 16f, Mathf.Max(16f, bubbleW - 28f));
                robotSpeechTail.style.left = tailLeft;
            }
        }

        private void UpdateCompanionVisualState(in NavigationUiState state)
        {
            if (companionAvatarBox != null)
            {
                companionAvatarBox.RemoveFromClassList("companion-avatar-box--walking");
                companionAvatarBox.RemoveFromClassList("companion-avatar-box--waiting");
                companionAvatarBox.RemoveFromClassList("companion-avatar-box--reroute");
                companionAvatarBox.RemoveFromClassList("companion-avatar-box--arrived");
            }

            if (companionStateBadge != null)
            {
                companionStateBadge.RemoveFromClassList("companion-state-badge--walking");
                companionStateBadge.RemoveFromClassList("companion-state-badge--waiting");
                companionStateBadge.RemoveFromClassList("companion-state-badge--reroute");
                companionStateBadge.RemoveFromClassList("companion-state-badge--arrived");
            }

            string modClass;
            string badgeText;
            if (state.IsArrived)
            {
                modClass = "arrived";
                badgeText = "ĐÃ TỚI ĐÍCH";
            }
            else if (state.IsCompanionRerouting)
            {
                modClass = "reroute";
                badgeText = "ĐANG TÍNH LẠI TUYẾN";
            }
            else if (state.IsCompanionWaiting)
            {
                modClass = "waiting";
                badgeText = "ROBOT ĐANG ĐỢI BẠN";
            }
            else
            {
                modClass = "walking";
                badgeText = "ROBOT ĐANG DẪN ĐƯỜNG";
            }

            companionAvatarBox?.AddToClassList($"companion-avatar-box--{modClass}");
            if (companionStateBadge != null)
            {
                companionStateBadge.AddToClassList($"companion-state-badge--{modClass}");
                companionStateBadge.text = badgeText;
            }
        }

        private void UpdateDiagnosticsAndTelemetry(in NavigationUiState state)
        {
            if (lblDiagnostics != null && !string.IsNullOrEmpty(state.DiagnosticsText))
            {
                lblDiagnostics.text = state.DiagnosticsText;
            }

            if (!isSettingsOpen) return;

            if (lblMetricFps != null)
            {
                lblMetricFps.text = state.Fps > 0.5f ? $"{state.Fps:0}" : "--";
            }
            if (lblMetricFrame != null)
            {
                lblMetricFrame.text = state.FrameTimeMs > 0.05f ? $"{state.FrameTimeMs:0.0} ms" : "-- ms";
            }
            if (lblMetricRam != null)
            {
                lblMetricRam.text = state.MemoryMb > 0.5f ? $"{state.MemoryMb:0} MB" : "-- MB";
            }
            if (lblMetricDepth != null)
            {
                lblMetricDepth.text = state.DepthAvailable
                    ? GetCompactOcclusionLabel(state.EffectiveOcclusionMode)
                    : "Chưa bật";
            }

            if (lblOcclusionStatus != null)
            {
                string fallback = experience != null ? experience.OcclusionFallbackReason : string.Empty;
                string diagnostics = experience != null ? experience.OcclusionDiagnostics : string.Empty;
                string status = diagnostics;
                if (!string.IsNullOrEmpty(fallback))
                {
                    status = string.IsNullOrEmpty(status) ? fallback : status + "\n" + fallback;
                }
                lblOcclusionStatus.text = status;
            }

            if (!string.IsNullOrEmpty(state.TtsStatusSummary))
            {
                UpdateTtsStatusLabel(state.TtsStatusSummary);
            }
        }

        private void UpdateTtsStatusLabel(string statusSummary)
        {
            if (lblTtsStatus == null) return;
            lblTtsStatus.text = string.IsNullOrWhiteSpace(statusSummary) ? "Google Android TTS" : statusSummary;
        }

        private static string GetCompactOcclusionLabel(ArOcclusionQualityMode mode)
        {
            switch (mode)
            {
                case ArOcclusionQualityMode.Off: return "Tắt";
                case ArOcclusionQualityMode.Fallback: return "Cơ bản";
                case ArOcclusionQualityMode.Balanced: return "Cân bằng";
                case ArOcclusionQualityMode.HighQuality: return "Chi tiết";
                default: return mode.ToString();
            }
        }

        public void OpenDestinationPickerModal()
        {
            if (modalDestinationPicker == null) return;
            isPickerOpen = true;
            destinationSearchInput?.SetValueWithoutNotify(string.Empty);
            UpdateClearSearchVisibility(string.Empty);
            modalDestinationPicker.style.display = DisplayStyle.Flex;
            modalDestinationPicker.RemoveFromClassList("modal-overlay--hidden");
            UpdateStartButtonState();
            ApplySafeArea();
        }

        public void CloseDestinationPickerModal()
        {
            if (modalDestinationPicker == null) return;
            isPickerOpen = false;
            modalDestinationPicker.AddToClassList("modal-overlay--hidden");
            modalDestinationPicker.style.display = DisplayStyle.None;
        }

        public void PopulatePois(IReadOnlyList<PublishedGraphPoiV2> pois, string selectedPoiId)
        {
            currentSelectedPoiId = selectedPoiId;
            latestPois.Clear();

            if (pois != null)
            {
                for (int i = 0; i < pois.Count; i++)
                {
                    if (pois[i] != null) latestPois.Add(pois[i]);
                }
            }

            bool isSearchEmpty = destinationSearchInput == null || string.IsNullOrEmpty(destinationSearchInput.value);
            if (isSearchEmpty || knownCategories.Count == 0)
            {
                RebuildKnownCategories(latestPois);
                RebuildCategoryFilterChips();
                RebuildQuickPickChips(latestPois);
            }

            if (destinationTotalBadge != null)
            {
                destinationTotalBadge.text = $"{latestPois.Count} điểm";
            }
            if (lblLauncherPoiCount != null)
            {
                lblLauncherPoiCount.text = $"{latestPois.Count} điểm";
            }
            if (lblLauncherPoiSummary != null)
            {
                lblLauncherPoiSummary.text = latestPois.Count > 0
                    ? $"Có sẵn {latestPois.Count} phòng & tiện ích để dẫn đường"
                    : "Đang chờ dữ liệu điểm đến từ bản đồ";
            }

            RenderFilteredPois();
        }

        private void RebuildKnownCategories(IReadOnlyList<PublishedGraphPoiV2> pois)
        {
            knownCategories.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pois.Count; i++)
            {
                string cat = FormatCategoryLabel(pois[i].Category);
                if (!string.IsNullOrEmpty(cat) && seen.Add(cat))
                {
                    knownCategories.Add(cat);
                }
            }

            if (!string.Equals(activeCategoryFilter, AllCategoriesKey, StringComparison.Ordinal) &&
                !seen.Contains(activeCategoryFilter))
            {
                activeCategoryFilter = AllCategoriesKey;
            }
        }

        private void RebuildCategoryFilterChips()
        {
            if (destinationFilterContainer == null) return;
            destinationFilterContainer.Clear();

            if (knownCategories.Count <= 1)
            {
                SetDisplay(destinationFilterContainer.parent, false);
                return;
            }

            SetDisplay(destinationFilterContainer.parent, true);
            AddFilterChip("Tất cả", AllCategoriesKey);
            for (int i = 0; i < knownCategories.Count; i++)
            {
                string cat = knownCategories[i];
                AddFilterChip(cat, cat);
            }
        }

        private void AddFilterChip(string title, string key)
        {
            var chip = new Button { text = title };
            chip.AddToClassList("filter-chip");
            chip.EnableInClassList("filter-chip--active", string.Equals(activeCategoryFilter, key, StringComparison.Ordinal));
            chip.clicked += () =>
            {
                activeCategoryFilter = key;
                RebuildCategoryFilterChips();
                RenderFilteredPois();
            };
            destinationFilterContainer.Add(chip);
        }

        private void RebuildQuickPickChips(IReadOnlyList<PublishedGraphPoiV2> pois)
        {
            if (idleQuickPicks == null || idleQuickPicksContainer == null) return;
            idleQuickPicksContainer.Clear();

            if (pois == null || pois.Count == 0)
            {
                SetDisplay(idleQuickPicks, false);
                return;
            }

            SetDisplay(idleQuickPicks, true);
            int maxChips = Mathf.Min(3, pois.Count);
            for (int i = 0; i < maxChips; i++)
            {
                var poi = pois[i];
                var chip = new Button();
                chip.AddToClassList("quick-pick-chip");
                chip.pickingMode = PickingMode.Position;

                var icon = new VisualElement();
                icon.AddToClassList("vec-icon");
                icon.AddToClassList("vec-icon--14");
                icon.AddToClassList("vec-icon--brand");
                icon.pickingMode = PickingMode.Ignore;
                var iconTex = NavigationUiVectorIcons.Get(GetPoiIconKind(poi.Category));
                if (iconTex != null) icon.style.backgroundImage = new StyleBackground(iconTex);

                var lbl = new Label(SiteGraphV2NavigationExperience.Capitalize(poi.Label));
                lbl.AddToClassList("quick-pick-label");
                lbl.pickingMode = PickingMode.Ignore;

                chip.Add(icon);
                chip.Add(lbl);

                string poiId = poi.Id;
                chip.clicked += () =>
                {
                    if (experience == null || isBusyConnecting) return;
                    currentSelectedPoiId = poiId;
                    experience.OnPoiSelected(poiId);
                    experience.OnStartNavigationRequested();
                };

                idleQuickPicksContainer.Add(chip);
            }
        }

        private void RenderFilteredPois()
        {
            if (destinationItemsContainer == null) return;

            destinationItemsContainer.Clear();
            poiRowById.Clear();

            var filtered = new List<PublishedGraphPoiV2>();
            bool filterAll = string.Equals(activeCategoryFilter, AllCategoriesKey, StringComparison.Ordinal);

            for (int i = 0; i < latestPois.Count; i++)
            {
                var poi = latestPois[i];
                if (!filterAll && !string.Equals(FormatCategoryLabel(poi.Category), activeCategoryFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                filtered.Add(poi);
            }

            var checkTex = NavigationUiVectorIcons.Get(NavigationUiVectorIcons.IconKind.Check);
            string selectedDisplayLabel = null;

            for (int i = 0; i < filtered.Count; i++)
            {
                var poi = filtered[i];
                string poiId = poi.Id;
                bool isSelected = !string.IsNullOrEmpty(currentSelectedPoiId) &&
                                  string.Equals(poiId, currentSelectedPoiId, StringComparison.Ordinal);
                string poiTitle = SiteGraphV2NavigationExperience.Capitalize(poi.Label);
                if (isSelected) selectedDisplayLabel = poiTitle;

                var card = new VisualElement();
                card.AddToClassList("poi-item");
                if (isSelected) card.AddToClassList("poi-item--selected");
                card.pickingMode = PickingMode.Position;

                var iconBox = new VisualElement();
                iconBox.name = "poi-icon-box";
                iconBox.AddToClassList("poi-icon-box");
                if (isSelected) iconBox.AddToClassList("poi-icon-box--selected");
                iconBox.pickingMode = PickingMode.Ignore;

                var iconEl = new VisualElement();
                iconEl.name = "poi-icon-glyph";
                iconEl.AddToClassList("vec-icon");
                iconEl.AddToClassList("vec-icon--20");
                iconEl.AddToClassList(isSelected ? "vec-icon--brand" : "vec-icon--slate");
                iconEl.pickingMode = PickingMode.Ignore;
                var glyphTex = NavigationUiVectorIcons.Get(GetPoiIconKind(poi.Category));
                if (glyphTex != null) iconEl.style.backgroundImage = new StyleBackground(glyphTex);
                iconBox.Add(iconEl);

                var textCol = new VisualElement();
                textCol.AddToClassList("poi-text-col");
                textCol.pickingMode = PickingMode.Ignore;

                var titleLbl = new Label(poiTitle);
                titleLbl.AddToClassList("poi-title");
                titleLbl.pickingMode = PickingMode.Ignore;

                var catLbl = new Label(FormatCategoryLabel(poi.Category));
                catLbl.AddToClassList("poi-category");
                catLbl.pickingMode = PickingMode.Ignore;

                textCol.Add(titleLbl);
                textCol.Add(catLbl);
                TryAddDescriptionPreview(textCol, poi.Description);

                var radio = new VisualElement();
                radio.name = "poi-radio";
                radio.AddToClassList("poi-radio");
                if (isSelected) radio.AddToClassList("poi-radio--selected");
                radio.pickingMode = PickingMode.Ignore;

                var radioCheck = new VisualElement();
                radioCheck.name = "poi-radio-check";
                radioCheck.AddToClassList("vec-icon");
                radioCheck.AddToClassList("vec-icon--12");
                radioCheck.AddToClassList("vec-icon--white");
                if (checkTex != null) radioCheck.style.backgroundImage = new StyleBackground(checkTex);
                radioCheck.style.display = isSelected ? DisplayStyle.Flex : DisplayStyle.None;
                radioCheck.pickingMode = PickingMode.Ignore;
                radio.Add(radioCheck);

                card.Add(iconBox);
                card.Add(textCol);
                card.Add(radio);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    currentSelectedPoiId = poiId;
                    experience?.OnPoiSelected(poiId);
                    UpdatePoiSelectionHighlights();
                });

                destinationItemsContainer.Add(card);
                poiRowById[poiId] = card;
            }

            if (selectedDisplayLabel == null && !string.IsNullOrEmpty(currentSelectedPoiId))
            {
                for (int i = 0; i < latestPois.Count; i++)
                {
                    if (string.Equals(latestPois[i].Id, currentSelectedPoiId, StringComparison.Ordinal))
                    {
                        selectedDisplayLabel = SiteGraphV2NavigationExperience.Capitalize(latestPois[i].Label);
                        break;
                    }
                }
            }

            if (destinationSelectionLabel != null)
            {
                destinationSelectionLabel.text = string.IsNullOrEmpty(selectedDisplayLabel)
                    ? "Chưa chọn điểm đến"
                    : $"Đã chọn: {selectedDisplayLabel}";
            }

            SetDisplay(destinationEmptyState, filtered.Count == 0);
            UpdateStartButtonState();
        }

        private void UpdatePoiSelectionHighlights()
        {
            string selectedTitle = null;
            for (int i = 0; i < latestPois.Count; i++)
            {
                if (string.Equals(latestPois[i].Id, currentSelectedPoiId, StringComparison.Ordinal))
                {
                    selectedTitle = SiteGraphV2NavigationExperience.Capitalize(latestPois[i].Label);
                    break;
                }
            }

            foreach (var kv in poiRowById)
            {
                bool isSel = string.Equals(kv.Key, currentSelectedPoiId, StringComparison.Ordinal);
                var card = kv.Value;
                card.EnableInClassList("poi-item--selected", isSel);

                var iconBox = card.Q<VisualElement>("poi-icon-box");
                iconBox?.EnableInClassList("poi-icon-box--selected", isSel);

                var glyph = card.Q<VisualElement>("poi-icon-glyph");
                if (glyph != null)
                {
                    glyph.EnableInClassList("vec-icon--brand", isSel);
                    glyph.EnableInClassList("vec-icon--slate", !isSel);
                }

                var radio = card.Q<VisualElement>("poi-radio");
                radio?.EnableInClassList("poi-radio--selected", isSel);

                var check = card.Q<VisualElement>("poi-radio-check");
                SetDisplay(check, isSel);
            }

            if (destinationSelectionLabel != null)
            {
                destinationSelectionLabel.text = string.IsNullOrEmpty(selectedTitle)
                    ? "Chưa chọn điểm đến"
                    : $"Đã chọn: {selectedTitle}";
            }

            UpdateStartButtonState();
        }

        private void UpdateStartButtonState()
        {
            if (btnStartNavigation == null) return;
            bool hasSelection = !string.IsNullOrEmpty(currentSelectedPoiId);
            btnStartNavigation.SetEnabled(hasSelection && !isBusyConnecting);

            if (lblStartNavigation != null)
            {
                lblStartNavigation.text = isBusyConnecting
                    ? "Đang kết nối VPS…"
                    : "Bắt đầu dẫn đường AR";
            }
        }

        private void UpdateClearSearchVisibility(string query)
        {
            SetDisplay(btnClearSearch, !string.IsNullOrEmpty(query));
        }

        public void OpenSettingsModal()
        {
            if (modalSettings == null) return;
            isSettingsOpen = true;
            SyncSettingsUi();
            modalSettings.style.display = DisplayStyle.Flex;
            modalSettings.RemoveFromClassList("modal-overlay--hidden");
            ApplySafeArea();
        }

        public void CloseSettingsModal()
        {
            if (modalSettings == null) return;
            isSettingsOpen = false;
            modalSettings.AddToClassList("modal-overlay--hidden");
            modalSettings.style.display = DisplayStyle.None;
        }

        public void SyncSettingsUi()
        {
            if (experience == null || rootElement == null) return;

            isSyncingSettings = true;
            try
            {
                if (sliderRouteHue != null)
                {
                    sliderRouteHue.SetValueWithoutNotify(experience.RouteHue);
                    if (valRouteHue != null) valRouteHue.text = $"{experience.RouteHue:0}°";
                }

                if (sliderRouteWidth != null)
                {
                    sliderRouteWidth.SetValueWithoutNotify(experience.RouteLineWidth);
                    if (valRouteWidth != null) valRouteWidth.text = $"{experience.RouteLineWidth:0.00} m";
                }

                if (sliderRouteOpacity != null)
                {
                    sliderRouteOpacity.SetValueWithoutNotify(experience.RouteLineOpacity);
                    if (valRouteOpacity != null) valRouteOpacity.text = $"{Mathf.RoundToInt(experience.RouteLineOpacity * 100f)}%";
                }

                if (sliderRouteDepthBias != null)
                {
                    sliderRouteDepthBias.SetValueWithoutNotify(experience.RouteDepthBias);
                    if (valRouteDepthBias != null) valRouteDepthBias.text = $"{experience.RouteDepthBias:0.00} m";
                }

                if (sliderModelScale != null)
                {
                    sliderModelScale.SetValueWithoutNotify(experience.CompanionModelScale);
                    if (valModelScale != null) valModelScale.text = $"{experience.CompanionModelScale:0.00}×";
                }

                if (sliderModelBrightness != null)
                {
                    sliderModelBrightness.SetValueWithoutNotify(experience.CompanionBrightness);
                    if (valModelBrightness != null) valModelBrightness.text = $"{experience.CompanionBrightness:0.00}";
                }

                if (sliderLeadDistance != null)
                {
                    sliderLeadDistance.SetValueWithoutNotify(experience.CompanionLeadDistance);
                    if (valLeadDistance != null) valLeadDistance.text = $"{experience.CompanionLeadDistance:0.00} m";
                }

                if (sliderGroundDrop != null)
                {
                    sliderGroundDrop.SetValueWithoutNotify(experience.CompanionGroundDrop);
                    if (valGroundDrop != null) valGroundDrop.text = $"{experience.CompanionGroundDrop:0.00} m";
                }

                toggleSmoothEdges?.SetValueWithoutNotify(experience.SmoothEdgesOcclusion);
                toggleStableOcclusionDistance?.SetValueWithoutNotify(experience.StabilizedOcclusionDistance);
                toggleSemantic?.SetValueWithoutNotify(experience.SemanticSuppressionOcclusion);
                toggleSuppressGround?.SetValueWithoutNotify(experience.SuppressGroundOcclusion);
                toggleDepthVis?.SetValueWithoutNotify(experience.DepthVisualizationOcclusion);
                toggleMeshDebug?.SetValueWithoutNotify(experience.MeshDebugOcclusion);
                toggleTtsEnabled?.SetValueWithoutNotify(experience.TtsEnabled);
                if (sliderTtsRate != null)
                {
                    sliderTtsRate.SetValueWithoutNotify(experience.TtsSpeakingRate);
                    if (valTtsRate != null) valTtsRate.text = $"{experience.TtsSpeakingRate:0.00}×";
                }
                ttsApiKeyInput?.SetValueWithoutNotify(experience.TtsCloudApiKey ?? string.Empty);
                UpdateTtsStatusLabel(experience.TtsStatusSummary);

                UpdateSwatchSelectionVisuals();
                UpdateOcclusionCardSelectionVisuals();
                UpdateRoutePreview();
            }
            finally
            {
                isSyncingSettings = false;
            }
        }

        private void UpdateSwatchSelectionVisuals()
        {
            if (experience == null) return;
            int activeIdx = experience.RouteColorIndex;
            for (int i = 0; i < colorChipElements.Count; i++)
            {
                bool isSel = i == activeIdx;
                var chip = colorChipElements[i];
                chip.EnableInClassList("color-swatch-chip--selected", isSel);
                var check = chip.Q<VisualElement>("swatch-check");
                SetDisplay(check, isSel);
            }
        }

        private void UpdateOcclusionCardSelectionVisuals()
        {
            if (experience == null) return;
            var currentMode = experience.OcclusionMode;
            for (int i = 0; i < occlusionCardElements.Count; i++)
            {
                var card = occlusionCardElements[i];
                bool isSel = card.userData is ArOcclusionQualityMode mode && mode == currentMode;
                card.EnableInClassList("occlusion-card--selected", isSel);
            }
        }

        private void UpdateRoutePreview()
        {
            if (experience == null) return;

            Color c;
            Color[] palette = SiteGraphV2NavigationExperience.PaletteColors;
            int idx = experience.RouteColorIndex;
            if (idx >= 0 && idx < palette.Length)
            {
                c = palette[idx];
            }
            else
            {
                c = Color.HSVToRGB(Mathf.Clamp01(experience.RouteHue / 360f), 0.92f, 0.98f);
            }

            if (routePreviewEnd != null)
            {
                routePreviewEnd.style.backgroundColor = c;
            }

            if (routePreviewLine != null)
            {
                Color lineCol = c;
                lineCol.a = Mathf.Clamp(experience.RouteLineOpacity, 0.2f, 1f);
                routePreviewLine.style.backgroundColor = lineCol;

                float normalizedW = Mathf.InverseLerp(0.04f, 0.50f, experience.RouteLineWidth);
                float pxHeight = Mathf.Lerp(3f, 13f, normalizedW);
                routePreviewLine.style.height = pxHeight;
                routePreviewLine.style.borderTopLeftRadius = pxHeight * 0.5f;
                routePreviewLine.style.borderTopRightRadius = pxHeight * 0.5f;
                routePreviewLine.style.borderBottomLeftRadius = pxHeight * 0.5f;
                routePreviewLine.style.borderBottomRightRadius = pxHeight * 0.5f;
            }
        }

        private static string FormatCategoryLabel(string rawCategory)
        {
            string formatted = SiteGraphV2NavigationExperience.FormatPoiCategory(rawCategory);
            return string.IsNullOrEmpty(formatted) ? "Điểm đến" : formatted;
        }

        private static NavigationUiVectorIcons.IconKind GetPoiIconKind(string rawCategory)
        {
            if (string.IsNullOrEmpty(rawCategory)) return NavigationUiVectorIcons.IconKind.Pin;

            string lower = rawCategory.ToLowerInvariant();
            if (lower.Contains("room") || lower.Contains("office") || lower.Contains("meeting") ||
                lower.Contains("class") || lower.Contains("lab") || lower.Contains("phòng") ||
                lower.Contains("door") || lower.Contains("cửa"))
            {
                return NavigationUiVectorIcons.IconKind.Door;
            }

            if (lower.Contains("restroom") || lower.Contains("toilet") || lower.Contains("wc") ||
                lower.Contains("elev") || lower.Contains("lift") || lower.Contains("stair") ||
                lower.Contains("cafe") || lower.Contains("service") || lower.Contains("amen") ||
                lower.Contains("recept") || lower.Contains("lobby") || lower.Contains("tiện") ||
                lower.Contains("thang") || lower.Contains("sảnh"))
            {
                return NavigationUiVectorIcons.IconKind.Amenity;
            }

            return NavigationUiVectorIcons.IconKind.Pin;
        }

        public void OpenMapEditorModal(IReadOnlyList<PublishedGraphPoiV2> pois, bool isUsingLocalOverride)
        {
            if (modalMapEditor == null) return;
            isMapEditorOpen = true;
            resetLocalConfirmPending = false;
            if (btnResetLocalManifest != null) btnResetLocalManifest.text = "Khôi phục gốc";
            ShowMapEditorFeedback(null, false);
            RefreshMapEditorList(pois, isUsingLocalOverride, null, false);
            modalMapEditor.style.display = DisplayStyle.Flex;
            modalMapEditor.RemoveFromClassList("modal-overlay--hidden");
            ApplySafeArea();
        }

        public void CloseMapEditorModal()
        {
            if (modalMapEditor == null) return;
            isMapEditorOpen = false;
            resetLocalConfirmPending = false;
            if (btnResetLocalManifest != null) btnResetLocalManifest.text = "Khôi phục gốc";
            modalMapEditor.AddToClassList("modal-overlay--hidden");
            modalMapEditor.style.display = DisplayStyle.None;
        }

        public void RefreshMapEditorList(
            IReadOnlyList<PublishedGraphPoiV2> pois,
            bool isUsingLocalOverride,
            string feedbackMessage,
            bool isError)
        {
            if (mapEditorItemsContainer == null) return;
            mapEditorItemsContainer.Clear();

            int count = pois != null ? pois.Count : 0;
            if (mapEditorCountBadge != null) mapEditorCountBadge.text = $"{count} điểm";
            SetDisplay(mapEditorCustomBadge, isUsingLocalOverride);
            if (btnResetLocalManifest != null) btnResetLocalManifest.SetEnabled(isUsingLocalOverride);
            if (feedbackMessage != null) ShowMapEditorFeedback(feedbackMessage, isError);

            if (pois == null) return;

            var editTex = NavigationUiVectorIcons.Get(NavigationUiVectorIcons.IconKind.Edit);
            var trashTex = NavigationUiVectorIcons.Get(NavigationUiVectorIcons.IconKind.Trash);
            bool canDelete = count > 1;

            for (int i = 0; i < pois.Count; i++)
            {
                PublishedGraphPoiV2 poi = pois[i];
                if (poi == null) continue;

                var card = new VisualElement();
                card.AddToClassList("poi-item");
                card.pickingMode = PickingMode.Position;

                var iconBox = new VisualElement();
                iconBox.AddToClassList("poi-icon-box");
                iconBox.pickingMode = PickingMode.Ignore;

                var iconEl = new VisualElement();
                iconEl.AddToClassList("vec-icon");
                iconEl.AddToClassList("vec-icon--20");
                iconEl.AddToClassList("vec-icon--slate");
                iconEl.pickingMode = PickingMode.Ignore;
                var glyphTex = NavigationUiVectorIcons.Get(GetPoiIconKind(poi.Category));
                if (glyphTex != null) iconEl.style.backgroundImage = new StyleBackground(glyphTex);
                iconBox.Add(iconEl);

                var textCol = new VisualElement();
                textCol.AddToClassList("poi-text-col");
                textCol.pickingMode = PickingMode.Ignore;

                var titleLbl = new Label(SiteGraphV2NavigationExperience.Capitalize(poi.Label));
                titleLbl.AddToClassList("poi-title");
                titleLbl.pickingMode = PickingMode.Ignore;

                string coords = poi.AnchorLocal != null
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}) m", poi.AnchorLocal.X, poi.AnchorLocal.Z)
                    : string.Empty;
                var catLbl = new Label($"{FormatCategoryLabel(poi.Category)} · {coords}");
                catLbl.AddToClassList("poi-category");
                catLbl.pickingMode = PickingMode.Ignore;

                textCol.Add(titleLbl);
                textCol.Add(catLbl);
                TryAddDescriptionPreview(textCol, poi.Description);

                var actions = new VisualElement();
                actions.AddToClassList("map-editor-item-actions");
                actions.pickingMode = PickingMode.Position;

                var btnEdit = new Button();
                btnEdit.AddToClassList("btn-row-icon");
                var editIcon = new VisualElement();
                editIcon.AddToClassList("vec-icon");
                editIcon.AddToClassList("vec-icon--16");
                editIcon.AddToClassList("vec-icon--slate");
                editIcon.pickingMode = PickingMode.Ignore;
                if (editTex != null) editIcon.style.backgroundImage = new StyleBackground(editTex);
                btnEdit.Add(editIcon);
                PublishedGraphPoiV2 capturedPoi = poi;
                btnEdit.clicked += () => OpenPoiFormForEdit(capturedPoi);

                var btnDelete = new Button();
                btnDelete.AddToClassList("btn-row-icon");
                btnDelete.AddToClassList("btn-row-icon--danger");
                btnDelete.SetEnabled(canDelete);
                var delIcon = new VisualElement();
                delIcon.AddToClassList("vec-icon");
                delIcon.AddToClassList("vec-icon--16");
                delIcon.AddToClassList("vec-icon--danger");
                delIcon.pickingMode = PickingMode.Ignore;
                if (trashTex != null) delIcon.style.backgroundImage = new StyleBackground(trashTex);
                btnDelete.Add(delIcon);
                string poiIdToDelete = poi.Id;
                btnDelete.clicked += () => experience?.OnDeleteExistingPoiRequested(poiIdToDelete);

                actions.Add(btnEdit);
                actions.Add(btnDelete);

                card.Add(iconBox);
                card.Add(textCol);
                card.Add(actions);
                mapEditorItemsContainer.Add(card);
            }
        }

        public void ShowMapEditorFeedback(string message, bool isError)
        {
            if (mapEditorFeedback == null || lblMapEditorFeedback == null) return;
            bool visible = !string.IsNullOrWhiteSpace(message);
            SetDisplay(mapEditorFeedback, visible);
            mapEditorFeedback.EnableInClassList("map-editor-feedback--error", isError);
            if (visible) lblMapEditorFeedback.text = message;
        }

        public void OpenPoiFormForNew(string connectionSummary)
        {
            if (modalPoiForm == null) return;
            isPoiFormOpen = true;
            editingPoiId = null;
            poiFormSelectedCategory = "room";

            if (lblPoiFormTitle != null) lblPoiFormTitle.text = "Lưu điểm đến mới";
            if (lblPoiFormSubtitle != null) lblPoiFormSubtitle.text = "Đặt tên và phân loại để lưu trực tiếp vào máy.";
            if (lblPoiFormConnection != null)
            {
                lblPoiFormConnection.text = string.IsNullOrEmpty(connectionSummary)
                    ? "Sẽ kết nối vào đồ thị dẫn đường hiện tại."
                    : connectionSummary;
            }
            SetDisplay(poiFormConnectionBox, true);
            SetPoiFormError(null);

            if (poiFormLabelInput != null)
            {
                poiFormLabelInput.SetValueWithoutNotify(string.Empty);
                poiFormLabelInput.Focus();
            }
            if (poiFormDescriptionInput != null)
            {
                poiFormDescriptionInput.SetValueWithoutNotify(string.Empty);
            }
            UpdatePoiFormCategoryHighlights();

            modalPoiForm.style.display = DisplayStyle.Flex;
            modalPoiForm.RemoveFromClassList("modal-overlay--hidden");
            ApplySafeArea();
        }

        public void OpenPoiFormForEdit(PublishedGraphPoiV2 poi)
        {
            if (modalPoiForm == null || poi == null) return;
            isPoiFormOpen = true;
            editingPoiId = poi.Id;
            poiFormSelectedCategory = LocalSiteGraphStore.NormalizeCategory(poi.Category);

            if (lblPoiFormTitle != null) lblPoiFormTitle.text = "Sửa điểm đến";
            if (lblPoiFormSubtitle != null) lblPoiFormSubtitle.text = "Đổi tên, phân loại hoặc lời giới thiệu khi Robot tới nơi.";
            SetDisplay(poiFormConnectionBox, false);
            SetPoiFormError(null);

            if (poiFormLabelInput != null)
            {
                poiFormLabelInput.SetValueWithoutNotify(poi.Label ?? string.Empty);
                poiFormLabelInput.Focus();
            }
            if (poiFormDescriptionInput != null)
            {
                poiFormDescriptionInput.SetValueWithoutNotify(poi.Description ?? string.Empty);
            }
            UpdatePoiFormCategoryHighlights();

            modalPoiForm.style.display = DisplayStyle.Flex;
            modalPoiForm.RemoveFromClassList("modal-overlay--hidden");
            ApplySafeArea();
        }

        public void ClosePoiFormModal()
        {
            if (modalPoiForm == null) return;
            isPoiFormOpen = false;
            editingPoiId = null;
            SetPoiFormError(null);
            modalPoiForm.AddToClassList("modal-overlay--hidden");
            modalPoiForm.style.display = DisplayStyle.None;
        }

        private void HandlePoiFormSubmit()
        {
            if (experience == null) return;
            string rawLabel = poiFormLabelInput != null ? poiFormLabelInput.value : string.Empty;
            string rawDescription = poiFormDescriptionInput != null ? poiFormDescriptionInput.value : string.Empty;
            string error;
            bool ok = string.IsNullOrEmpty(editingPoiId)
                ? experience.OnSaveNewPoiRequested(rawLabel, poiFormSelectedCategory, rawDescription, out error)
                : experience.OnUpdateExistingPoiRequested(editingPoiId, rawLabel, poiFormSelectedCategory, rawDescription, out error);

            if (!ok)
            {
                SetPoiFormError(string.IsNullOrWhiteSpace(error) ? "Không thể lưu điểm đến." : error);
                return;
            }

            ClosePoiFormModal();
        }

        private static void TryAddDescriptionPreview(VisualElement textCol, string description)
        {
            if (textCol == null || string.IsNullOrWhiteSpace(description)) return;
            var descLbl = new Label(description.Trim());
            descLbl.AddToClassList("poi-description-preview");
            descLbl.pickingMode = PickingMode.Ignore;
            textCol.Add(descLbl);
        }

        private void SetPoiFormError(string message)
        {
            if (lblPoiFormError == null) return;
            bool visible = !string.IsNullOrWhiteSpace(message);
            SetDisplay(lblPoiFormError, visible);
            if (visible) lblPoiFormError.text = message;
        }

        private void UpdatePoiFormCategoryHighlights()
        {
            for (int i = 0; i < poiFormCategoryButtons.Count; i++)
            {
                Button btn = poiFormCategoryButtons[i];
                bool selected = btn.userData is string key &&
                    string.Equals(key, poiFormSelectedCategory, StringComparison.Ordinal);
                btn.EnableInClassList("filter-chip--active", selected);
            }
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
