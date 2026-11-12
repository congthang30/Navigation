using System;
using NianticSpatial.NSDK.AR.Sites;

namespace Vps.UnityClient.Authoring
{
    /// <summary>Applies the NSDK Site guide's Production VPS asset and anchor-payload requirements.</summary>
    public static class ProductionVpsAssetFilter
    {
        public static bool IsEligible(AssetInfo asset, string expectedSiteId)
        {
            return !string.IsNullOrWhiteSpace(expectedSiteId) &&
                !string.IsNullOrWhiteSpace(asset.Id) &&
                !string.IsNullOrWhiteSpace(asset.SiteId) &&
                string.Equals(asset.SiteId, expectedSiteId, StringComparison.Ordinal) &&
                asset.AssetType == AssetType.VpsInfo &&
                asset.Deployment == AssetDeploymentType.Production &&
                asset.VpsData.HasValue &&
                !string.IsNullOrWhiteSpace(asset.VpsData.Value.AnchorPayload);
        }
    }
}
