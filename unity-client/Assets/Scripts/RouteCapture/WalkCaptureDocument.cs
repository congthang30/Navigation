using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vps.UnityClient.RouteCapture
{
    [Serializable]
    public sealed class WalkCaptureDocument
    {
        public string captureId;
        public string siteId;
        public string productionAssetId;
        public string payloadSha256;
        public string nsdkVersion;
        public string appBuild;
        public string capturedAt;
        public List<TrackingSegmentDocument> segments = new List<TrackingSegmentDocument>();
        public List<CapturedPoiMarkerDocument> poiMarkers = new List<CapturedPoiMarkerDocument>();
        public List<CapturedJunctionMarkerDocument> junctionMarkers = new List<CapturedJunctionMarkerDocument>();
    }

    [Serializable]
    public sealed class PendingRouteCaptureQueueDocument
    {
        public string floorId;
        public string captureJson;
    }

    [Serializable]
    public sealed class TrackingSegmentDocument
    {
        public string segmentId;
        public string startedAt;
        public string endedAt;
        public string endReason;
        public List<RouteSampleDocument> samples = new List<RouteSampleDocument>();
    }

    [Serializable]
    public sealed class RouteSampleDocument
    {
        public int sequence;
        public string capturedAt;
        public AnchorLocalPointDocument anchorLocal;
    }

    [Serializable]
    public sealed class AnchorLocalPointDocument
    {
        public float x;
        public float y;
        public float z;

        public AnchorLocalPointDocument(float xValue, float yValue, float zValue)
        {
            x = xValue;
            y = yValue;
            z = zValue;
        }
    }

    [Serializable]
    public sealed class CapturedPoiMarkerDocument
    {
        public string markerId;
        public string segmentId;
        public int sampleSequence;
        public string label;
        public string category;
    }

    [Serializable]
    public sealed class CapturedJunctionMarkerDocument
    {
        public string markerId;
        public string segmentId;
        public int sampleSequence;
    }

    /// <summary>Builds route captures without ever connecting samples across tracking gaps.</summary>
    public sealed class RouteCaptureBuilder
    {
        private TrackingSegmentDocument currentSegment;

        public WalkCaptureDocument Capture { get; private set; }
        public TrackingSegmentDocument CurrentSegment { get { return currentSegment; } }
        public int TotalSampleCount { get; private set; }

        public bool Begin(string siteId, string productionAssetId, string payloadSha256, string nsdkVersion, string appBuild, DateTime capturedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(productionAssetId) ||
                string.IsNullOrWhiteSpace(payloadSha256) || string.IsNullOrWhiteSpace(nsdkVersion) || string.IsNullOrWhiteSpace(appBuild))
            {
                return false;
            }

            Capture = new WalkCaptureDocument
            {
                captureId = Guid.NewGuid().ToString("D"),
                siteId = siteId.Trim(),
                productionAssetId = productionAssetId.Trim(),
                payloadSha256 = payloadSha256,
                nsdkVersion = nsdkVersion,
                appBuild = appBuild,
                capturedAt = Iso(capturedAtUtc)
            };
            currentSegment = null;
            TotalSampleCount = 0;
            return true;
        }

        public bool BeginSegment(DateTime startedAtUtc)
        {
            if (Capture == null || currentSegment != null || Capture.segments.Count >= 200) return false;
            currentSegment = new TrackingSegmentDocument
            {
                segmentId = Guid.NewGuid().ToString("D"),
                startedAt = Iso(startedAtUtc),
                endedAt = Iso(startedAtUtc),
                endReason = "sessionEnded"
            };
            return true;
        }

        public bool AddSample(Vector3 anchorLocal, DateTime capturedAtUtc)
        {
            if (Capture == null || currentSegment == null || TotalSampleCount >= 100000 ||
                !IsFinite(anchorLocal.x) || !IsFinite(anchorLocal.y) || !IsFinite(anchorLocal.z)) return false;
            string timestamp = Iso(capturedAtUtc);
            if (currentSegment.samples.Count > 0 &&
                DateTime.Parse(currentSegment.samples[currentSegment.samples.Count - 1].capturedAt).ToUniversalTime() >= capturedAtUtc.ToUniversalTime())
            {
                return false;
            }
            currentSegment.samples.Add(new RouteSampleDocument
            {
                sequence = currentSegment.samples.Count,
                capturedAt = timestamp,
                anchorLocal = new AnchorLocalPointDocument(anchorLocal.x, anchorLocal.y, anchorLocal.z)
            });
            TotalSampleCount++;
            return true;
        }

        public bool MarkPoi(string label, string category)
        {
            if (Capture == null || currentSegment == null || currentSegment.samples.Count < 2 ||
                string.IsNullOrWhiteSpace(label) || label.Trim().Length > 100 || !IsKnownCategory(category) || Capture.poiMarkers.Count >= 500) return false;
            Capture.poiMarkers.Add(new CapturedPoiMarkerDocument
            {
                markerId = Guid.NewGuid().ToString("D"),
                segmentId = currentSegment.segmentId,
                sampleSequence = currentSegment.samples[currentSegment.samples.Count - 1].sequence,
                label = label.Trim(),
                category = category
            });
            return true;
        }

        public bool MarkJunction(Vector3 anchorLocal, DateTime capturedAtUtc)
        {
            if (Capture == null || currentSegment == null || currentSegment.samples.Count < 1 ||
                Capture.junctionMarkers.Count >= 500 || !AddSample(anchorLocal, capturedAtUtc)) return false;
            Capture.junctionMarkers.Add(new CapturedJunctionMarkerDocument
            {
                markerId = Guid.NewGuid().ToString("D"),
                segmentId = currentSegment.segmentId,
                sampleSequence = currentSegment.samples[currentSegment.samples.Count - 1].sequence
            });
            return true;
        }

        public bool EndSegment(string reason, DateTime endedAtUtc)
        {
            if (Capture == null || currentSegment == null ||
                (reason != "trackingLost" && reason != "operatorStopped" && reason != "sessionEnded")) return false;
            currentSegment.endedAt = Iso(endedAtUtc);
            currentSegment.endReason = reason;
            if (currentSegment.samples.Count >= 2)
            {
                Capture.segments.Add(currentSegment);
            }
            else
            {
                Capture.poiMarkers.RemoveAll(marker => marker.segmentId == currentSegment.segmentId);
                Capture.junctionMarkers.RemoveAll(marker => marker.segmentId == currentSegment.segmentId);
                TotalSampleCount -= currentSegment.samples.Count;
            }
            currentSegment = null;
            return true;
        }

        public WalkCaptureDocument Finish(string reason, DateTime endedAtUtc)
        {
            if (currentSegment != null) EndSegment(reason, endedAtUtc);
            if (Capture == null || Capture.segments.Count == 0) return null;
            WalkCaptureDocument result = Capture;
            Capture = null;
            TotalSampleCount = 0;
            return result;
        }

        private static string Iso(DateTime value)
        {
            return value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool IsKnownCategory(string value)
        {
            return value == "room" || value == "service" || value == "entrance" || value == "elevator" ||
                value == "stairs" || value == "restroom" || value == "other";
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class SiteMeshUploadDocument
    {
        public string siteId;
        public string productionAssetId;
        public string payloadSha256;
        public string nsdkVersion;
        public string capturedAt;
        public string coordinateFrame = "siteAnchorLocal";
        public string unit = "metre";
        public List<AnchorLocalPointDocument> vertices = new List<AnchorLocalPointDocument>();
        public List<int[]> triangles = new List<int[]>();
    }
}
