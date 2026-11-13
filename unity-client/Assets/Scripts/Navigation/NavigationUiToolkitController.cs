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
        private Button btnChangeDestination;
        private Button btnFinishNavigation;

        // Destination Picker Bottom Sheet
        private VisualElement modalDestinationPicker;
        private VisualElement destinationFooter;
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
        private Toggle toggleSemantic;
        private Toggle toggleSuppressGround;
        private Toggle toggleDepthVis;
        private Toggle toggleMeshDebug;

        private Label lblMetricFps;
        private Label lblMetricFrame;
        private Label lblMetricRam;
        private Label lblMetricDepth;
        private Label lblDiagnostics;
        private Button btnSettingsReset;
        private Button btnSettingsDone;

        // Cached State
        private readonly List<VisualElement> colorChipElements = new List<VisualElement>();
        private readonly List<VisualElement> occlusionCardElements = new List<VisualElement>();
        private readonly Dictionary<string, VisualElement> poiRowById = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private readonly List<PublishedGraphPoiV2> latestPois = new List<PublishedGraphPoiV2>();
        private readonly List<string> knownCategories = new List<string>();

        private string currentSelectedPoiId;
        private string activeCategoryFilter = AllCategoriesKey;
        private bool isSyncingSettings;
        private bool isPickerOpen;
        private bool isSettingsOpen;
        private bool isBusyConnecting;

        // Safe-area geometry cache (avoids touching styles every frame)
        private Rect lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private float lastPanelW = -1f;
        private float lastPanelH = -1f;
        private int lastScreenW = -1;
        private int lastScreenH = -1;

        public bool IsActive => rootElement != null;
        public bool IsPickerOpen => isPickerOpen;
        public bool IsSettingsOpen => isSettingsOpen;

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
            BuildColorSwatches();
            BuildOcclusionModeCards();
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
            btnChangeDestination = rootElement.Q<Button>("btn-change-destination");
            btnFinishNavigation = rootElement.Q<Button>("btn-finish-navigation");

            modalDestinationPicker = rootElement.Q<VisualElement>("modal-destination-picker");
            destinationFooter = modalDestinationPicker != null ? modalDestinationPicker.Q<VisualElement>(className: "modal-footer--picker") : null;
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
            toggleSemantic = rootElement.Q<Toggle>("toggle-semantic");
            toggleSuppressGround = rootElement.Q<Toggle>("toggle-suppress-ground");
            toggleDepthVis = rootElement.Q<Toggle>("toggle-depth-vis");
            toggleMeshDebug = rootElement.Q<Toggle>("toggle-mesh-debug");

            lblMetricFps = rootElement.Q<Label>("lbl-metric-fps");
            lblMetricFrame = rootElement.Q<Label>("lbl-metric-frame");
            lblMetricRam = rootElement.Q<Label>("lbl-metric-ram");
            lblMetricDepth = rootElement.Q<Label>("lbl-metric-depth");
            lblDiagnostics = rootElement.Q<Label>("lbl-diagnostics");
            btnSettingsReset = rootElement.Q<Button>("btn-settings-reset");
            btnSettingsDone = rootElement.Q<Button>("btn-settings-done");
        }

        private void BindVectorIcons()
        {
            BindBrandLogo();
            SetIcon("icon-nav-compass", NavigationUiVectorIcons.IconKind.Compass);
            SetIcon("icon-idle-settings", NavigationUiVectorIcons.IconKind.Settings);
            SetIcon("icon-localizing-back", NavigationUiVectorIcons.IconKind.Back);
            SetIcon("icon-localizing-settings", NavigationUiVectorIcons.IconKind.Settings);
            SetIcon("icon-nav-pin", NavigationUiVectorIcons.IconKind.Pin);
            SetIcon("icon-nav-settings", NavigationUiVectorIcons.IconKind.Settings);

            SetIcon("icon-readiness-error", NavigationUiVectorIcons.IconKind.AlertCircle);
            SetIcon("icon-retry-bootstrap", NavigationUiVectorIcons.IconKind.Refresh);
            SetIcon("icon-step-pin", NavigationUiVectorIcons.IconKind.Pin);
            SetIcon("icon-step-scan", NavigationUiVectorIcons.IconKind.ScanCamera);
            SetIcon("icon-step-robot", NavigationUiVectorIcons.IconKind.Robot);

            SetIcon("icon-launcher-search", NavigationUiVectorIcons.IconKind.Search);
            SetIcon("icon-launcher-chevron", NavigationUiVectorIcons.IconKind.ChevronRight);
            SetIcon("icon-localizing-scan", NavigationUiVectorIcons.IconKind.ScanCamera);
            SetIcon("icon-companion-robot", NavigationUiVectorIcons.IconKind.Robot);
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
            SetIcon("icon-sec-occlusion", NavigationUiVectorIcons.IconKind.Layers);
            SetIcon("icon-sec-diag", NavigationUiVectorIcons.IconKind.Activity);
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

        private void RegisterEvents()
        {
            if (btnIdleSettings != null) btnIdleSettings.clicked += OpenSettingsModal;
            if (btnLocalizingSettings != null) btnLocalizingSettings.clicked += OpenSettingsModal;
            if (btnNavSettings != null) btnNavSettings.clicked += OpenSettingsModal;

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
            RegisterToggle(toggleSemantic, val => { if (experience != null) experience.SemanticSuppressionOcclusion = val; });
            RegisterToggle(toggleSuppressGround, val => { if (experience != null) experience.SuppressGroundOcclusion = val; });
            RegisterToggle(toggleDepthVis, val => { if (experience != null) experience.DepthVisualizationOcclusion = val; });
            RegisterToggle(toggleMeshDebug, val => { if (experience != null) experience.MeshDebugOcclusion = val; });
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
            bool showNavigating = state.IsTrackingAccepted && state.IsStableTracking && state.IsNavigating;

            SetDisplay(launcherBackground, showIdle);
            SetDisplay(idleHeader, showIdle);
            SetDisplay(destinationDock, showIdle);
            SetDisplay(readinessCard, showIdle);

            SetDisplay(localizingHeader, showLocalizing);
            SetDisplay(localizingDock, showLocalizing);
            SetDisplay(localizingReticle, showLocalizing);

            SetDisplay(navigatingHeader, showNavigating);
            SetDisplay(companionDock, showNavigating);

            isBusyConnecting = state.IsBusyConnecting;

            if (showIdle)
            {
                UpdateIdleReadiness(in state);
            }

            if (showLocalizing)
            {
                if (localizingTargetLabel != null)
                {
                    localizingTargetLabel.text = string.IsNullOrEmpty(state.DestinationLabel)
                        ? "Đang khóa không gian 3D…"
                        : $"Đích: {state.DestinationLabel}";
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

            UpdateStartButtonState();
            UpdateDiagnosticsAndTelemetry(in state);
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
                toggleSemantic?.SetValueWithoutNotify(experience.SemanticSuppressionOcclusion);
                toggleSuppressGround?.SetValueWithoutNotify(experience.SuppressGroundOcclusion);
                toggleDepthVis?.SetValueWithoutNotify(experience.DepthVisualizationOcclusion);
                toggleMeshDebug?.SetValueWithoutNotify(experience.MeshDebugOcclusion);

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

        private static void SetDisplay(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
