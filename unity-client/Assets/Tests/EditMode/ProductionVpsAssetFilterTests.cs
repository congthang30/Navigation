using System;
using NianticSpatial.NSDK.AR.Sites;
using NUnit.Framework;
using Vps.UnityClient.Authoring;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class ProductionVpsAssetFilterTests
    {
        [Test]
        public void AcceptsProductionVpsPayloadWhenStatusIsUnspecified()
        {
            AssetInfo asset = CreateAsset(status: AssetStatusType.Unspecified);
            Assert.That(ProductionVpsAssetFilter.IsEligible(asset, "site-a"), Is.True);
        }

        [Test]
        public void RejectsAssetForAnotherSite()
        {
            Assert.That(ProductionVpsAssetFilter.IsEligible(CreateAsset(siteId: "site-b"), "site-a"), Is.False);
        }

        [Test]
        public void RejectsNonProductionOrNonVpsAssets()
        {
            Assert.That(ProductionVpsAssetFilter.IsEligible(CreateAsset(deployment: AssetDeploymentType.Unspecified), "site-a"), Is.False);
            Assert.That(ProductionVpsAssetFilter.IsEligible(CreateAsset(type: AssetType.Mesh), "site-a"), Is.False);
        }

        [Test]
        public void RejectsMissingOrWhitespaceAnchorPayload()
        {
            Assert.That(ProductionVpsAssetFilter.IsEligible(CreateAsset(payload: null), "site-a"), Is.False);
            Assert.That(ProductionVpsAssetFilter.IsEligible(CreateAsset(payload: "  "), "site-a"), Is.False);
        }

        private static AssetInfo CreateAsset(
            string siteId = "site-a",
            string payload = "opaque-payload",
            AssetStatusType status = AssetStatusType.Active,
            AssetDeploymentType deployment = AssetDeploymentType.Production,
            AssetType type = AssetType.VpsInfo)
        {
            AssetVpsData? vpsData = payload == null ? (AssetVpsData?)null : new AssetVpsData(payload);
            return new AssetInfo(
                "asset-a", siteId, "Test asset", null, type, status, deployment,
                null, null, vpsData, null, AssetPipelineJobStatus.Unspecified, Array.Empty<string>());
        }
    }
}
