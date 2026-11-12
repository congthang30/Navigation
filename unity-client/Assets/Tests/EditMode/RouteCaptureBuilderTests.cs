using System;
using NUnit.Framework;
using UnityEngine;
using Vps.UnityClient.Authoring;
using Vps.UnityClient.RouteCapture;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class RouteCaptureBuilderTests
    {
        [Test]
        public void TrackingLossCreatesSeparateSegmentsAndPoiReferencesTheCapturedSample()
        {
            var builder = new RouteCaptureBuilder();
            DateTime start = new DateTime(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc);
            Assert.That(builder.Begin("site-a", "asset-a", new string('a', 64), "4.1.0-26051913", "1.0.0", start), Is.True);
            Assert.That(builder.BeginSegment(start), Is.True);
            Assert.That(builder.AddSample(new Vector3(0, 0, 0), start.AddMilliseconds(200)), Is.True);
            Assert.That(builder.AddSample(new Vector3(1, 0, 0), start.AddMilliseconds(400)), Is.True);
            Assert.That(builder.MarkPoi("Room A", "room"), Is.True);
            string firstSegmentId = builder.CurrentSegment.segmentId;
            Assert.That(builder.EndSegment("trackingLost", start.AddSeconds(1)), Is.True);

            Assert.That(builder.BeginSegment(start.AddSeconds(10)), Is.True);
            Assert.That(builder.AddSample(new Vector3(10, 0, 0), start.AddSeconds(10).AddMilliseconds(200)), Is.True);
            Assert.That(builder.AddSample(new Vector3(11, 0, 0), start.AddSeconds(10).AddMilliseconds(400)), Is.True);
            Assert.That(builder.EndSegment("operatorStopped", start.AddSeconds(11)), Is.True);
            WalkCaptureDocument capture = builder.Finish("operatorStopped", start.AddSeconds(11));

            Assert.That(capture, Is.Not.Null);
            Assert.That(capture.segments, Has.Count.EqualTo(2));
            Assert.That(capture.segments[0].endReason, Is.EqualTo("trackingLost"));
            Assert.That(capture.segments[1].endReason, Is.EqualTo("operatorStopped"));
            Assert.That(capture.segments[0].segmentId, Is.EqualTo(firstSegmentId));
            Assert.That(capture.segments[1].segmentId, Is.Not.EqualTo(firstSegmentId));
            Assert.That(capture.segments[0].samples[0].sequence, Is.EqualTo(0));
            Assert.That(capture.segments[1].samples[0].sequence, Is.EqualTo(0));
            Assert.That(capture.poiMarkers, Has.Count.EqualTo(1));
            Assert.That(capture.poiMarkers[0].segmentId, Is.EqualTo(firstSegmentId));
            Assert.That(capture.poiMarkers[0].sampleSequence, Is.EqualTo(1));
        }

        [Test]
        public void ShortAndNonFiniteSegmentsAreNotSubmitted()
        {
            var builder = new RouteCaptureBuilder();
            DateTime start = DateTime.UtcNow;
            builder.Begin("site-a", "asset-a", new string('b', 64), "4.1.0-26051913", "1.0.0", start);
            builder.BeginSegment(start);
            Assert.That(builder.AddSample(new Vector3(float.NaN, 0, 0), start.AddMilliseconds(100)), Is.False);
            Assert.That(builder.AddSample(Vector3.zero, start.AddMilliseconds(200)), Is.True);
            Assert.That(builder.MarkPoi("", "room"), Is.False);
            builder.EndSegment("trackingLost", start.AddSeconds(1));
            Assert.That(builder.Finish("operatorStopped", start.AddSeconds(2)), Is.Null);
        }

        [Test]
        public void SiteMeshPreviewUsesAnchorLocalMetresAndCapsTriangleCount()
        {
            var anchor = new GameObject("anchor");
            var meshRoot = new GameObject("mesh");
            try
            {
                anchor.transform.position = new Vector3(10, 0, -5);
                meshRoot.transform.position = new Vector3(12, 0, -4);
                var filter = meshRoot.AddComponent<MeshFilter>();
                var mesh = new Mesh
                {
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward },
                    triangles = new[] { 0, 1, 2 }
                };
                filter.sharedMesh = mesh;

                bool succeeded = SiteMeshPreviewBuilder.TryBuild(
                    meshRoot, anchor.transform, "site-a", "asset-a", new string('c', 64),
                    "4.1.0-26051913", DateTime.UtcNow, out SiteMeshUploadDocument preview, out string error);

                Assert.That(succeeded, Is.True, error);
                Assert.That(preview.coordinateFrame, Is.EqualTo("siteAnchorLocal"));
                Assert.That(preview.unit, Is.EqualTo("metre"));
                Assert.That(preview.vertices[0].x, Is.EqualTo(2).Within(0.0001));
                Assert.That(preview.vertices[0].z, Is.EqualTo(1).Within(0.0001));
                Assert.That(preview.triangles, Has.Count.EqualTo(1));
                Assert.That(preview.triangles[0], Is.EqualTo(new[] { 0, 1, 2 }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(meshRoot);
                UnityEngine.Object.DestroyImmediate(anchor);
            }
        }
    }
}
