using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Vps.UnityClient.Api;
using Vps.UnityClient.Bootstrap;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class LocalMapAuthoringTests
    {
        private string tempManifestPath;
        private GameObject hostObject;

        [SetUp]
        public void SetUp()
        {
            tempManifestPath = Path.Combine(
                Path.GetTempPath(),
                "vps-local-manifest-test-" + Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown]
        public void TearDown()
        {
            if (hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(hostObject);
                hostObject = null;
            }
            if (!string.IsNullOrEmpty(tempManifestPath) && File.Exists(tempManifestPath))
            {
                File.Delete(tempManifestPath);
            }
        }

        private NavigationBootstrapController CreateTestBootstrap()
        {
            hostObject = new GameObject("LocalMapAuthoringTestHost");
            var bootstrap = hostObject.AddComponent<NavigationBootstrapController>();
            bootstrap.LocalManifestPathOverride = tempManifestPath;
            Assert.That(bootstrap.LoadEmbeddedManifest(), Is.True, bootstrap.ErrorMessage);
            return bootstrap;
        }

        [Test]
        public void AddPoiWithWalkedRoute_SplitsMidEdgeAndRoutesCleanly()
        {
            var bootstrap = CreateTestBootstrap();
            PublishedSiteGraphManifest baseManifest = bootstrap.Manifest;

            // Midpoint along edge e-b15f77a560e036957d (from junction (-2.049, -1.019, 2.332) to "đến" (1.560, -1.001, 4.996))
            var walkPoints = new List<Vector3>
            {
                new Vector3(-0.24f, -1.01f, 3.66f),
                new Vector3(0.40f, -1.01f, 2.40f),
                new Vector3(1.20f, -1.01f, 1.50f),
                new Vector3(2.10f, -1.01f, 1.50f),
            };
            var destination = new Vector3(2.80f, -1.01f, 1.50f);

            bool added = LocalSiteGraphStore.AddPoiWithRoute(
                baseManifest,
                "Phòng họp B",
                "room",
                destination,
                walkPoints,
                out PublishedSiteGraphManifest updated,
                out PublishedGraphPoiV2 addedPoi,
                out string error);

            Assert.That(added, Is.True, error);
            Assert.That(addedPoi, Is.Not.Null);
            Assert.That(addedPoi.Label, Is.EqualTo("Phòng họp B"));
            Assert.That(updated.Graph.Pois.Length, Is.EqualTo(4));
            // Original 5 nodes + 1 split junction + 1 new destination node = 7
            Assert.That(updated.Graph.Nodes.Length, Is.EqualTo(7));
            // Original 4 edges - 1 split + 2 halves + 1 walked edge = 6
            Assert.That(updated.Graph.Edges.Length, Is.EqualTo(6));

            // Verify pathfinder routes from "cửa" (-3.75, -1.05, 1.2) to the newly added room
            bool routed = SiteGraphV2Pathfinder.TryFindRoute(
                updated,
                new Vector3(-3.75f, -1.05f, 1.2f),
                addedPoi.NodeId,
                1.5f,
                out SiteGraphV2Route route,
                out string routeErr);

            Assert.That(routed, Is.True, routeErr);
            Assert.That(route.AnchorLocalPoints.Length, Is.GreaterThanOrEqualTo(4));
            Assert.That(
                Vector3.Distance(route.AnchorLocalPoints[route.AnchorLocalPoints.Length - 1], destination),
                Is.LessThan(0.05f));
        }

        [Test]
        public void AddPoiNearExistingNode_AttachesDirectlyWithoutSelfEdge()
        {
            var bootstrap = CreateTestBootstrap();
            var nearCua = new Vector3(-3.70f, -1.05f, 1.22f);

            bool added = LocalSiteGraphStore.AddPoiWithRoute(
                bootstrap.Manifest,
                "Cửa phụ",
                "entrance",
                nearCua,
                null,
                out PublishedSiteGraphManifest updated,
                out PublishedGraphPoiV2 addedPoi,
                out string error);

            Assert.That(added, Is.True, error);
            Assert.That(addedPoi.NodeId, Is.EqualTo("n-a472f7ef4e3a6d6f09"));
            Assert.That(updated.Graph.Nodes.Length, Is.EqualTo(5));
            Assert.That(updated.Graph.Edges.Length, Is.EqualTo(4));
            Assert.That(updated.Graph.Pois.Length, Is.EqualTo(4));
        }

        [Test]
        public void UpdateAndDeletePoi_PrunesCustomSpurAndPreservesAtLeastOneActivePoi()
        {
            var bootstrap = CreateTestBootstrap();
            var newDest = new Vector3(3.2f, -1.0f, 6.2f);

            Assert.That(LocalSiteGraphStore.AddPoiWithRoute(
                bootstrap.Manifest,
                "Kho thiết bị",
                "service",
                newDest,
                null,
                out PublishedSiteGraphManifest withAdded,
                out PublishedGraphPoiV2 addedPoi,
                out string addErr), Is.True, addErr);

            Assert.That(LocalSiteGraphStore.UpdatePoi(
                withAdded,
                addedPoi.Id,
                "Phòng kỹ thuật",
                "room",
                out PublishedSiteGraphManifest withUpdated,
                out string updateErr), Is.True, updateErr);

            PublishedGraphPoiV2 renamed = Array.Find(withUpdated.Graph.Pois, p => p.Id == addedPoi.Id);
            Assert.That(renamed, Is.Not.Null);
            Assert.That(renamed.Label, Is.EqualTo("Phòng kỹ thuật"));
            Assert.That(renamed.Category, Is.EqualTo("room"));

            Assert.That(LocalSiteGraphStore.DeletePoi(
                withUpdated,
                addedPoi.Id,
                out PublishedSiteGraphManifest afterDelete,
                out string deleteErr), Is.True, deleteErr);

            Assert.That(afterDelete.Graph.Pois.Length, Is.EqualTo(3));
            Assert.That(afterDelete.Graph.Nodes.Length, Is.EqualTo(5));
            Assert.That(afterDelete.Graph.Edges.Length, Is.EqualTo(4));

            // Deleting down to 0 active POIs must fail cleanly
            PublishedSiteGraphManifest cursor = afterDelete;
            Assert.That(LocalSiteGraphStore.DeletePoi(cursor, cursor.Graph.Pois[0].Id, out cursor, out _), Is.True);
            Assert.That(LocalSiteGraphStore.DeletePoi(cursor, cursor.Graph.Pois[0].Id, out cursor, out _), Is.True);
            Assert.That(LocalSiteGraphStore.DeletePoi(cursor, cursor.Graph.Pois[0].Id, out _, out string lastErr), Is.False);
            Assert.That(lastErr, Does.Contain("1 điểm đến"));
        }

        [Test]
        public void Bootstrap_PersistsLocalOverrideAndResetsToEmbeddedDefault()
        {
            var bootstrap = CreateTestBootstrap();
            Assert.That(bootstrap.IsUsingLocalOverride, Is.False);

            Assert.That(LocalSiteGraphStore.AddPoiWithRoute(
                bootstrap.Manifest,
                "Sảnh thang máy",
                "elevator",
                "Thang máy lên các tầng văn phòng và tầng thượng.",
                new Vector3(2.5f, -1.0f, 5.5f),
                null,
                out PublishedSiteGraphManifest updated,
                out _,
                out string addErr), Is.True, addErr);

            Assert.That(bootstrap.ApplyAndSaveLocalManifest(updated, out string saveErr), Is.True, saveErr);
            Assert.That(bootstrap.IsUsingLocalOverride, Is.True);
            Assert.That(File.Exists(tempManifestPath), Is.True);

            // Simulate app restart with a fresh bootstrap controller pointing at the same override file
            var host2 = new GameObject("LocalMapAuthoringRestartHost");
            try
            {
                var bootstrap2 = host2.AddComponent<NavigationBootstrapController>();
                bootstrap2.LocalManifestPathOverride = tempManifestPath;
                Assert.That(bootstrap2.LoadEmbeddedOrLocalManifest(), Is.True, bootstrap2.ErrorMessage);
                Assert.That(bootstrap2.IsUsingLocalOverride, Is.True);
                Assert.That(bootstrap2.Manifest.Graph.Pois.Length, Is.EqualTo(4));
                PublishedGraphPoiV2 savedPoi = Array.Find(bootstrap2.Manifest.Graph.Pois, p => p.Label == "Sảnh thang máy");
                Assert.That(savedPoi, Is.Not.Null);
                Assert.That(savedPoi.Description, Is.EqualTo("Thang máy lên các tầng văn phòng và tầng thượng."));

                // Reject descriptions longer than 500 characters
                Assert.That(LocalSiteGraphStore.UpdatePoi(
                    bootstrap2.Manifest,
                    savedPoi.Id,
                    savedPoi.Label,
                    savedPoi.Category,
                    new string('a', 501),
                    out _,
                    out string tooLongErr), Is.False);
                Assert.That(tooLongErr, Does.Contain("500"));

                Assert.That(bootstrap2.ResetLocalManifestToDefault(), Is.True);
                Assert.That(bootstrap2.IsUsingLocalOverride, Is.False);
                Assert.That(File.Exists(tempManifestPath), Is.False);
                Assert.That(bootstrap2.Manifest.Graph.Pois.Length, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host2);
            }
        }

        [Test]
        public void UiToolkit_MapEditorPoiFormAndAuthoringHudStatesWorkCleanly()
        {
            hostObject = new GameObject("LocalMapUiTestHost");
            var bootstrap = hostObject.AddComponent<NavigationBootstrapController>();
            bootstrap.LocalManifestPathOverride = tempManifestPath;
            Assert.That(bootstrap.LoadEmbeddedManifest(), Is.True);

            var experience = hostObject.AddComponent<SiteGraphV2NavigationExperience>();
            var uiDoc = hostObject.AddComponent<UIDocument>();

            var panelSettings = Resources.Load<PanelSettings>("UI/NavigationPanelSettings");
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(390, 844);
            }
            uiDoc.panelSettings = panelSettings;
            uiDoc.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/NavigationUI");
            Assert.That(uiDoc.visualTreeAsset, Is.Not.Null);

            experience.InitializeUiToolkit();
            NavigationUiToolkitController ui = experience.UiToolkitController;
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.IsActive, Is.True);

            VisualElement root = uiDoc.rootVisualElement;
            Assert.That(root.Q<Button>("btn-open-map-editor"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("modal-map-editor"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("modal-poi-form"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("authoring-header"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("authoring-dock"), Is.Not.Null);

            // Open Map Editor modal from experience callback
            experience.OnOpenMapEditorClicked();
            Assert.That(ui.IsMapEditorOpen, Is.True);
            var editorItems = root.Q<VisualElement>("map-editor-items-container");
            Assert.That(editorItems.childCount, Is.EqualTo(3));

            // Edit an existing POI via experience callback and verify persisted override + description preview
            string firstPoiId = bootstrap.Manifest.Graph.Pois[0].Id;
            const string sampleDescription = "Đây là sảnh đón khách chính, có quầy lễ tân và bản đồ tầng.";
            Assert.That(
                experience.OnUpdateExistingPoiRequested(firstPoiId, "Cổng chính", "entrance", sampleDescription, out string err),
                Is.True,
                err);
            Assert.That(bootstrap.IsUsingLocalOverride, Is.True);
            Assert.That(root.Q<Label>("map-editor-custom-badge").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<TextField>("poi-form-description-input"), Is.Not.Null);
            Assert.That(editorItems.Q<Label>(className: "poi-description-preview").text, Is.EqualTo(sampleDescription));

            // Prefill description when opening edit form
            ui.OpenPoiFormForEdit(bootstrap.Manifest.Graph.Pois[0]);
            Assert.That(root.Q<TextField>("poi-form-description-input").value, Is.EqualTo(sampleDescription));
            ui.ClosePoiFormModal();

            // Verify floating Robot notification bubble hides during walking and anchors right above the 3D robot on arrival
            var speechLayer = root.Q<VisualElement>("robot-speech-layer");
            var speechBubble = root.Q<VisualElement>("robot-speech-bubble");
            var bubbleTitle = root.Q<Label>("lbl-robot-bubble-title");
            var bubbleBody = root.Q<Label>("lbl-robot-bubble-body");
            var bubbleTail = root.Q<VisualElement>("robot-speech-tail");
            Assert.That(speechLayer, Is.Not.Null);
            Assert.That(speechBubble, Is.Not.Null);
            Assert.That(bubbleTitle, Is.Not.Null);
            Assert.That(bubbleBody, Is.Not.Null);
            Assert.That(bubbleTail, Is.Not.Null);

            ui.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = true,
                IsStableTracking = true,
                IsNavigating = true,
                DestinationLabel = "Cổng chính",
                DestinationDescription = sampleDescription,
                RouteRemainingMeters = 4.2f,
                TotalRouteMeters = 10f,
                IsArrived = false,
            });
            Assert.That(speechLayer.style.display.value, Is.EqualTo(DisplayStyle.None));

            ui.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = true,
                IsStableTracking = true,
                IsNavigating = true,
                DestinationLabel = "Cổng chính",
                DestinationDescription = sampleDescription,
                RouteRemainingMeters = 0f,
                TotalRouteMeters = 10f,
                IsArrived = true,
                HasRobotBubbleAnchor = true,
                IsRobotInFrontOfCamera = true,
                RobotBubbleViewportPos = new Vector2(0.50f, 0.50f),
                IsTtsSpeaking = true,
            });
            Assert.That(speechLayer.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(bubbleTitle.text, Does.Contain("CỔNG CHÍNH"));
            Assert.That(bubbleBody.text, Is.EqualTo(sampleDescription));
            Assert.That(bubbleTail.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            var speakBtn = root.Q<Button>("btn-robot-bubble-speak");
            Assert.That(speakBtn, Is.Not.Null);
            Assert.That(speakBtn.ClassListContains("robot-speak-btn--speaking"), Is.True);

            // When camera is NOT looking at the robot, notification MUST stay hidden
            ui.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = true,
                IsStableTracking = true,
                IsNavigating = true,
                DestinationLabel = "Cổng chính",
                DestinationDescription = sampleDescription,
                RouteRemainingMeters = 0f,
                TotalRouteMeters = 10f,
                IsArrived = true,
                HasRobotBubbleAnchor = true,
                IsRobotInFrontOfCamera = false,
                RobotBubbleViewportPos = new Vector2(1.45f, 0.50f),
            });
            Assert.That(speechLayer.style.display.value, Is.EqualTo(DisplayStyle.None));

            // Verify DoraemonCompanionGuide exposes a speech anchor and strict camera-view projection
            var guide = hostObject.GetComponent<DoraemonCompanionGuide>() ?? hostObject.AddComponent<DoraemonCompanionGuide>();
            guide.Initialize(null, null);
            guide.SetRoute(new[] { Vector3.zero, new Vector3(0f, 0f, 3f) });
            Assert.That(guide.TryGetSpeechAnchorWorldPosition(out _), Is.False);
            guide.SetVisible(true);
            Assert.That(guide.TryGetSpeechAnchorWorldPosition(out Vector3 speechWorld), Is.True);
            Assert.That(guide.TryGetCompanionWorldBounds(out Bounds robotBounds), Is.True);
            Assert.That(speechWorld.y, Is.GreaterThan(guide.CompanionInstance.transform.position.y));

            var camGo = new GameObject("TestArCamera");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 60f;
                cam.aspect = 0.5f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 50f;
                camGo.transform.position = robotBounds.center + new Vector3(0f, 0.3f, -2.0f);

                // Looking directly at the robot
                camGo.transform.LookAt(robotBounds.center);
                Assert.That(
                    DoraemonCompanionGuide.TryProjectSpeechBubbleInCameraView(cam, robotBounds, speechWorld, out Vector2 visibleVp),
                    Is.True);
                Assert.That(visibleVp.x, Is.InRange(0.2f, 0.8f));
                Assert.That(visibleVp.y, Is.InRange(0.1f, 0.9f));

                // Looking 90 degrees to the side (robot outside screen)
                camGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                Assert.That(
                    DoraemonCompanionGuide.TryProjectSpeechBubbleInCameraView(cam, robotBounds, speechWorld, out _),
                    Is.False);

                // Looking 180 degrees away (robot behind camera)
                camGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Assert.That(
                    DoraemonCompanionGuide.TryProjectSpeechBubbleInCameraView(cam, robotBounds, speechWorld, out _),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
            }

            // Verify Settings UI binds Google TTS controls
            Assert.That(root.Q<Toggle>("toggle-tts-enabled"), Is.Not.Null);
            Assert.That(root.Q<Slider>("slider-tts-rate"), Is.Not.Null);
            Assert.That(root.Q<TextField>("tts-api-key-input"), Is.Not.Null);
            Assert.That(root.Q<Label>("lbl-tts-status"), Is.Not.Null);
            Assert.That(root.Q<Button>("btn-tts-test"), Is.Not.Null);
            Assert.That(
                SiteGraphV2NavigationExperience.BuildArrivalSpeechText("Cổng chính", sampleDescription),
                Is.EqualTo("Đã tới Cổng chính. " + sampleDescription));

            // Verify Authoring AR HUD transitions cleanly
            ui.UpdateState(new NavigationUiState
            {
                IsTrackingAccepted = true,
                IsStableTracking = true,
                IsNavigating = false,
                IsAuthoringMode = true,
                IsRecordingWalk = true,
                RecordedWalkMeters = 3.4f,
                RecordedPointCount = 9,
                NearestGraphDistanceMeters = 0.3f,
                CurrentAnchorLocalPos = new Vector3(1.25f, -1.02f, 4.50f),
                BootstrapState = NavigationBootstrapState.ManifestLoaded,
                TotalPoiCount = 3,
            });

            Assert.That(root.Q<VisualElement>("authoring-header").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<VisualElement>("authoring-dock").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<VisualElement>("navigating-header").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(root.Q<VisualElement>("companion-dock").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(root.Q<Label>("authoring-coords-label").text, Does.Contain("1.25").And.Contain("4.50"));
            Assert.That(root.Q<Label>("authoring-state-badge").text, Does.Contain("3.4 m").And.Contain("9 điểm"));
        }
    }
}
