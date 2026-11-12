using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Vps.UnityClient.Api;
using Vps.UnityClient.Authoring;
using Vps.UnityClient.Vps;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class RouteRecorderCameraFlowTests
    {
        private GameObject host;
        private WalkRouteCaptureAuthoring recorder;
        private const string FloorId = "111111111111111111111111";

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Recorder camera flow test");
            recorder = host.AddComponent<WalkRouteCaptureAuthoring>();
            Set("floorId", FloorId);
            Set("selectedFloorIndex", 0);
            Set("selectableFloors", new[] { new RecordableFloorOption { id = FloorId } });
        }

        [TearDown]
        public void TearDown()
        {
            VpsWorkflowCoordinator.Release(recorder);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void BoundFloorCanStartOpeningCameraBeforeAnArSessionExists()
        {
            Assert.That(Get("arSession"), Is.Null);
            Assert.That(Get("arHostReady"), Is.False);
            Assert.That(Call("GetLocateSiteSetupBlockReason"), Is.Null,
                "Choosing a bound Floor must enable the action that opens the camera; AR tracking is awaited afterward.");
        }

        [Test]
        public void MismatchedFloorRemainsBlockedBeforeCameraStartup()
        {
            Set("floorId", "222222222222222222222222");
            Assert.That(Call("GetLocateSiteSetupBlockReason"), Is.Not.Null);
        }

        [Test]
        public void CameraObjectWithoutFramesDoesNotAskTheUserToScan()
        {
            var cameraObject = new GameObject("Camera without frames");
            try
            {
                Set("arCamera", cameraObject.AddComponent<Camera>());
                Set("arHostReady", true);
                string message = (string)Call("GetArReadinessMessage");
                Assert.That(message, Does.Contain("Đang mở camera"));
                Assert.That(message, Does.Not.Contain("di chuyển"));
                Assert.That(message, Does.Not.Contain("đủ sáng"));
            }
            finally { Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void CancelDuringStartupReturnsToSetupAndReleasesTheSharedWorkflow()
        {
            Set("scanViewOpen", true);
            Set("locateStarting", true);
            Assert.That(VpsWorkflowCoordinator.TryAcquire(recorder), Is.True);
            Call("StopTracking");
            Assert.That(Get("scanViewOpen"), Is.False);
            Assert.That(Get("locateStarting"), Is.False);
            Assert.That(Get("trackingRequestAccepted"), Is.False);
            Assert.That(Call("GetLocateSiteSetupBlockReason"), Is.Null);

            var otherHost = new GameObject("Other workflow");
            var other = otherHost.AddComponent<WalkRouteCaptureAuthoring>();
            try { Assert.That(VpsWorkflowCoordinator.TryAcquire(other), Is.True); }
            finally
            {
                VpsWorkflowCoordinator.Release(other);
                Object.DestroyImmediate(otherHost);
            }
        }

        [Test]
        public void IncompatibleArServiceReportsCompatibilityInsteadOfScanningInstructions()
        {
            string message = (string)typeof(WalkRouteCaptureAuthoring)
                .GetMethod("GetArInstallErrorMessage", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { SessionInstallationStatus.ErrorDeviceNotCompatible });
            Assert.That(message, Does.Contain("chưa tương thích"));
            Assert.That(message, Does.Not.Contain("di chuyển"));
            Assert.That(message, Does.Not.Contain("quyền camera"));
        }

        [Test]
        public void DeclinedArServiceInstallationExplainsHowToRetry()
        {
            string message = (string)typeof(WalkRouteCaptureAuthoring)
                .GetMethod("GetArInstallErrorMessage", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { SessionInstallationStatus.ErrorUserDeclined });
            Assert.That(message, Does.Contain("chưa được chấp nhận"));
            Assert.That(message, Does.Contain("thử lại"));
        }

        [Test]
        public void LateCameraFrameAfterCancelDoesNotReopenThePreview()
        {
            Set("scanViewOpen", true);
            Call("StopTracking");
            var texture = new Texture2D(2, 2);
            try
            {
                Call("OnArCameraFrameReceived", new ARCameraFrameEventArgs { textures = new List<Texture2D> { texture } });
                Assert.That(Get("cameraFrameCount"), Is.EqualTo(0));
                Assert.That(Get("scanViewOpen"), Is.False);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private object Call(string name, params object[] args) => typeof(WalkRouteCaptureAuthoring)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(recorder, args);

        private object Get(string name) => typeof(WalkRouteCaptureAuthoring)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(recorder);

        private void Set(string name, object value) => typeof(WalkRouteCaptureAuthoring)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(recorder, value);
    }
}
