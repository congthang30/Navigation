using System;
using System.Collections.Generic;
using UnityEngine;
using Vps.UnityClient.Diagnostics;

namespace Vps.UnityClient.Vps
{
    public enum VpsLocalizationSessionState
    {
        Idle,
        WaitingForTracking,
        TrackingLimited,
        TrackingStabilizing,
        Localized,
        TimedOut,
        RequestRejected,
        InvalidInput,
        Stopped
    }

    /// <summary>
    /// Owns a single normalized VPS tracking attempt. Request acceptance is kept separate from
    /// localization, which requires a real TRACKED timeline, the configured stability interval,
    /// and a provider-created anchor transform.
    /// </summary>
    public sealed class VpsLocalizationSession
    {
        private readonly List<TrackingObservation> pendingObservations = new List<TrackingObservation>();
        private IVpsLocalizationAdapter adapter;
        private VpsLocalizationTuning tuning;
        private double requestStartedAt;
        private double lastTimestamp;
        private double trackedSince;
        private bool hasLastTimestamp;
        private bool hasTrackedSince;
        private bool requestCallInProgress;
        private bool adapterRequestActive;

        public event Action<VpsLocalizationSessionState> StateChanged;
        public event Action<NormalizedVpsTrackingState, double> NormalizedTrackingStateChanged;

        public VpsLocalizationSessionState State { get; private set; }
        public NormalizedVpsTrackingState TrackingState { get; private set; }
        public Transform AnchorTransform { get; private set; }
        public bool RequestAccepted { get; private set; }
        public bool IsLocalized { get; private set; }
        public string ErrorCode { get; private set; }

        public VpsLocalizationSession()
        {
            State = VpsLocalizationSessionState.Idle;
            TrackingState = NormalizedVpsTrackingState.UNTRACKED;
        }

        /// <summary>
        /// Starts an adapter request. A true return means accepted for tracking only; callers must
        /// wait for normalized TRACKED observations and this session's stable/localized state.
        /// </summary>
        public bool TryBeginTracking(
            IVpsLocalizationAdapter localizationAdapter,
            object anchorPayload,
            VpsLocalizationTuning navigationTuning,
            double timestampSeconds,
            out string errorCode)
        {
            errorCode = null;
            if (adapter != null)
            {
                Stop();
            }

            Reset();
            if (localizationAdapter == null || anchorPayload == null || navigationTuning == null ||
                !navigationTuning.IsValid || !IsFinite(timestampSeconds) || timestampSeconds < 0d)
            {
                return FailInput("INVALID_INPUT", timestampSeconds, out errorCode);
            }

            adapter = localizationAdapter;
            tuning = navigationTuning;
            requestStartedAt = timestampSeconds;
            lastTimestamp = timestampSeconds;
            hasLastTimestamp = true;
            adapter.TrackingStateChanged += OnAdapterTrackingStateChanged;
            SetState(VpsLocalizationSessionState.WaitingForTracking);
            PublishNormalizedState(NormalizedVpsTrackingState.UNTRACKED, timestampSeconds);

            Transform anchorTransform;
            bool accepted;
            requestCallInProgress = true;
            try
            {
                accepted = adapter.TryTrackAnchor(anchorPayload, out anchorTransform);
            }
            catch (Exception)
            {
                requestCallInProgress = false;
                pendingObservations.Clear();
                return FailInput("ADAPTER_REQUEST_FAILED", timestampSeconds, out errorCode);
            }

            requestCallInProgress = false;
            if (!accepted)
            {
                pendingObservations.Clear();
                ErrorCode = "TRACKING_REQUEST_REJECTED";
                DetachAdapter(false);
                SetState(VpsLocalizationSessionState.RequestRejected);
                errorCode = ErrorCode;
                return false;
            }

            RequestAccepted = true;
            adapterRequestActive = true;
            AnchorTransform = anchorTransform;
            for (int index = 0; index < pendingObservations.Count; index++)
            {
                TrackingObservation observation = pendingObservations[index];
                ApplyObservation(observation.State, observation.TimestampSeconds);
            }

            pendingObservations.Clear();
            errorCode = ErrorCode;
            return true;
        }

        /// <summary>Advances stability and timeout using the same monotonic realtime clock as adapter events.</summary>
        public VpsLocalizationSessionState Tick(double timestampSeconds)
        {
            if (!adapterRequestActive || IsTerminal(State))
            {
                return State;
            }

            if (!IsFinite(timestampSeconds) || timestampSeconds < lastTimestamp)
            {
                FailInput("INVALID_TIMESTAMP", lastTimestamp, out _);
                return State;
            }

            lastTimestamp = timestampSeconds;
            if (TrackingState == NormalizedVpsTrackingState.TRACKED && hasTrackedSince &&
                AnchorTransform != null && timestampSeconds - trackedSince >= tuning.TrackedStableSeconds)
            {
                IsLocalized = true;
                SetState(VpsLocalizationSessionState.Localized);
                return State;
            }

            if (timestampSeconds - requestStartedAt >= tuning.LocalizationTimeoutSeconds)
            {
                ErrorCode = "LOCALIZATION_TIMEOUT";
                IsLocalized = false;
                TrackingState = NormalizedVpsTrackingState.UNTRACKED;
                hasTrackedSince = false;
                AnchorTransform = null;
                PublishNormalizedState(NormalizedVpsTrackingState.UNTRACKED, timestampSeconds);
                SetState(VpsLocalizationSessionState.TimedOut);
                DetachAdapter(true);
            }

            return State;
        }

        /// <summary>Stops the provider request and resets downstream tracking to UNTRACKED.</summary>
        public void Stop()
        {
            if (adapter == null && State == VpsLocalizationSessionState.Idle)
            {
                return;
            }

            double timestamp = hasLastTimestamp ? lastTimestamp : requestStartedAt;
            IsLocalized = false;
            TrackingState = NormalizedVpsTrackingState.UNTRACKED;
            hasTrackedSince = false;
            AnchorTransform = null;
            PublishNormalizedState(NormalizedVpsTrackingState.UNTRACKED, timestamp);
            SetState(VpsLocalizationSessionState.Stopped);
            DetachAdapter(true);
        }

        private void OnAdapterTrackingStateChanged(NormalizedVpsTrackingState state, double timestampSeconds)
        {
            if (requestCallInProgress)
            {
                pendingObservations.Add(new TrackingObservation(state, timestampSeconds));
                return;
            }

            if (!adapterRequestActive || IsTerminal(State))
            {
                return;
            }

            ApplyObservation(state, timestampSeconds);
        }

        private void ApplyObservation(NormalizedVpsTrackingState state, double timestampSeconds)
        {
            if (!IsKnownTrackingState(state) || !IsFinite(timestampSeconds) ||
                timestampSeconds < requestStartedAt || timestampSeconds < lastTimestamp)
            {
                FailInput("INVALID_TRACKING_OBSERVATION", lastTimestamp, out _);
                return;
            }

            lastTimestamp = timestampSeconds;
            NormalizedVpsTrackingState previousState = TrackingState;
            TrackingState = state;
            PublishNormalizedState(state, timestampSeconds);

            if (state == NormalizedVpsTrackingState.TRACKED)
            {
                if (previousState != NormalizedVpsTrackingState.TRACKED)
                {
                    trackedSince = timestampSeconds;
                    hasTrackedSince = true;
                    IsLocalized = false;
                }

                if (!IsLocalized)
                {
                    SetState(VpsLocalizationSessionState.TrackingStabilizing);
                }
            }
            else
            {
                IsLocalized = false;
                hasTrackedSince = false;
                SetState(state == NormalizedVpsTrackingState.LIMITED
                    ? VpsLocalizationSessionState.TrackingLimited
                    : VpsLocalizationSessionState.WaitingForTracking);
            }

            Tick(timestampSeconds);
        }

        private bool FailInput(string code, double timestampSeconds, out string errorCode)
        {
            ErrorCode = code;
            IsLocalized = false;
            TrackingState = NormalizedVpsTrackingState.UNTRACKED;
            hasTrackedSince = false;
            AnchorTransform = null;
            double safeTimestamp = IsFinite(timestampSeconds) && timestampSeconds >= 0d
                ? timestampSeconds
                : (hasLastTimestamp ? lastTimestamp : 0d);
            PublishNormalizedState(NormalizedVpsTrackingState.UNTRACKED, safeTimestamp);
            SetState(VpsLocalizationSessionState.InvalidInput);
            DetachAdapter(true);
            errorCode = ErrorCode;
            return false;
        }

        private void PublishNormalizedState(NormalizedVpsTrackingState state, double timestampSeconds)
        {
            Action<NormalizedVpsTrackingState, double> handler = NormalizedTrackingStateChanged;
            if (handler != null)
            {
                handler(state, timestampSeconds);
            }
        }

        private void SetState(VpsLocalizationSessionState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            VpsSystemDiagnostics.SetState("VPS localization session", state.ToString() +
                (string.IsNullOrEmpty(ErrorCode) ? string.Empty : " error=" + ErrorCode));
            Action<VpsLocalizationSessionState> handler = StateChanged;
            if (handler != null)
            {
                handler(state);
            }
        }

        private void DetachAdapter(bool stopTracking)
        {
            IVpsLocalizationAdapter current = adapter;
            if (current == null)
            {
                adapterRequestActive = false;
                return;
            }

            current.TrackingStateChanged -= OnAdapterTrackingStateChanged;
            adapter = null;
            adapterRequestActive = false;
            requestCallInProgress = false;
            pendingObservations.Clear();
            if (stopTracking)
            {
                current.StopTracking();
            }
        }

        private void Reset()
        {
            tuning = null;
            requestStartedAt = 0d;
            lastTimestamp = 0d;
            trackedSince = 0d;
            hasLastTimestamp = false;
            hasTrackedSince = false;
            requestCallInProgress = false;
            adapterRequestActive = false;
            TrackingState = NormalizedVpsTrackingState.UNTRACKED;
            AnchorTransform = null;
            RequestAccepted = false;
            IsLocalized = false;
            ErrorCode = null;
            State = VpsLocalizationSessionState.Idle;
            pendingObservations.Clear();
        }

        private static bool IsTerminal(VpsLocalizationSessionState state)
        {
            return state == VpsLocalizationSessionState.TimedOut ||
                   state == VpsLocalizationSessionState.RequestRejected ||
                   state == VpsLocalizationSessionState.InvalidInput ||
                   state == VpsLocalizationSessionState.Stopped;
        }

        private static bool IsKnownTrackingState(NormalizedVpsTrackingState state)
        {
            return state == NormalizedVpsTrackingState.UNTRACKED ||
                   state == NormalizedVpsTrackingState.LIMITED ||
                   state == NormalizedVpsTrackingState.TRACKED;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private struct TrackingObservation
        {
            public readonly NormalizedVpsTrackingState State;
            public readonly double TimestampSeconds;

            public TrackingObservation(NormalizedVpsTrackingState state, double timestampSeconds)
            {
                State = state;
                TimestampSeconds = timestampSeconds;
            }
        }
    }
}
