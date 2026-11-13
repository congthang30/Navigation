using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Vps.UnityClient.Api;
using Vps.UnityClient.Bootstrap;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    [TestFixture]
    public sealed class NavigationUiToolkitTests
    {
        private GameObject hostObject;
        private SiteGraphV2NavigationExperience experience;
        private UIDocument uiDocument;
        private NavigationUiToolkitController controller;

        [SetUp]
        public void SetUp()
        {
            hostObject = new GameObject("Test_NavigationUiToolkit");
            experience = hostObject.AddComponent<SiteGraphV2NavigationExperience>();
            uiDocument = hostObject.AddComponent<UIDocument>();

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1080, 1920);
            uiDocument.panelSettings = panelSettings;

            VisualTreeAsset vta = Resources.Load<VisualTreeAsset>("UI/NavigationUI");
            Assert.That(vta, Is.Not.Null, "Resources/UI/NavigationExperience.uxml must exist and load as VisualTreeAsset");
            uiDocument.visualTreeAsset = vta;

            controller = new NavigationUiToolkitController(experience);
            bool initSuccess = controller.Initialize(uiDocument);
            Assert.That(initSuccess, Is.True, "NavigationUiToolkitController failed to initialize with UIDocument");
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
        public void VisualTreeContainsAllRequiredElements()
        {
            VisualElement root = uiDocument.rootVisualElement;
            Assert.That(root, Is.Not.Null);

            // Background & safe container
            Assert.That(root.Q<VisualElement>("launcher-background"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("safe-area-container"), Is.Not.Null);

            // Headers
            Assert.That(root.Q<VisualElement>("idle-header"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("localizing-header"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("navigating-header"), Is.Not.Null);
            Assert.That(root.Q<Label>("nav-destination-name"), Is.Not.Null);
            Assert.That(root.Q<Label>("nav-distance-remaining"), Is.Not.Null);

            // Center & bottom docks
            Assert.That(root.Q<VisualElement>("readiness-card"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("destination-dock"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("localizing-dock"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("companion-dock"), Is.Not.Null);
            Assert.That(root.Q<Label>("companion-status-message"), Is.Not.Null);
            Assert.That(root.Q<Button>("btn-finish-navigation"), Is.Not.Null);

            // Destination modal
            Assert.That(root.Q<VisualElement>("modal-destination-picker"), Is.Not.Null);
            Assert.That(root.Q<TextField>("destination-search-input"), Is.Not.Null);
            Assert.That(root.Q<ScrollView>("destination-scroll-view"), Is.Not.Null);
            Assert.That(root.Q<Button>("btn-start-navigation"), Is.Not.Null);

            // Settings modal & sliders
            Assert.That(root.Q<VisualElement>("modal-settings"), Is.Not.Null);
            Assert.That(root.Q<ScrollView>("settings-scroll-view"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-route-width"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-route-hue"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-route-opacity"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-route-depth-bias"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-model-scale"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-model-brightness"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-lead-distance"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-ground-drop"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("container-color-swatches"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("container-occlusion-modes"), Is.Not.Null);
        }

        [Test]
        public void ControllerStateTransitionsBetweenIdleLocalizingAndNavigating()
        {
            VisualElement root = uiDocument.rootVisualElement;
            var idleHeader = root.Q<VisualElement>("idle-header");
            var localizingHeader = root.Q<VisualElement>("localizing-header");
            var navigatingHeader = root.Q<VisualElement>("navigating-header");
            var destinationDock = root.Q<VisualElement>("destination-dock");
            var localizingDock = root.Q<VisualElement>("localizing-dock");
            var companionDock = root.Q<VisualElement>("companion-dock");
            var readinessCard = root.Q<VisualElement>("readiness-card");

            // 1. Idle State
            controller.UpdateState(
                isTrackingAccepted: false,
                isStableTracking: false,
                isNavigating: false,
                destinationLabel: "Phòng họp",
                routeRemainingMeters: 0f,
                companionMessage: "Sẵn sàng",
                isArrived: false,
                diagnosticsInfo: "");

            Assert.That(idleHeader.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(localizingHeader.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(navigatingHeader.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(destinationDock.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(localizingDock.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(companionDock.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(readinessCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            // 2. Localizing State (Tracking requested but not yet stable)
            controller.UpdateState(
                isTrackingAccepted: true,
                isStableTracking: false,
                isNavigating: false,
                destinationLabel: "Phòng họp",
                routeRemainingMeters: 0f,
                companionMessage: "Đang dò",
                isArrived: false,
                diagnosticsInfo: "");

            Assert.That(idleHeader.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(localizingHeader.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(navigatingHeader.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(destinationDock.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(localizingDock.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(companionDock.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(readinessCard.style.display.value, Is.EqualTo(DisplayStyle.None));

            // 3. Active Navigating State
            controller.UpdateState(
                isTrackingAccepted: true,
                isStableTracking: true,
                isNavigating: true,
                destinationLabel: "Phòng họp 101",
                routeRemainingMeters: 25.5f,
                companionMessage: "Đi thẳng 10m",
                isArrived: false,
                diagnosticsInfo: "");

            Assert.That(idleHeader.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(localizingHeader.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(navigatingHeader.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(destinationDock.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(localizingDock.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(companionDock.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(readinessCard.style.display.value, Is.EqualTo(DisplayStyle.None));

            var destName = root.Q<Label>("nav-destination-name");
            Assert.That(destName.text, Is.EqualTo("Phòng họp 101"));
            var distRemaining = root.Q<Label>("nav-distance-remaining");
            Assert.That(distRemaining.text, Does.Contain("26 m").Or.Contain("25 m").Or.Contain("25.5"));
        }

        [Test]
        public void DestinationPickerModalOpensPopulatesPoisAndCloses()
        {
            VisualElement root = uiDocument.rootVisualElement;
            var modal = root.Q<VisualElement>("modal-destination-picker");
            var itemsContainer = root.Q<VisualElement>("destination-items-container");
            var startBtn = root.Q<Button>("btn-start-navigation");

            // Open modal
            controller.OpenDestinationPickerModal();
            Assert.That(modal.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(modal.ClassListContains("modal-overlay--hidden"), Is.False);

            // Populate POIs
            var samplePois = new List<PublishedGraphPoiV2>
            {
                new PublishedGraphPoiV2 { Id = "poi_1", Label = "Phòng Hội Thảo", Category = "room", IsActive = true },
                new PublishedGraphPoiV2 { Id = "poi_2", Label = "Lễ Tân", Category = "service", IsActive = true }
            };

            controller.PopulatePois(samplePois, "poi_1");
            Assert.That(itemsContainer.childCount, Is.EqualTo(2));

            // Selected POI enables start button
            Assert.That(startBtn.enabledSelf, Is.True);

            // Close modal
            controller.CloseDestinationPickerModal();
            Assert.That(modal.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(modal.ClassListContains("modal-overlay--hidden"), Is.True);
        }

        [Test]
        public void SettingsModalOpensSyncsPropertiesAndCloses()
        {
            VisualElement root = uiDocument.rootVisualElement;
            var modal = root.Q<VisualElement>("modal-settings");
            var sliderWidth = root.Q<Slider>("slider-route-width");
            var sliderScale = root.Q<Slider>("slider-model-scale");

            // Change model settings
            experience.RouteLineWidth = 0.35f;
            experience.CompanionModelScale = 1.45f;

            // Open modal and sync
            controller.OpenSettingsModal();
            Assert.That(modal.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(sliderWidth.value, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(sliderScale.value, Is.EqualTo(1.45f).Within(0.001f));

            // Close modal
            controller.CloseSettingsModal();
            Assert.That(modal.style.display.value, Is.EqualTo(DisplayStyle.None));
        }
    }
}
