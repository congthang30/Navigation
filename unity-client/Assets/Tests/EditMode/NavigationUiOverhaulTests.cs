using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Vps.UnityClient.Api;
using Vps.UnityClient.Bootstrap;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class NavigationUiOverhaulTests
    {
        private GameObject hostObject;
        private SiteGraphV2NavigationExperience experience;
        private UIDocument uiDocument;
        private NavigationUiToolkitController controller;

        [SetUp]
        public void SetUp()
        {
            hostObject = new GameObject("UiOverhaulTestHost");
            experience = hostObject.AddComponent<SiteGraphV2NavigationExperience>();
            uiDocument = hostObject.AddComponent<UIDocument>();

            var panelSettings = Resources.Load<PanelSettings>("UI/NavigationPanelSettings");
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(390, 844);
            }

            uiDocument.panelSettings = panelSettings;
            uiDocument.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/NavigationUI");
            Assert.That(uiDocument.visualTreeAsset, Is.Not.Null, "NavigationUI.uxml should load from Resources/UI/NavigationUI");

            controller = new NavigationUiToolkitController(experience);
            Assert.That(controller.Initialize(uiDocument), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (hostObject != null)
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void PanelSettingsAndFonts_LoadCleanlyFromResources()
        {
            var panelSettings = Resources.Load<PanelSettings>("UI/NavigationPanelSettings");
            Assert.That(panelSettings, Is.Not.Null);
            Assert.That(panelSettings.themeStyleSheet, Is.Not.Null);
            Assert.That(panelSettings.referenceResolution, Is.EqualTo(new Vector2Int(390, 844)));

            Assert.That(Resources.Load<Font>("UI/Fonts/Inter-Regular"), Is.Not.Null);
            Assert.That(Resources.Load<Font>("UI/Fonts/Inter-SemiBold"), Is.Not.Null);
        }

        [Test]
        public void VectorIcons_GenerateCrispTexturesAndBindToHud()
        {
            foreach (NavigationUiVectorIcons.IconKind kind in System.Enum.GetValues(typeof(NavigationUiVectorIcons.IconKind)))
            {
                var tex = NavigationUiVectorIcons.Get(kind);
                Assert.That(tex, Is.Not.Null, $"Expected procedural texture for {kind}");
                Assert.That(tex.width, Is.EqualTo(64));
                Assert.That(tex.height, Is.EqualTo(64));
            }

            var root = uiDocument.rootVisualElement;
            string[] iconNames =
            {
                "icon-brand-compass",
                "icon-idle-settings",
                "icon-nav-pin",
                "icon-companion-robot",
                "icon-launcher-search",
                "icon-close-picker",
                "icon-settings-header",
                "icon-sec-route",
                "icon-sec-occlusion",
            };

            foreach (var name in iconNames)
            {
                var el = root.Q<VisualElement>(name);
                Assert.That(el, Is.Not.Null, $"Missing vector icon slot '{name}'");
                Assert.That(el.style.backgroundImage.value.texture, Is.Not.Null, $"Vector texture not bound to '{name}'");
            }

            var brandTitle = root.Q<Label>("brand-title-label");
            Assert.That(brandTitle, Is.Not.Null, "Missing brand title label");
            Assert.That(brandTitle.text, Is.EqualTo("Robot Navigation"));

            var appLogoTex = Resources.Load<Texture2D>("UI/AppLogo");
            Assert.That(appLogoTex, Is.Not.Null, "Missing Resources/UI/AppLogo.png");
            Assert.That(root.Q<VisualElement>("icon-brand-compass").style.backgroundImage.value.texture, Is.EqualTo(appLogoTex));
        }

        [Test]
        public void RichState_ReflectsBootstrapErrorsLocalizingReticleAndArrivalHud()
        {
            var root = uiDocument.rootVisualElement;

            // 1. Bootstrap Error State
            controller.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = false,
                BootstrapState = NavigationBootstrapState.Error,
                BootstrapError = "Mất kết nối máy chủ bản đồ",
                TotalPoiCount = 0,
            });

            var errorRow = root.Q<VisualElement>("readiness-error-row");
            var errorLbl = root.Q<Label>("lbl-readiness-error");
            Assert.That(root.Q<VisualElement>("readiness-status-pill"), Is.Null);
            Assert.That(root.Q<VisualElement>("readiness-map-chip"), Is.Null);
            Assert.That(errorRow.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(errorLbl.text, Is.EqualTo("Mất kết nối máy chủ bản đồ"));

            // 2. Localizing Reticle & Live Status
            controller.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = true,
                IsStableTracking = false,
                DestinationLabel = "Phòng họp A",
                StatusMessage = "Đang dò VPS khu vực Sảnh chính…",
            });

            var reticle = root.Q<VisualElement>("localizing-reticle");
            var locStatus = root.Q<Label>("lbl-localizing-status");
            var scanHint = root.Q<Label>("scan-hint-label");
            Assert.That(reticle.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(locStatus.text, Is.EqualTo("Đang dò VPS khu vực Sảnh chính…"));
            Assert.That(scanHint, Is.Not.Null);
            Assert.That(scanHint.parent, Is.EqualTo(reticle));
            Assert.That(root.Q(className: "scan-hint-chip"), Is.Null);

            // 3. Companion Waiting & Rerouting & Decimal Remaining Distance
            controller.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = true,
                IsStableTracking = true,
                IsNavigating = true,
                DestinationLabel = "Phòng họp A",
                RouteRemainingMeters = 4.5f,
                TotalRouteMeters = 20f,
                CompanionMessage = "Robot đang chờ bạn tới gần hơn",
                IsCompanionWaiting = true,
            });

            Assert.That(reticle.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(root.Q<Label>("nav-distance-remaining").text, Is.EqualTo("4.5 m"));
            Assert.That(root.Q<Label>("companion-state-badge").text, Is.EqualTo("ROBOT ĐANG ĐỢI BẠN"));
            Assert.That(root.Q<Button>("btn-finish-navigation").style.display.value, Is.EqualTo(DisplayStyle.None));

            // 4. Arrival state highlights finish CTA & progress 100%
            controller.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = true,
                IsStableTracking = true,
                IsNavigating = true,
                DestinationLabel = "Phòng họp A",
                RouteRemainingMeters = 0f,
                TotalRouteMeters = 20f,
                CompanionMessage = "Bạn đã đến nơi!",
                IsArrived = true,
            });

            Assert.That(root.Q<Label>("nav-distance-remaining").text, Is.EqualTo("Tại đích"));
            Assert.That(root.Q<Label>("nav-state-eyebrow").text, Is.EqualTo("ĐÃ TỚI ĐIỂM ĐÍCH"));
            Assert.That(root.Q<Label>("companion-state-badge").text, Is.EqualTo("ĐÃ TỚI ĐÍCH"));
            Assert.That(root.Q<Button>("btn-finish-navigation").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<VisualElement>("nav-progress-fill").style.width.value.value, Is.EqualTo(100f).Within(0.1f));
        }

        private static void TriggerButtonClick(Button button)
        {
            button.Focus();
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        [Test]
        public void DestinationPicker_CategoryChipsAndQuickPicksWorkCleanly()
        {
            var root = uiDocument.rootVisualElement;
            var pois = new List<PublishedGraphPoiV2>
            {
                new PublishedGraphPoiV2 { Id = "p1", Label = "Phòng 101", Category = "room", IsActive = true },
                new PublishedGraphPoiV2 { Id = "p2", Label = "Phòng 102", Category = "room", IsActive = true },
                new PublishedGraphPoiV2 { Id = "p3", Label = "Thang máy A", Category = "elevator", IsActive = true },
                new PublishedGraphPoiV2 { Id = "p4", Label = "Quầy lễ tân", Category = "reception", IsActive = true },
            };

            controller.PopulatePois(pois, "p1");
            controller.OpenDestinationPickerModal();
            Assert.That(controller.IsPickerOpen, Is.True);

            var quickPicks = root.Q<VisualElement>("idle-quick-picks-container");
            Assert.That(quickPicks.childCount, Is.EqualTo(3));

            var filterRow = root.Q<VisualElement>("destination-filter-container");
            Assert.That(filterRow.childCount, Is.GreaterThanOrEqualTo(3));

            var itemsContainer = root.Q<VisualElement>("destination-items-container");
            Assert.That(itemsContainer.childCount, Is.EqualTo(4));

            // Click the second filter chip (first specific category after "Tất cả")
            var categoryChip = filterRow[1] as Button;
            Assert.That(categoryChip, Is.Not.Null);
            TriggerButtonClick(categoryChip);

            Assert.That(itemsContainer.childCount, Is.EqualTo(2));

            var resetBtn = root.Q<Button>("btn-reset-poi-filter");
            TriggerButtonClick(resetBtn);

            Assert.That(itemsContainer.childCount, Is.EqualTo(4));
        }

        [Test]
        public void SettingsModal_UpdatesTelemetryAndRoutePreview()
        {
            var root = uiDocument.rootVisualElement;
            controller.OpenSettingsModal();
            Assert.That(controller.IsSettingsOpen, Is.True);

            controller.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = false,
                BootstrapState = NavigationBootstrapState.ManifestLoaded,
                Fps = 60f,
                FrameTimeMs = 16.6f,
                MemoryMb = 240f,
                DepthAvailable = true,
                EffectiveOcclusionMode = ArOcclusionQualityMode.Balanced,
            });

            Assert.That(root.Q<Label>("lbl-metric-fps").text, Is.EqualTo("60"));
            Assert.That(root.Q<Label>("lbl-metric-frame").text, Is.EqualTo("16.6 ms"));
            Assert.That(root.Q<Label>("lbl-metric-ram").text, Is.EqualTo("240 MB"));

            var sliderWidth = root.Q<Slider>("slider-route-width");
            var previewLine = root.Q<VisualElement>("route-preview-line");
            sliderWidth.value = 0.04f;
            float narrowHeight = previewLine.style.height.value.value;
            sliderWidth.value = 0.50f;
            float wideHeight = previewLine.style.height.value.value;
            Assert.That(wideHeight, Is.GreaterThan(narrowHeight));

            var swatchRow = root.Q<VisualElement>("container-color-swatches");
            Assert.That(swatchRow.childCount, Is.EqualTo(SiteGraphV2NavigationExperience.PaletteColors.Length));
            Assert.That(swatchRow.Query<Label>().ToList().Count, Is.EqualTo(0), "Swatches must be color circles without text labels");

            controller.CloseSettingsModal();
            Assert.That(controller.IsSettingsOpen, Is.False);
        }
    }
}
