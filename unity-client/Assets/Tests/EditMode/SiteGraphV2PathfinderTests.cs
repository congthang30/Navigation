using NUnit.Framework;
using UnityEngine;
using Vps.UnityClient.Api;
using Vps.UnityClient.Bootstrap;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class SiteGraphV2PathfinderTests
    {
        [Test]
        public void ProjectsAnchorLocalStartAndUsesDirectedMetricGeometry()
        {
            PublishedSiteGraphManifest manifest = MakeManifest();
            bool found = SiteGraphV2Pathfinder.TryFindRoute(manifest, new Vector3(2f, 0.2f, 0f), "n-c", 1f, out SiteGraphV2Route route, out string error);
            Assert.That(found, Is.True, error);
            Assert.That(route.EdgeIds, Is.EqualTo(new[] { "e-a-b", "e-b-c" }));
            Assert.That(route.AnchorLocalPoints[0].x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(route.AnchorLocalPoints[route.AnchorLocalPoints.Length - 1].x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(route.WeightedCost, Is.EqualTo(8d).Within(0.001d));
        }

        [Test]
        public void DoesNotTraverseOneWayEdgeBackwardsAndFailsWhenStartIsTooFar()
        {
            PublishedSiteGraphManifest manifest = MakeManifest();
            Assert.That(SiteGraphV2Pathfinder.TryFindRoute(manifest, new Vector3(8f, 0f, 0f), "n-a", 1f, out SiteGraphV2Route reverse, out _), Is.False);
            Assert.That(reverse, Is.Null);
            Assert.That(SiteGraphV2Pathfinder.TryFindRoute(manifest, new Vector3(8f, 0f, 4f), "n-c", 1f, out SiteGraphV2Route far, out _), Is.False);
        }

        [Test]
        public void BindingAndTrackingGateFailClosed()
        {
            PublishedSiteGraphManifest manifest = MakeManifest();
            manifest.SiteBinding = new SiteBindingManifestV2 { SiteId = "site-a", ProductionAssetId = "asset-a", PayloadSha256 = new string('b', 64) };
            Assert.That(SiteGraphV2NavigationGate.BindingMatches(manifest, "site-a", "asset-a", new string('b', 64)), Is.True);
            Assert.That(SiteGraphV2NavigationGate.BindingMatches(manifest, "site-a", "asset-other", new string('b', 64)), Is.False);
            Assert.That(SiteGraphV2NavigationGate.CanRender(true, true, true), Is.True);
            Assert.That(SiteGraphV2NavigationGate.CanRender(false, true, true), Is.False);
            Assert.That(SiteGraphV2NavigationGate.CanRender(true, false, true), Is.False);
        }

        private static PublishedSiteGraphManifest MakeManifest()
        {
            return new PublishedSiteGraphManifest
            {
                SchemaVersion = 2,
                SiteBinding = new SiteBindingManifestV2 { SiteId = "site-a", ProductionAssetId = "asset-a", PayloadSha256 = new string('b', 64) },
                Graph = new PublishedNavigationGraphV2
                {
                    Nodes = new[]
                    {
                        Node("n-a", 0f), Node("n-b", 5f), Node("n-c", 10f)
                    },
                    Edges = new[]
                    {
                        Edge("e-a-b", "n-a", "n-b", true, 5f),
                        Edge("e-b-c", "n-b", "n-c", false, 5f)
                    },
                    Pois = new[] { new PublishedGraphPoiV2 { Id = "poi-c", Label = "Room C", Category = "room", IsActive = true, NodeId = "n-c", AnchorLocal = Point(10f) } }
                }
            };
        }

        private static PublishedGraphNodeV2 Node(string id, float x)
        {
            return new PublishedGraphNodeV2 { Id = id, Kind = "waypoint", AnchorLocal = Point(x), Provenance = "generated" };
        }

        private static PublishedGraphEdgeV2 Edge(string id, string from, string to, bool bidirectional, float length)
        {
            return new PublishedGraphEdgeV2
            {
                Id = id, FromNodeId = from, ToNodeId = to, Bidirectional = bidirectional, Enabled = true,
                CostMultiplier = 1f, LengthM = length,
                Geometry = new[] { Point(from == "n-a" ? 0f : 5f), Point(to == "n-c" ? 10f : 5f) }, Provenance = "generated"
            };
        }

        private static AnchorLocalPointV2 Point(float x)
        {
            return new AnchorLocalPointV2 { X = x, Y = 0f, Z = 0f };
        }

        [Test]
        public void FindsRouteAcrossEmbeddedHomeManifest()
        {
            var go = new GameObject("bootstrap-test");
            try
            {
                var bootstrap = go.AddComponent<NavigationBootstrapController>();
                Assert.That(bootstrap.LoadEmbeddedManifest(), Is.True);
                var manifest = bootstrap.Manifest;

                // Test route from "đi" (-0.175, -1.02, -0.443) to "đến" (n-79193cbce8b1e56a52)
                Vector3 startAtDi = new Vector3(-0.175f, -1.02f, -0.443f);
                bool foundToDen = SiteGraphV2Pathfinder.TryFindRoute(
                    manifest, startAtDi, "n-79193cbce8b1e56a52", 1.5f, out SiteGraphV2Route routeToDen, out string err1);
                Assert.That(foundToDen, Is.True, "Route to 'đến' failed: " + err1);
                Assert.That(routeToDen.AnchorLocalPoints.Length, Is.GreaterThan(1));

                // Test route from "đến" (1.56, -1.0, 5.0) to "cửa" (n-a472f7ef4e3a6d6f09)
                Vector3 startAtDen = new Vector3(1.56f, -1.0f, 5.0f);
                bool foundToCua = SiteGraphV2Pathfinder.TryFindRoute(
                    manifest, startAtDen, "n-a472f7ef4e3a6d6f09", 1.5f, out SiteGraphV2Route routeToCua, out string err2);
                Assert.That(foundToCua, Is.True, "Route to 'cửa' failed: " + err2);
                Assert.That(routeToCua.AnchorLocalPoints.Length, Is.GreaterThan(1));

                // Test route from "cửa" (-3.75, -1.05, 1.2) to "đi" (n-a2df9d9b50c0bb184d)
                Vector3 startAtCua = new Vector3(-3.75f, -1.05f, 1.2f);
                bool foundToDi = SiteGraphV2Pathfinder.TryFindRoute(
                    manifest, startAtCua, "n-a2df9d9b50c0bb184d", 1.5f, out SiteGraphV2Route routeToDi, out string err3);
                Assert.That(foundToDi, Is.True, "Route to 'đi' failed: " + err3);
                Assert.That(routeToDi.AnchorLocalPoints.Length, Is.GreaterThan(1));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
