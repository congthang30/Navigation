using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Vps.UnityClient.Api;
using Vps.UnityClient.Bootstrap;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class NavigationContractParserTests
    {
        private const string FloorId = "66f000000000000000000011";
        private const string VersionId = "66f000000000000000000022";

        private const string EmptyConfig = "{\"schemaVersion\":2,\"activeFloorId\":null,\"activeVersionId\":null,\"configRevision\":0}";
        private const string ActiveConfig = "{\"schemaVersion\":2,\"activeFloorId\":\"66f000000000000000000011\",\"activeVersionId\":\"66f000000000000000000022\",\"configRevision\":7}";
        private const string Manifest = "{\"schemaVersion\":2,\"versionId\":\"66f000000000000000000022\",\"version\":3,\"floorId\":\"66f000000000000000000011\",\"buildingId\":\"66f000000000000000000033\",\"siteBinding\":{\"siteId\":\"site-a\",\"productionAssetId\":\"asset-a\",\"payloadSha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"},\"sourceDraftRevision\":12,\"siteMeshSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"graph\":{\"nodes\":[{\"id\":\"n-a\",\"kind\":\"entrance\",\"anchorLocal\":{\"x\":0,\"y\":0,\"z\":0},\"provenance\":\"generated\"},{\"id\":\"n-b\",\"kind\":\"waypoint\",\"anchorLocal\":{\"x\":2,\"y\":0,\"z\":0},\"provenance\":\"generated\"}],\"edges\":[{\"id\":\"e-a-b\",\"fromNodeId\":\"n-a\",\"toNodeId\":\"n-b\",\"bidirectional\":true,\"enabled\":true,\"costMultiplier\":1,\"lengthM\":2,\"geometry\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":2,\"y\":0,\"z\":0}],\"provenance\":\"generated\"}],\"pois\":[{\"id\":\"p-room\",\"label\":\"Room A\",\"category\":\"room\",\"isActive\":true,\"nodeId\":\"n-b\",\"anchorLocal\":{\"x\":2,\"y\":0,\"z\":0},\"provenance\":\"captured\"}]},\"publishedAt\":\"2026-11-02T04:00:00.000Z\"}";

        [Test]
        public void ParsesEmptyV2ConfigAndRequiresPairedActivePointers()
        {
            PublicNavigationConfigV2 config;
            string error;
            Assert.That(SiteGraphV2ContractParser.TryParseConfig(EmptyConfig, out config, out error), Is.True, error);
            Assert.That(config.SchemaVersion, Is.EqualTo(2));
            Assert.That(config.ActiveFloorId, Is.Null);
            Assert.That(SiteGraphV2ContractParser.TryParseConfig(
                EmptyConfig.Replace("\"activeVersionId\":null", "\"activeVersionId\":\"v1\""), out config, out error), Is.False);
        }

        [Test]
        public void ParsesPublishedAnchorLocalV2ManifestMatchingConfig()
        {
            PublishedSiteGraphManifest manifest;
            string error;
            bool mismatch;
            Assert.That(SiteGraphV2ContractParser.TryParseManifest(
                Manifest, FloorId, VersionId, out manifest, out error, out mismatch), Is.True, error);
            Assert.That(mismatch, Is.False);
            Assert.That(manifest.Graph.Nodes, Has.Length.EqualTo(2));
            Assert.That(manifest.Graph.Edges[0].Geometry[1].X, Is.EqualTo(2));
            Assert.That(manifest.Graph.Pois[0].NodeId, Is.EqualTo("n-b"));
        }

        [TestCase("planRevision", "1")]
        [TestCase("calibration", "{}")]
        [TestCase("pixel", "{}")]
        public void RejectsAnyLegacySpatialField(string field, string value)
        {
            JObject root = JObject.Parse(Manifest);
            root[field] = JToken.Parse(value);
            PublishedSiteGraphManifest parsed;
            string error;
            bool mismatch;
            Assert.That(SiteGraphV2ContractParser.TryParseManifest(root.ToString(), FloorId, VersionId, out parsed, out error, out mismatch), Is.False);
            Assert.That(error, Does.Contain(field));
        }

        [Test]
        public void RejectsNonFiniteAndUnrepresentableAnchorLocalCoordinates()
        {
            JObject root = JObject.Parse(Manifest);
            root["graph"]["nodes"][0]["anchorLocal"]["x"] = 1e100;
            PublishedSiteGraphManifest parsed;
            string error;
            bool mismatch;
            Assert.That(SiteGraphV2ContractParser.TryParseManifest(root.ToString(), FloorId, VersionId, out parsed, out error, out mismatch), Is.False);
            Assert.That(error, Does.Contain("finite"));
        }

        [Test]
        public void RejectsInconsistentEdgeGeometryAndReportsActivePointerMismatch()
        {
            JObject root = JObject.Parse(Manifest);
            root["graph"]["edges"][0]["geometry"][1]["x"] = 3;
            PublishedSiteGraphManifest parsed;
            string error;
            bool mismatch;
            Assert.That(SiteGraphV2ContractParser.TryParseManifest(root.ToString(), FloorId, VersionId, out parsed, out error, out mismatch), Is.False);
            Assert.That(error, Does.Contain("endpoints"));

            Assert.That(SiteGraphV2ContractParser.TryParseManifest(Manifest, FloorId, "66f000000000000000000099", out parsed, out error, out mismatch), Is.False);
            Assert.That(mismatch, Is.True);
        }

        [Test]
        public void RejectsUnsupportedSchemaAndGraphEnumValues()
        {
            JObject root = JObject.Parse(Manifest);
            root["graph"]["nodes"][0]["kind"] = "destination";
            PublishedSiteGraphManifest parsed;
            string error;
            bool mismatch;
            Assert.That(SiteGraphV2ContractParser.TryParseManifest(root.ToString(), FloorId, VersionId, out parsed, out error, out mismatch), Is.False);
            Assert.That(error, Does.Contain("kind"));
            Assert.That(SiteGraphV2ContractParser.TryParseConfig(ActiveConfig.Replace("\"schemaVersion\":2", "\"schemaVersion\":1"), out PublicNavigationConfigV2 config, out error), Is.False);
        }

        [Test]
        public void LoadsEmbeddedManifestSuccessfully()
        {
            var go = new UnityEngine.GameObject("test-bootstrap");
            try
            {
                var controller = go.AddComponent<NavigationBootstrapController>();
                bool loaded = controller.LoadEmbeddedManifest();
                Assert.That(loaded, Is.True, "LoadEmbeddedManifest failed: " + controller.ErrorMessage);
                Assert.That(controller.State, Is.EqualTo(NavigationBootstrapState.ManifestLoaded));
                Assert.That(controller.Manifest, Is.Not.Null);
                Assert.That(controller.Manifest.Graph.Nodes, Has.Length.EqualTo(5));
                Assert.That(controller.Manifest.Graph.Edges, Has.Length.EqualTo(4));
                Assert.That(controller.Manifest.Graph.Pois, Has.Length.EqualTo(3));
                Assert.That(controller.ActiveFloorId, Is.EqualTo("6ae860aca2f51d025151f60c"));
                Assert.That(controller.ActiveVersionId, Is.EqualTo("6aec0890f6fec623b1161e00"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
