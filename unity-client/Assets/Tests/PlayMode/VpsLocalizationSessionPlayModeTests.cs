using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vps.UnityClient.Vps;

namespace Vps.UnityClient.Tests.PlayMode
{
    public sealed class VpsLocalizationSessionPlayModeTests
    {
        private readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = ownedObjects.Count - 1; index >= 0; index--)
            {
                if (ownedObjects[index] != null)
                {
                    UnityEngine.Object.Destroy(ownedObjects[index]);
                }
            }

            ownedObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AcceptedRequestIsNotLocalizedUntilStableTrackedState()
        {
            Transform anchor = CreateObject("MockTrackedAnchor").transform;
            var adapter = new FakeVpsLocalizationAdapter(anchor, true);
            var session = new VpsLocalizationSession();
            var normalizedStates = new List<NormalizedVpsTrackingState>();
            session.NormalizedTrackingStateChanged += (state, timestamp) => normalizedStates.Add(state);

            string error;
            bool accepted = session.TryBeginTracking(adapter, new object(), CreateTuning(), 0d, out error);

            Assert.That(accepted, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(session.RequestAccepted, Is.True);
            Assert.That(session.IsLocalized, Is.False);
            Assert.That(session.State, Is.EqualTo(VpsLocalizationSessionState.WaitingForTracking));
            Assert.That(session.AnchorTransform, Is.EqualTo(anchor));

            adapter.Emit(NormalizedVpsTrackingState.TRACKED, 1d);
            Assert.That(session.State, Is.EqualTo(VpsLocalizationSessionState.TrackingStabilizing));
            Assert.That(session.Tick(1.49d), Is.EqualTo(VpsLocalizationSessionState.TrackingStabilizing));
            Assert.That(session.IsLocalized, Is.False);
            Assert.That(session.Tick(1.5d), Is.EqualTo(VpsLocalizationSessionState.Localized));
            Assert.That(session.IsLocalized, Is.True);

            adapter.Emit(NormalizedVpsTrackingState.LIMITED, 1.51d);
            Assert.That(session.IsLocalized, Is.False);
            Assert.That(session.State, Is.EqualTo(VpsLocalizationSessionState.TrackingLimited));
            adapter.Emit(NormalizedVpsTrackingState.UNTRACKED, 1.52d);
            Assert.That(session.IsLocalized, Is.False);
            Assert.That(session.State, Is.EqualTo(VpsLocalizationSessionState.WaitingForTracking));
            CollectionAssert.AreEqual(
                new[]
                {
                    NormalizedVpsTrackingState.UNTRACKED,
                    NormalizedVpsTrackingState.TRACKED,
                    NormalizedVpsTrackingState.LIMITED,
                    NormalizedVpsTrackingState.UNTRACKED
                },
                normalizedStates);

            yield return null;
        }

        [UnityTest]
        public IEnumerator MissingStableTrackingTimesOutAtConfiguredTwentySeconds()
        {
            var adapter = new FakeVpsLocalizationAdapter(CreateObject("MockAnchor").transform, true);
            var session = new VpsLocalizationSession();
            string error;

            Assert.That(session.TryBeginTracking(adapter, new object(), CreateTuning(), 100d, out error), Is.True);
            Assert.That(session.Tick(119.99d), Is.EqualTo(VpsLocalizationSessionState.WaitingForTracking));
            Assert.That(session.Tick(120d), Is.EqualTo(VpsLocalizationSessionState.TimedOut));
            Assert.That(session.ErrorCode, Is.EqualTo("LOCALIZATION_TIMEOUT"));
            Assert.That(session.IsLocalized, Is.False);
            Assert.That(session.TrackingState, Is.EqualTo(NormalizedVpsTrackingState.UNTRACKED));
            Assert.That(adapter.StopTrackingCalled, Is.True);

            adapter.Emit(NormalizedVpsTrackingState.TRACKED, 120.1d);
            Assert.That(session.State, Is.EqualTo(VpsLocalizationSessionState.TimedOut));
            Assert.That(session.IsLocalized, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RejectedTrackRequestNeverBecomesLocalized()
        {
            var adapter = new FakeVpsLocalizationAdapter(CreateObject("MockAnchor").transform, false);
            var session = new VpsLocalizationSession();
            string error;

            bool accepted = session.TryBeginTracking(adapter, new object(), CreateTuning(), 5d, out error);

            Assert.That(accepted, Is.False);
            Assert.That(session.RequestAccepted, Is.False);
            Assert.That(session.IsLocalized, Is.False);
            Assert.That(session.State, Is.EqualTo(VpsLocalizationSessionState.RequestRejected));
            Assert.That(error, Is.EqualTo("TRACKING_REQUEST_REJECTED"));

            adapter.Emit(NormalizedVpsTrackingState.TRACKED, 5.1d);
            Assert.That(session.IsLocalized, Is.False);
            Assert.That(session.State, Is.EqualTo(VpsLocalizationSessionState.RequestRejected));
            yield return null;
        }

        [UnityTest]
        public IEnumerator StableTrackingWithoutAnchorTransformDoesNotLocalize()
        {
            var adapter = new FakeVpsLocalizationAdapter(null, true);
            var session = new VpsLocalizationSession();
            string error;

            Assert.That(session.TryBeginTracking(adapter, new object(), CreateTuning(), 0d, out error), Is.True);
            adapter.Emit(NormalizedVpsTrackingState.TRACKED, 1d);
            Assert.That(session.Tick(1.5d), Is.EqualTo(VpsLocalizationSessionState.TrackingStabilizing));
            Assert.That(session.IsLocalized, Is.False);
            yield return null;
        }

        private VpsLocalizationTuning CreateTuning()
        {
            VpsLocalizationTuning tuning = ScriptableObject.CreateInstance<VpsLocalizationTuning>();
            ownedObjects.Add(tuning);
            return tuning;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            ownedObjects.Add(gameObject);
            return gameObject;
        }

        private sealed class FakeVpsLocalizationAdapter : IVpsLocalizationAdapter
        {
            public event Action<NormalizedVpsTrackingState, double> TrackingStateChanged;

            private readonly bool acceptsRequest;

            public Transform AnchorTransform { get; private set; }
            public bool StopTrackingCalled { get; private set; }

            public FakeVpsLocalizationAdapter(Transform anchorTransform, bool shouldAcceptRequest)
            {
                AnchorTransform = anchorTransform;
                acceptsRequest = shouldAcceptRequest;
            }

            public bool TryTrackAnchor(object anchorPayload, out Transform anchorTransform)
            {
                anchorTransform = AnchorTransform;
                return acceptsRequest;
            }

            public void StopTracking()
            {
                StopTrackingCalled = true;
            }

            public void Emit(NormalizedVpsTrackingState state, double timestampSeconds)
            {
                Action<NormalizedVpsTrackingState, double> handler = TrackingStateChanged;
                if (handler != null)
                {
                    handler(state, timestampSeconds);
                }
            }
        }
    }
}
