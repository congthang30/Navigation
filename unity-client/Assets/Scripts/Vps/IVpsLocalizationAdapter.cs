using System;
using UnityEngine;

namespace Vps.UnityClient.Vps
{
    /// <summary>
    /// Boundary for a VPS provider. A true return from TryTrackAnchor means only that the request was
    /// accepted for tracking; localization is reported separately through TrackingStateChanged.
    /// Provider callbacks must be marshalled to Unity's main thread before invoking the event.
    /// </summary>
    public interface IVpsLocalizationAdapter
    {
        event Action<NormalizedVpsTrackingState, double> TrackingStateChanged;

        /// <param name="anchorPayload">Opaque provider payload. This seam makes no assumptions about its shape.</param>
        /// <param name="anchorTransform">Provider-created anchor transform, if the request was accepted.</param>
        /// <returns>True means accepted for tracking, not that the anchor is localized.</returns>
        bool TryTrackAnchor(object anchorPayload, out Transform anchorTransform);

        void StopTracking();
    }
}
