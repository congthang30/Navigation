using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Niantic.Lightship.AR.Protobuf;
using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Sites;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.XRSubsystems;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Vps.UnityClient.Vps;

namespace Vps.UnityClient.Tests.LiveAuth
{
    public sealed class NsdkVpsAnchorSmokeTests
    {
        private const int RequestTimeoutSeconds = 20;
        private const double ArSessionStartupTimeoutSeconds = 45d;

        [UnityTest]
        [Explicit("Requires an authorized Production VPS asset and an Android device physically inside its mapped Site.")]
        [Timeout(180000)]
        public IEnumerator ProductionVpsPayloadMustReachStableAnchorTracking()
        {
            var createdObjects = new List<GameObject>();
            var addedComponents = new List<Component>();
            SitesClientManager sites = null;
            VpsLocalizationTuning tuning = null;
            VpsLocalizationSession localization = null;
            NsdkVpsLocalizationAdapter adapter = null;
            ARSession arSession = null;
            ARInputManager arInputManager = null;
            XROrigin origin = null;
            ARVps2Manager vps2Manager = null;
            Action<XRVps2LocalizationRequestRecord> requestRecordHandler = null;
            Action<VpsDebuggerDataEvent> debuggerDataHandler = null;
            var requestRecordSummaries = new List<string>();
            var debuggerEventCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            bool previousVpsDebuggerEnabled = false;
            bool vpsDebuggerSettingCaptured = false;
            bool vps2ManagerCreated = false;
            bool previousArSessionEnabled = false;
            bool arSessionStateCaptured = false;
            bool previousOriginActive = false;
            bool originActiveStateCaptured = false;

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                    double permissionDeadline = Time.realtimeSinceStartupAsDouble + 20d;
                    while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) &&
                           Time.realtimeSinceStartupAsDouble < permissionDeadline)
                    {
                        yield return null;
                    }
                }

                Assert.That(
                    UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera),
                    Is.True,
                    "Camera permission is required for the real VPS anchor smoke.");

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
                {
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
                    double locationPermissionDeadline = Time.realtimeSinceStartupAsDouble + 20d;
                    while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation) &&
                           Time.realtimeSinceStartupAsDouble < locationPermissionDeadline)
                    {
                        yield return null;
                    }
                }

                if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
                {
                    Input.compass.enabled = true;
                    if (Input.location.isEnabledByUser && Input.location.status == LocationServiceStatus.Stopped)
                    {
                        Input.location.Start();
                    }
                }
#endif

                yield return ARSession.CheckAvailability();
                if (ARSession.state == ARSessionState.NeedsInstall)
                {
                    yield return ARSession.Install();
                }

                Assert.That(
                    ARSession.state,
                    Is.Not.EqualTo(ARSessionState.Unsupported),
                    "AR Foundation reports this device as unsupported.");

                var authSettings = NsdkSettingsHelper.ActiveSettings;
                Assert.That(authSettings, Is.Not.Null, "NSDK runtime settings were not initialized.");
                var organizationId = GetOrganizationIdClaim(authSettings.AccessToken);
                Assert.That(
                    organizationId,
                    Is.Not.Null.And.Not.Empty,
                    "The locally configured credential has no organizationId claim. Credential contents are never logged.");

                var apiObject = new GameObject("AR-02 live Sites client");
                createdObjects.Add(apiObject);
                sites = apiObject.AddComponent<SitesClientManager>();
                sites.RequestTimeoutSeconds = RequestTimeoutSeconds;

                var siteRequest = sites.GetSitesForOrganizationAsync(organizationId);
                yield return WaitForCompletion(siteRequest, "AR-02 Production VPS Site query");
                var siteResult = siteRequest.Result;
                Assert.That(
                    siteResult.Status,
                    Is.EqualTo(SitesRequestStatus.Success),
                    $"Site query failed with status {siteResult.Status}; provider details omitted.");
                Assert.That(siteResult.Sites, Is.Not.Empty, "No Sites are visible to this credential.");

                string anchorPayload = null;
                string selectedSiteId = null;
                string selectedAssetId = null;
                int productionAssetCount = 0;
                foreach (var site in siteResult.Sites)
                {
                    var assetRequest = sites.GetAssetsForSiteAsync(site.Id);
                    yield return WaitForCompletion(assetRequest, "AR-02 Production VPS asset query");
                    var assetResult = assetRequest.Result;
                    Assert.That(
                        assetResult.Status,
                        Is.EqualTo(SitesRequestStatus.Success),
                        $"Asset query failed for an authorized Site with status {assetResult.Status}; provider details omitted.");

                    foreach (var asset in assetResult.Assets)
                    {
                        if (asset.AssetType != AssetType.VpsInfo ||
                            asset.Deployment != AssetDeploymentType.Production ||
                            !asset.VpsData.HasValue ||
                            string.IsNullOrWhiteSpace(asset.VpsData.Value.AnchorPayload) ||
                            string.IsNullOrWhiteSpace(site.Id) ||
                            !string.Equals(asset.SiteId, site.Id, StringComparison.Ordinal) ||
                            string.IsNullOrWhiteSpace(asset.Id))
                        {
                            continue;
                        }

                        productionAssetCount++;
                        anchorPayload = asset.VpsData.Value.AnchorPayload;
                        selectedSiteId = site.Id;
                        selectedAssetId = asset.Id;
                    }
                }

                Assert.That(
                    productionAssetCount,
                    Is.EqualTo(1),
                    "Expected exactly one authorized Production VPS asset with a non-empty payload. Narrow the local account's Site selection; no IDs or payloads are logged.");

                arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>();
                if (arSession != null)
                {
                    previousArSessionEnabled = arSession.enabled;
                    arSessionStateCaptured = true;
                    arSession.enabled = false;
                }

                vps2Manager = UnityEngine.Object.FindFirstObjectByType<ARVps2Manager>();
                origin = EnsureOrigin(createdObjects);
                previousOriginActive = origin.gameObject.activeSelf;
                originActiveStateCaptured = true;
                if (origin.gameObject.activeSelf)
                {
                    origin.gameObject.SetActive(false);
                }

                EnsureArCamera(origin, createdObjects, addedComponents);

                if (vps2Manager == null)
                {
                    vps2Manager = origin.gameObject.AddComponent<ARVps2Manager>();
                    vps2ManagerCreated = true;
                    if (!createdObjects.Contains(origin.gameObject))
                    {
                        addedComponents.Add(vps2Manager);
                    }
                }

                adapter = vps2Manager.GetComponent<NsdkVpsLocalizationAdapter>();
                if (adapter == null)
                {
                    adapter = vps2Manager.gameObject.AddComponent<NsdkVpsLocalizationAdapter>();
                    if (!createdObjects.Contains(vps2Manager.gameObject))
                    {
                        addedComponents.Add(adapter);
                    }
                }

                previousVpsDebuggerEnabled = vps2Manager.VpsDebuggerEnabled;
                vpsDebuggerSettingCaptured = !vps2ManagerCreated;
                requestRecordHandler = record =>
                {
                    string durationMs = record.EndTimeMs >= record.StartTimeMs
                        ? (record.EndTimeMs - record.StartTimeMs).ToString()
                        : "n/a";
                    string summary =
                        $"{record.RequestType}/{record.Status}/{record.ErrorCode}/{durationMs}ms";
                    requestRecordSummaries.Add(summary);
                    Debug.Log(
                        $"AR-02 VPS2 request type={record.RequestType}; status={record.Status}; " +
                        $"error={record.ErrorCode}; durationMs={durationMs}.");
                };
                vps2Manager.LocalizationRequestRecordAdded += requestRecordHandler;
                debuggerDataHandler = data =>
                {
                    if (data == null)
                    {
                        return;
                    }

                    string eventType = data.VpsDebuggerEventCase.ToString();
                    debuggerEventCounts.TryGetValue(eventType, out int count);
                    debuggerEventCounts[eventType] = count + 1;
                };
                vps2Manager.DebuggerDataReceived += debuggerDataHandler;
                SetVpsDebuggerEnabledBeforeSubsystemStart(vps2Manager, true);

                origin.gameObject.SetActive(true);
                arSession = EnsureArSession(createdObjects, addedComponents, out arInputManager);
                arSession.enabled = true;
                if (arInputManager != null)
                {
                    arInputManager.enabled = true;
                    if (arInputManager.subsystem != null && !arInputManager.subsystem.running)
                    {
                        arInputManager.subsystem.Start();
                    }
                }

                double sessionDeadline = Time.realtimeSinceStartupAsDouble + ArSessionStartupTimeoutSeconds;
                while (ARSession.state != ARSessionState.SessionTracking &&
                       Time.realtimeSinceStartupAsDouble < sessionDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    ARSession.state,
                    Is.EqualTo(ARSessionState.SessionTracking),
                    $"AR session did not reach SessionTracking; state={ARSession.state}; " +
                    $"ARFoundationNotTrackingReason={ARSession.notTrackingReason}; " +
                    $"ARCoreTrackingState={arSession?.subsystem?.trackingState}; " +
                    $"ARCoreNotTrackingReason={arSession?.subsystem?.notTrackingReason}; " +
                    $"ARCoreSessionRunning={arSession?.subsystem?.running}; " +
                    $"trackingMode={arSession?.currentTrackingMode}.");

                while (!HasValidTrackedDevicePose() &&
                       Time.realtimeSinceStartupAsDouble < sessionDeadline)
                {
                    if (arInputManager != null && arInputManager.subsystem != null && !arInputManager.subsystem.running)
                    {
                        arInputManager.subsystem.Start();
                    }

                    yield return null;
                }

                bool inputSubsystemRunning = arInputManager != null &&
                                             arInputManager.subsystem != null &&
                                             arInputManager.subsystem.running;
                bool devicePoseValid = HasValidTrackedDevicePose();
                Assert.That(
                    devicePoseValid,
                    Is.True,
                    $"AR session reached SessionTracking but XR input device pose remained invalid; " +
                    $"inputSubsystemRunning={inputSubsystemRunning}; " +
                    $"arInputManagerEnabled={arInputManager != null && arInputManager.isActiveAndEnabled}.");

                tuning = ScriptableObject.CreateInstance<VpsLocalizationTuning>();
                localization = new VpsLocalizationSession();
                var transitions = new List<string>();
                double trackingStartTime = Time.realtimeSinceStartupAsDouble;
                localization.NormalizedTrackingStateChanged += (state, timestamp) =>
                {
                    transitions.Add($"{state}@{timestamp - trackingStartTime:F2}s");
                };
                var providerStates = new List<string>();
                Vps2TrackingState? lastProviderState = null;
                RecordProviderTrackingState(
                    vps2Manager,
                    providerStates,
                    ref lastProviderState,
                    trackingStartTime);

                bool accepted = localization.TryBeginTracking(
                    adapter,
                    anchorPayload,
                    tuning,
                    trackingStartTime,
                    out string errorCode);

                Debug.Log(
                    $"AR-02 TryTrackAnchor accepted={accepted}; awaiting stable anchor Tracking; " +
                    $"vps2SubsystemRunning={vps2Manager.subsystem != null && vps2Manager.subsystem.running}; " +
                    $"inputSubsystemRunning={inputSubsystemRunning}; " +
                    $"devicePoseValid={devicePoseValid}; " +
                    $"mapLocalization={vps2Manager.VpsMapLocalizationEnabled}; " +
                    $"vpsDebugger={vps2Manager.VpsDebuggerEnabled}; " +
                    $"device={SystemInfo.deviceModel}; unity={Application.unityVersion}. Payload and credential omitted.");

                Assert.That(accepted, Is.True, $"NSDK rejected the anchor request ({errorCode ?? "no error code"}).");
                Assert.That(localization.RequestAccepted, Is.True, "The localization session did not preserve the accepted request state.");

                double localizationDeadline = trackingStartTime + tuning.LocalizationTimeoutSeconds + 1d;
                while (localization.State != VpsLocalizationSessionState.Localized &&
                       localization.State != VpsLocalizationSessionState.TimedOut &&
                       localization.State != VpsLocalizationSessionState.RequestRejected &&
                       Time.realtimeSinceStartupAsDouble < localizationDeadline)
                {
                    RecordProviderTrackingState(
                        vps2Manager,
                        providerStates,
                        ref lastProviderState,
                        trackingStartTime);
                    localization.Tick(Time.realtimeSinceStartupAsDouble);
                    yield return null;
                }

                RecordProviderTrackingState(
                    vps2Manager,
                    providerStates,
                    ref lastProviderState,
                    trackingStartTime);
                string stateTimeline = string.Join(",", transitions);
                string providerTimeline = string.Join(",", providerStates);
                string requestTimeline = requestRecordSummaries.Count == 0
                    ? "none"
                    : string.Join(";", requestRecordSummaries);
                string debuggerEventTimeline = SummarizeDebuggerEvents(debuggerEventCounts);
                bool finalInputSubsystemRunning = arInputManager != null &&
                                                  arInputManager.subsystem != null &&
                                                  arInputManager.subsystem.running;
                bool finalDevicePoseValid = HasValidTrackedDevicePose();
                Assert.That(
                    localization.State,
                    Is.EqualTo(VpsLocalizationSessionState.Localized),
                    "Anchor request was accepted but did not reach stable TRACKED within " +
                    $"{tuning.LocalizationTimeoutSeconds:F1}s; lastState={localization.TrackingState}; " +
                    $"sessionState={localization.State}; anchorTimeline=[{stateTimeline}]; " +
                    $"vps2Tracking=[{providerTimeline}]; vps2Requests=[{requestTimeline}]; " +
                    $"debuggerEvents=[{debuggerEventTimeline}]; " +
                    $"inputSubsystemRunning={finalInputSubsystemRunning}; " +
                    $"devicePoseValid={finalDevicePoseValid}. " +
                    $"siteId={selectedSiteId}; assetId={selectedAssetId}; payload omitted.");

                Debug.Log(
                    $"AR-02 real anchor smoke PASS; accepted=true; stableTracking=true; " +
                    $"elapsed={Time.realtimeSinceStartupAsDouble - trackingStartTime:F2}s; " +
                    $"anchorTimeline=[{stateTimeline}]; vps2Tracking=[{providerTimeline}]; " +
                    $"vps2Requests=[{requestTimeline}]; debuggerEvents=[{debuggerEventTimeline}]; " +
                    $"siteId={selectedSiteId}; assetId={selectedAssetId}. " +
                    "Payload and credential omitted.");
            }
            finally
            {
                if (origin != null && originActiveStateCaptured &&
                    !createdObjects.Contains(origin.gameObject))
                {
                    origin.gameObject.SetActive(previousOriginActive);
                }

                localization?.Stop();
                if (localization == null)
                {
                    adapter?.StopTracking();
                }

                if (vps2Manager != null)
                {
                    if (requestRecordHandler != null)
                    {
                        vps2Manager.LocalizationRequestRecordAdded -= requestRecordHandler;
                    }

                    if (debuggerDataHandler != null)
                    {
                        vps2Manager.DebuggerDataReceived -= debuggerDataHandler;
                    }

                    if (vpsDebuggerSettingCaptured && !vps2ManagerCreated)
                    {
                        SetVpsDebuggerEnabledBeforeSubsystemStart(
                            vps2Manager,
                            previousVpsDebuggerEnabled);
                    }
                }

                for (int index = addedComponents.Count - 1; index >= 0; index--)
                {
                    if (addedComponents[index] != null)
                    {
                        UnityEngine.Object.Destroy(addedComponents[index]);
                    }
                }

                for (int index = createdObjects.Count - 1; index >= 0; index--)
                {
                    if (createdObjects[index] != null)
                    {
                        UnityEngine.Object.Destroy(createdObjects[index]);
                    }
                }

                if (arSession != null && arSessionStateCaptured &&
                    !createdObjects.Contains(arSession.gameObject))
                {
                    arSession.enabled = previousArSessionEnabled;
                }

                if (tuning != null)
                {
                    UnityEngine.Object.Destroy(tuning);
                }
            }
        }

        private static ARSession EnsureArSession(
            List<GameObject> createdObjects,
            List<Component> addedComponents,
            out ARInputManager inputManager)
        {
            var session = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            if (session == null)
            {
                var sessionObject = new GameObject("AR Session (AR-02 live smoke)");
                createdObjects.Add(sessionObject);
                session = sessionObject.AddComponent<ARSession>();
            }

            bool sessionObjectIsOwned = createdObjects.Contains(session.gameObject);
            inputManager = EnsureComponent<ARInputManager>(
                session.gameObject,
                sessionObjectIsOwned,
                addedComponents);
            return session;
        }

        private static bool HasValidTrackedDevicePose()
        {
            var devices = InputSystem.devices;
            for (int index = 0; index < devices.Count; index++)
            {
                InputDevice device = devices[index];
                if (device == null || !device.added)
                {
                    continue;
                }

                QuaternionControl rotationControl =
                    device.TryGetChildControl<QuaternionControl>("centerEyeRotation") ??
                    device.TryGetChildControl<QuaternionControl>("deviceRotation");
                if (rotationControl == null)
                {
                    continue;
                }

                Quaternion rot = rotationControl.ReadValue();
                float magnitudeSquared = rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w;
                if (magnitudeSquared > 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static XROrigin EnsureOrigin(List<GameObject> createdObjects)
        {
            var origin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (origin != null)
            {
                return origin;
            }

            var originObject = new GameObject("XR Origin (AR-02 live smoke)");
            originObject.SetActive(false);
            createdObjects.Add(originObject);
            var createdOrigin = originObject.AddComponent<XROrigin>();
            var cameraOffsetObject = new GameObject("Camera Floor Offset (AR-02 live smoke)");
            cameraOffsetObject.transform.SetParent(originObject.transform, false);
            createdOrigin.CameraFloorOffsetObject = cameraOffsetObject;
            return createdOrigin;
        }

        private static Camera EnsureArCamera(
            XROrigin origin,
            List<GameObject> createdObjects,
            List<Component> addedComponents)
        {
            var camera = origin.Camera;
            if (camera == null)
            {
                var cameraObject = new GameObject("AR Camera (AR-02 live smoke)");
                createdObjects.Add(cameraObject);
                Transform cameraParent = origin.CameraFloorOffsetObject != null
                    ? origin.CameraFloorOffsetObject.transform
                    : origin.transform;
                cameraObject.transform.SetParent(cameraParent, false);
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.Color;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 20f;
                origin.Camera = camera;
            }

            bool cameraObjectIsOwned = createdObjects.Contains(camera.gameObject);
            EnsureComponent<ARCameraManager>(camera.gameObject, cameraObjectIsOwned, addedComponents);
            EnsureComponent<ARCameraBackground>(camera.gameObject, cameraObjectIsOwned, addedComponents);

            var trackedPoseDriver = camera.GetComponent<TrackedPoseDriver>();
            if (trackedPoseDriver == null)
            {
                trackedPoseDriver = EnsureComponent<TrackedPoseDriver>(
                    camera.gameObject, cameraObjectIsOwned, addedComponents);
                trackedPoseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;

                var positionAction = new InputAction(
                    "AR Camera Position",
                    binding: "<XRHMD>/centerEyePosition",
                    expectedControlType: "Vector3");
                positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");
                var rotationAction = new InputAction(
                    "AR Camera Rotation",
                    binding: "<XRHMD>/centerEyeRotation",
                    expectedControlType: "Quaternion");
                rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
                trackedPoseDriver.positionInput = new InputActionProperty(positionAction);
                trackedPoseDriver.rotationInput = new InputActionProperty(rotationAction);
            }

            return camera;
        }

        private static T EnsureComponent<T>(
            GameObject target,
            bool targetIsOwned,
            List<Component> addedComponents)
            where T : Component
        {
            var component = target.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            component = target.AddComponent<T>();
            if (!targetIsOwned)
            {
                addedComponents.Add(component);
            }

            return component;
        }

        private static void SetVpsDebuggerEnabledBeforeSubsystemStart(
            ARVps2Manager vps2Manager,
            bool enabled)
        {
            if (vps2Manager.VpsDebuggerEnabled == enabled)
            {
                return;
            }

            bool managerWasActive = vps2Manager.isActiveAndEnabled;
            if (managerWasActive)
            {
                vps2Manager.enabled = false;
            }

            vps2Manager.VpsDebuggerEnabled = enabled;

            if (managerWasActive)
            {
                vps2Manager.enabled = true;
            }
        }

        private static void RecordProviderTrackingState(
            ARVps2Manager vps2Manager,
            List<string> providerStates,
            ref Vps2TrackingState? previousState,
            double trackingStartTime)
        {
            Vps2TrackingState currentState = vps2Manager.TryGetLatestLocalization(out var providerLocalization)
                ? providerLocalization.TrackingState
                : Vps2TrackingState.Unavailable;
            if (previousState.HasValue && previousState.Value == currentState)
            {
                return;
            }

            providerStates.Add(
                $"{currentState}@{Time.realtimeSinceStartupAsDouble - trackingStartTime:F2}s");
            previousState = currentState;
        }

        private static string SummarizeDebuggerEvents(SortedDictionary<string, int> eventCounts)
        {
            if (eventCounts == null || eventCounts.Count == 0)
            {
                return "none";
            }

            var summaries = new List<string>(eventCounts.Count);
            foreach (var eventCount in eventCounts)
            {
                summaries.Add($"{eventCount.Key}:{eventCount.Value}");
            }

            return string.Join(",", summaries);
        }

        private static string GetOrganizationIdClaim(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            var segments = accessToken.Split('.');
            if (segments.Length < 2)
            {
                return null;
            }

            try
            {
                string payload = DecodeBase64Url(segments[1]);
                var claims = JsonUtility.FromJson<OrganizationClaims>(payload);
                return claims?.organizationId;
            }
            catch
            {
                return null;
            }
        }

        private static string DecodeBase64Url(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }

        [Serializable]
        private sealed class OrganizationClaims
        {
            public string organizationId;
        }

        private static IEnumerator WaitForCompletion<T>(Task<T> task, string operation)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + RequestTimeoutSeconds;
            while (!task.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, $"Timed out during {operation}; credential and payload omitted.");
            Assert.That(task.IsFaulted, Is.False, $"{operation} faulted; credential and payload omitted.");
            Assert.That(task.IsCanceled, Is.False, $"{operation} was canceled; credential and payload omitted.");
        }
    }
}
