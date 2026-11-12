using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Auth;
using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Sites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Vps.UnityClient.Tests.LiveAuth
{
    public sealed class NsdkDeveloperAuthSmokeTests
    {
        private const int RequestTimeoutSeconds = 20;

        [UnityTest]
        [Explicit("Calls live Niantic Sites APIs. Requires an organization-scoped token with Sites access and an authorized Site.")]
        [Timeout(90000)]
        public IEnumerator DeveloperTokenCanReadAuthorizedSitesAndAssets()
        {
            var managerObject = new GameObject("AUTH-01 live Sites client");
            try
            {
                var sites = managerObject.AddComponent<SitesClientManager>();
                sites.RequestTimeoutSeconds = 20;
                var settings = NsdkSettingsHelper.ActiveSettings;
                Assert.That(settings, Is.Not.Null, "NSDK runtime settings were not initialized.");

                var organizationId = GetOrganizationIdClaim(settings.AccessToken);
                Assert.That(
                    organizationId,
                    Is.Not.Null.And.Not.Empty,
                    "The configured access token has no organizationId claim. Token contents are never logged.");

                var organizationTask = sites.GetOrganizationInfoAsync(organizationId);
                yield return WaitForCompletion(organizationTask, "organization query");

                var organizations = organizationTask.Result;
                Assert.That(
                    organizations.Status,
                    Is.EqualTo(SitesRequestStatus.Success),
                    $"Organization query failed with {organizations.Error}.");
                Assert.That(organizations.Organizations, Is.Not.Empty, "The credential returned no organization for its organizationId.");

                var siteTask = sites.GetSitesForOrganizationAsync(organizationId);
                yield return WaitForCompletion(siteTask, "site query");

                var siteResult = siteTask.Result;
                Assert.That(
                    siteResult.Status,
                    Is.EqualTo(SitesRequestStatus.Success),
                    $"Site query failed with {siteResult.Error}.");

                var siteCount = siteResult.Sites.Length;
                string firstAuthorizedSiteId = null;
                foreach (var site in siteResult.Sites)
                {
                    if (firstAuthorizedSiteId == null)
                    {
                        firstAuthorizedSiteId = site.Id;
                    }
                }

                Assert.That(siteCount, Is.GreaterThan(0), "No Sites are visible to this credential.");

                var assetTask = sites.GetAssetsForSiteAsync(firstAuthorizedSiteId);
                yield return WaitForCompletion(assetTask, "site asset query");
                var assetResult = assetTask.Result;
                Assert.That(
                    assetResult.Status,
                    Is.EqualTo(SitesRequestStatus.Success),
                    $"Site asset query failed with {assetResult.Error}.");

                Debug.Log(
                    $"AUTH-01 live Sites smoke PASS; unity={Application.unityVersion}; " +
                    $"device={SystemInfo.deviceModel}; organizations={organizations.Organizations.Length}; " +
                    $"sites={siteCount}; firstSiteAssetCount={assetResult.Assets.Length}. " +
                    "Names, IDs, payloads, and credentials were omitted.");
            }
            finally
            {
                UnityEngine.Object.Destroy(managerObject);
            }
        }

        [UnityTest]
        [Explicit("Calls live Niantic Sites APIs. Resolves IDs for authorized Sites with a Production VPS asset.")]
        [Timeout(90000)]
        public IEnumerator DeveloperTokenCanReadProductionVpsAssetPayload()
        {
            var managerObject = new GameObject("AUTH-01 Production VPS asset client");
            try
            {
                var sites = managerObject.AddComponent<SitesClientManager>();
                sites.RequestTimeoutSeconds = RequestTimeoutSeconds;
                var settings = NsdkSettingsHelper.ActiveSettings;
                Assert.That(settings, Is.Not.Null, "NSDK runtime settings were not initialized.");

                var organizationId = GetOrganizationIdClaim(settings.AccessToken);
                Assert.That(
                    organizationId,
                    Is.Not.Null.And.Not.Empty,
                    "The configured access token has no organizationId claim. Token contents are never logged.");

                var siteTask = sites.GetSitesForOrganizationAsync(organizationId);
                yield return WaitForCompletion(siteTask, "Production VPS site query");
                var siteResult = siteTask.Result;
                Assert.That(
                    siteResult.Status,
                    Is.EqualTo(SitesRequestStatus.Success),
                    $"Site query failed with {siteResult.Error}.");
                Assert.That(siteResult.Sites, Is.Not.Empty, "No Sites are visible to this credential.");

                var productionVpsAssetReferences = new List<string>();
                foreach (var site in siteResult.Sites)
                {
                    var assetsTask = sites.GetAssetsForSiteAsync(site.Id);
                    yield return WaitForCompletion(assetsTask, "Production VPS asset query");
                    var assetResult = assetsTask.Result;
                    Assert.That(
                        assetResult.Status,
                        Is.EqualTo(SitesRequestStatus.Success),
                        $"Asset query failed for an authorized Site: {assetResult.Error}.");

                    foreach (var asset in assetResult.Assets)
                    {
                        if (asset.AssetType == AssetType.VpsInfo &&
                            asset.Deployment == AssetDeploymentType.Production &&
                            asset.VpsData.HasValue &&
                            !string.IsNullOrWhiteSpace(asset.VpsData.Value.AnchorPayload) &&
                            !string.IsNullOrWhiteSpace(site.Id) &&
                            string.Equals(asset.SiteId, site.Id, StringComparison.Ordinal) &&
                            !string.IsNullOrWhiteSpace(asset.Id))
                        {
                            productionVpsAssetReferences.Add($"siteId={site.Id},assetId={asset.Id}");
                        }
                    }
                }

                Assert.That(
                    productionVpsAssetReferences,
                    Is.Not.Empty,
                    "No authorized Site returned a Production VPS asset with an anchor payload. Token and payload are never logged.");

                Debug.Log(
                    "AUTH-01 Production VPS asset query PASS; matching Site/asset IDs for Admin binding: " +
                    string.Join("; ", productionVpsAssetReferences) +
                    ". Token and anchor payload were omitted.");
            }
            finally
            {
                UnityEngine.Object.Destroy(managerObject);
            }
        }

        [UnityTest]
        [Explicit("Calls the live Niantic Sites API. Configure a locally stored expired or revoked organization-scoped developer token first.")]
        [Timeout(90000)]
        public IEnumerator ExpiredOrRevokedDeveloperTokenFailsClosed()
        {
            var managerObject = new GameObject("AUTH-01 expired or revoked token client");
            try
            {
                var sites = managerObject.AddComponent<SitesClientManager>();
                sites.RequestTimeoutSeconds = RequestTimeoutSeconds;
                var accessToken = ApplyConfiguredAccessToken();
                var organizationId = GetOrganizationIdClaim(accessToken);
                Assert.That(
                    organizationId,
                    Is.Not.Null.And.Not.Empty,
                    "Configure an expired or revoked JWT-shaped developer token with an organizationId claim locally. Token contents are never logged.");

                var request = sites.GetOrganizationInfoAsync(organizationId);
                yield return WaitForCompletion(request, "expired or revoked token organization query");

                var result = request.Result;
                Assert.That(result.Status, Is.EqualTo(SitesRequestStatus.Failed), "The invalid credential unexpectedly returned organization data.");
                Assert.That(result.Error, Is.EqualTo(SitesError.HttpForbidden), "The SDK must report an authorization denial; network and local errors are not auth-denial evidence.");
                Assert.That(result.Organizations, Is.Empty, "A denied request must not return organization data.");

                Debug.Log("AUTH-01 expired/revoked credential fail-closed PASS; SDK reported authorization denial; identifiers and credential omitted.");
            }
            finally
            {
                UnityEngine.Object.Destroy(managerObject);
            }
        }

        [UnityTest]
        [Explicit("Calls the live Niantic Sites API. Configure an organization-scoped developer token without Scaniverse API access first.")]
        [Timeout(90000)]
        public IEnumerator DeveloperTokenWithoutSitesPermissionFailsClosed()
        {
            var managerObject = new GameObject("AUTH-01 insufficient Sites scope client");
            try
            {
                var sites = managerObject.AddComponent<SitesClientManager>();
                sites.RequestTimeoutSeconds = RequestTimeoutSeconds;
                var accessToken = ApplyConfiguredAccessToken();
                var organizationId = GetOrganizationIdClaim(accessToken);
                Assert.That(
                    organizationId,
                    Is.Not.Null.And.Not.Empty,
                    "Configure a JWT-shaped developer token with an organizationId claim and without Scaniverse API access locally. Token contents are never logged.");

                var request = sites.GetSitesForOrganizationAsync(organizationId);
                yield return WaitForCompletion(request, "insufficient-Sites-permission query");

                var result = request.Result;
                Assert.That(result.Status, Is.EqualTo(SitesRequestStatus.Failed), "The credential unexpectedly returned Sites data without Sites permission.");
                Assert.That(result.Error, Is.EqualTo(SitesError.HttpForbidden), "A live HTTP 403 is required; network and local errors are not permission-denial evidence.");
                Assert.That(result.Sites, Is.Empty, "A denied request must not return Site data.");

                Debug.Log("AUTH-01 insufficient Sites permission fail-closed PASS; service returned HTTP 403; identifiers and credential omitted.");
            }
            finally
            {
                UnityEngine.Object.Destroy(managerObject);
            }
        }

        private static string ApplyConfiguredAccessToken()
        {
            var settings = NsdkSettingsHelper.ActiveSettings;
            Assert.That(settings, Is.Not.Null, "NSDK runtime settings were not initialized.");

            var accessToken = settings.AccessToken;
            Assert.That(accessToken, Is.Not.Null.And.Not.Empty, "No local NSDK access token is configured.");

            AuthClient.StaticLogout();
            AuthClient.SetAccessToken(accessToken);
            return accessToken;
        }

        private static string GetOrganizationIdClaim(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            var segments = accessToken.Split('.');
            if (segments.Length != 3)
            {
                return null;
            }

            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 0:
                    break;
                case 2:
                    payload += "==";
                    break;
                case 3:
                    payload += "=";
                    break;
                default:
                    return null;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return JsonUtility.FromJson<DeveloperTokenClaims>(json)?.organizationId;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        [Serializable]
        private sealed class DeveloperTokenClaims
        {
            public string organizationId;
        }

        private static IEnumerator WaitForCompletion<T>(Task<T> task, string operation)
        {
            var deadline = Time.realtimeSinceStartup + RequestTimeoutSeconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, $"The {operation} timed out.");
            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion), $"The {operation} did not complete successfully.");
        }
    }
}
