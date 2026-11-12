using System;
using NianticSpatial.NSDK.AR.VPS2;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using Vps.UnityClient.Diagnostics;

namespace Vps.UnityClient.Vps
{
    /// <summary>
    /// Bridges NSDK VPS2 anchors into the provider-independent localization session.
    /// A successful request only means NSDK accepted the payload; Tracking is observed later.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ARVps2Manager))]
    public sealed class NsdkVpsLocalizationAdapter : MonoBehaviour, IVpsLocalizationAdapter
    {
        [SerializeField] private ARVps2Manager vps2Manager = null;

        private ARVps2Anchor activeAnchor;
        private string activePayload;
        private bool hasLastState;
        private NormalizedVpsTrackingState lastState;

        public event Action<NormalizedVpsTrackingState, double> TrackingStateChanged;

        public NormalizedVpsTrackingState CurrentTrackingState { get; private set; }
            = NormalizedVpsTrackingState.UNTRACKED;

        private void Awake()
        {
            Debug.developerConsoleVisible = false;
            ResolveManager();
        }

        private void Update()
        {
            if (activeAnchor == null)
            {
                if (hasLastState && lastState != NormalizedVpsTrackingState.UNTRACKED)
                {
                    Publish(NormalizedVpsTrackingState.UNTRACKED);
                }

                return;
            }

            Publish(Normalize(activeAnchor.trackingState));
        }

        public bool TryTrackAnchor(object anchorPayload, out Transform anchorTransform)
        {
            anchorTransform = null;
            if (!(anchorPayload is string payload) || string.IsNullOrWhiteSpace(payload))
            {
                VpsSystemDiagnostics.Record("VPS", "anchor request rejected: payload missing or invalid");
                return false;
            }

            ResolveManager();
            if (vps2Manager == null || !vps2Manager.isActiveAndEnabled)
            {
                VpsSystemDiagnostics.Record("VPS", "anchor request rejected: ARVps2Manager missing or disabled");
                return false;
            }

            if (activeAnchor != null && string.Equals(activePayload, payload, StringComparison.Ordinal) && activeAnchor.gameObject != null)
            {
                anchorTransform = activeAnchor.transform;
                return true;
            }

            if (activeAnchor != null)
            {
                StopTracking();
            }

            ARVps2Anchor requestedAnchor = null;
            try
            {
                if (!vps2Manager.TryTrackAnchor(payload, out requestedAnchor) || requestedAnchor == null)
                {
                    Publish(NormalizedVpsTrackingState.UNTRACKED);
                    return false;
                }
            }
            catch (ArgumentException ex)
            {
                Debug.LogWarning("[NsdkVpsLocalizationAdapter] Anchor already tracked in NSDK; reusing; exceptionType=" + ex.GetType().Name);
                if (vps2Manager.trackables != null)
                {
                    foreach (var trackable in vps2Manager.trackables)
                    {
                        if (trackable != null && trackable.gameObject != null)
                        {
                            requestedAnchor = trackable;
                            break;
                        }
                    }
                }
                if (requestedAnchor == null)
                {
                    Publish(NormalizedVpsTrackingState.UNTRACKED);
                    return false;
                }
            }

            activePayload = payload;
            activeAnchor = requestedAnchor;
            hasLastState = false;
            anchorTransform = activeAnchor.transform;
            Publish(Normalize(activeAnchor.trackingState));
            VpsSystemDiagnostics.Record("VPS", "anchor request accepted; currentState=" + Normalize(activeAnchor.trackingState));
            return true;
        }

        public void StopTracking()
        {
            activePayload = null;
            ARVps2Anchor anchor = activeAnchor;
            activeAnchor = null;

            if (anchor != null && vps2Manager != null)
            {
                vps2Manager.RemoveAnchor(anchor);
            }

            Publish(NormalizedVpsTrackingState.UNTRACKED);
        }

        private void OnDisable()
        {
            StopTracking();
        }

        private void ResolveManager()
        {
            if (vps2Manager == null)
            {
                vps2Manager = GetComponent<ARVps2Manager>();
            }
        }

        private void Publish(NormalizedVpsTrackingState state)
        {
            CurrentTrackingState = state;
            if (hasLastState && lastState == state)
            {
                return;
            }

            hasLastState = true;
            lastState = state;
            VpsSystemDiagnostics.SetState("VPS tracking", state.ToString());
            TrackingStateChanged?.Invoke(state, Time.realtimeSinceStartupAsDouble);
        }

        private static NormalizedVpsTrackingState Normalize(TrackingState state)
        {
            switch (state)
            {
                case TrackingState.Tracking:
                    return NormalizedVpsTrackingState.TRACKED;
                case TrackingState.Limited:
                    return NormalizedVpsTrackingState.LIMITED;
                default:
                    return NormalizedVpsTrackingState.UNTRACKED;
            }
        }
    }
}
