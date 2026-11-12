using System;
using Vps.UnityClient.Api;

namespace Vps.UnityClient.Navigation
{
    public static class SiteGraphV2NavigationGate
    {
        public static bool BindingMatches(PublishedSiteGraphManifest manifest, string siteId, string assetId, string payloadSha256)
        {
            return manifest != null && manifest.SiteBinding != null &&
                string.Equals(manifest.SiteBinding.SiteId, siteId, StringComparison.Ordinal) &&
                string.Equals(manifest.SiteBinding.ProductionAssetId, assetId, StringComparison.Ordinal) &&
                string.Equals(manifest.SiteBinding.PayloadSha256, payloadSha256, StringComparison.Ordinal);
        }

        public static bool CanRender(bool stableTracking, bool bindingMatches, bool hasRoute)
        {
            return stableTracking && bindingMatches && hasRoute;
        }
    }
}
