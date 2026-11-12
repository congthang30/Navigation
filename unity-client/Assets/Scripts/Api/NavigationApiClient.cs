using System;
using System.Collections;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Vps.UnityClient.Diagnostics;

namespace Vps.UnityClient.Api
{
    public sealed class NavigationApiResponse
    {
        public bool Success;
        public long StatusCode;
        public string Body;
        public string ErrorMessage;
    }

    [Serializable]
    public sealed class MobileGuestSessionResponse
    {
        public string sessionToken;
        public string expiresAt;
        public string guestId;
    }

    [Serializable]
    public sealed class NianticAccessTokenResponse
    {
        public string accessToken;
        public long expiresAt;
    }

    [Serializable]
    public sealed class AdminLoginRequest
    {
        public string email;
        public string password;
    }

    [Serializable]
    public sealed class AdminUserDto
    {
        public string id;
        public string email;
        public string role;
    }

    [Serializable]
    public sealed class AdminJwtLoginResponse
    {
        public AdminUserDto user;
        public string accessToken;
        public string expiresAt;
    }

    [Serializable]
    public sealed class RecordableFloorListResponse
    {
        public RecordableFloorOption[] items = Array.Empty<RecordableFloorOption>();
    }

    [Serializable]
    public sealed class RecordableFloorOption
    {
        public string id;
        public string buildingName;
        public string displayName;
        public int level;
        public string meshPayloadSha256;

        public string Label
        {
            get { return buildingName + " · " + displayName + " (Level " + level + ")"; }
        }
    }

    public sealed class NavigationApiClient : MonoBehaviour
    {
        private const int RequestTimeoutSeconds = 120;
        private const string ApiPrefix = "/api/v2";
        private const string DevTunnelHost = "rvwvw3kz-3000.asse.devtunnels.ms";
        public const string DefaultApiBaseUrl = "https://rvwvw3kz-3000.asse.devtunnels.ms";

        [SerializeField] private string apiBaseUrl = "";

        public string ApiBaseUrl
        {
            get { return string.IsNullOrWhiteSpace(apiBaseUrl) ? DefaultApiBaseUrl : apiBaseUrl.Trim().TrimEnd('/'); }
            set { apiBaseUrl = value ?? ""; }
        }

        public IEnumerator GetConfig(Action<NavigationApiResponse> completed)
        {
            return Send(ApiPrefix + "/public/navigation/config", "GET", null, null, completed);
        }

        public IEnumerator GetManifest(string floorId, Action<NavigationApiResponse> completed)
        {
            if (string.IsNullOrEmpty(floorId))
            {
                Complete(completed, Failure("The active floor ID is missing."));
                yield break;
            }

            yield return Send(ApiPrefix + "/public/floors/" + Uri.EscapeDataString(floorId) + "/manifest", "GET", null, null, completed);
        }

        public IEnumerator CreateGuestSession(string challengeJson, Action<NavigationApiResponse> completed)
        {
            yield return Send(ApiPrefix + "/mobile/sessions/guest", "POST", challengeJson, null, completed);
        }

        public IEnumerator GetNianticAccessToken(string sessionToken, Action<NavigationApiResponse> completed)
        {
            yield return Send(ApiPrefix + "/mobile/niantic/access-token", "POST", "{}", sessionToken, completed);
        }

        public IEnumerator AdminLoginJwt(string email, string password, Action<NavigationApiResponse> completed)
        {
            var payload = new AdminLoginRequest
            {
                email = email == null ? "" : email.Trim(),
                password = password ?? ""
            };
            yield return Send(ApiPrefix + "/auth/jwt/login", "POST", JsonUtility.ToJson(payload), null, completed);
        }

        public IEnumerator GetCurrentAdmin(string accessToken, Action<NavigationApiResponse> completed)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Complete(completed, Failure("Admin access token is missing."));
                yield break;
            }
            yield return Send(ApiPrefix + "/auth/me", "GET", null, accessToken, completed);
        }

        public IEnumerator AdminLogoutJwt(string accessToken, Action<NavigationApiResponse> completed)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Complete(completed, Failure("Admin access token is missing."));
                yield break;
            }
            yield return Send(ApiPrefix + "/auth/logout", "POST", "{}", accessToken, completed);
        }

        public IEnumerator GetSelectableFloors(
            string siteId,
            string productionAssetId,
            string sessionToken,
            Action<NavigationApiResponse> completed)
        {
            if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(productionAssetId))
            {
                Complete(completed, Failure("A Site and Production asset are required to find matching floors."));
                yield break;
            }

            string path = ApiPrefix + "/mobile/route-recording/floors?siteId=" + Uri.EscapeDataString(siteId) +
                "&productionAssetId=" + Uri.EscapeDataString(productionAssetId);
            yield return Send(path, "GET", null, sessionToken, completed);
        }

        public IEnumerator UploadSiteMesh(string floorId, string json, string sessionToken, Action<NavigationApiResponse> completed)
        {
            if (string.IsNullOrWhiteSpace(floorId))
            {
                Complete(completed, Failure("The target floor ID is missing."));
                yield break;
            }

            yield return Send(ApiPrefix + "/mobile/floors/" + Uri.EscapeDataString(floorId) + "/site-mesh", "PUT", json, sessionToken, completed);
        }

        public IEnumerator UploadRouteCapture(string floorId, string json, string sessionToken, Action<NavigationApiResponse> completed)
        {
            if (string.IsNullOrWhiteSpace(floorId))
            {
                Complete(completed, Failure("The target floor ID is missing."));
                yield break;
            }

            yield return Send(ApiPrefix + "/mobile/floors/" + Uri.EscapeDataString(floorId) + "/route-captures", "POST", json, sessionToken, completed);
        }

        public IEnumerator RevokeGuestSession(string sessionToken, Action<NavigationApiResponse> completed)
        {
            yield return Send(ApiPrefix + "/mobile/sessions/current", "DELETE", null, sessionToken, completed);
        }

        private IEnumerator Send(
            string path,
            string method,
            string json,
            string sessionToken,
            Action<NavigationApiResponse> completed)
        {
            Uri baseUri;
            if (!TryGetBaseUri(out baseUri))
            {
                VpsSystemDiagnostics.Record("HTTP", "blocked before send: invalid API base URL");
                Complete(completed, Failure("The navigation API base URL is not configured."));
                yield break;
            }

            string requestUrl = baseUri.AbsoluteUri.TrimEnd('/') + path;
            float requestStartedAt = Time.realtimeSinceStartup;
            VpsSystemDiagnostics.Record("HTTP", "start " + method + " " + path + " host=" + baseUri.Host);
            using (var request = new UnityWebRequest(requestUrl, method))
            {
                request.timeout = RequestTimeoutSeconds;
#if UNITY_ANDROID && !UNITY_EDITOR
                if (baseUri.Scheme == Uri.UriSchemeHttps &&
                    string.Equals(baseUri.Host, DevTunnelHost, StringComparison.OrdinalIgnoreCase))
                {
                    // UnityTLS on this Android build does not trust the current Microsoft devtunnel chain.
                    // Accept only the public key observed for this exact host; never follow redirects with credentials.
                    request.certificateHandler = new DevTunnelCertificateHandler();
                    request.redirectLimit = 0;
                }
#endif
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Accept", "application/json");
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                    request.useHttpContinue = false;
                    request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                }
                if (!string.IsNullOrWhiteSpace(sessionToken))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
                }

                yield return request.SendWebRequest();
                bool succeeded = request.result == UnityWebRequest.Result.Success &&
                    request.responseCode >= 200 && request.responseCode < 300;
                VpsSystemDiagnostics.Record("HTTP", "finish " + method + " " + path +
                    " status=" + request.responseCode + " result=" + request.result +
                    " elapsedMs=" + Mathf.RoundToInt((Time.realtimeSinceStartup - requestStartedAt) * 1000f));
                string errorMessage = null;
                if (!succeeded)
                {
                    errorMessage = request.responseCode > 0
                        ? "Navigation API request failed with HTTP " + request.responseCode + "."
                        : string.IsNullOrWhiteSpace(request.error)
                            ? "Navigation API connection failed before receiving an HTTP response (" + request.result + ")."
                            : "Navigation API connection failed (" + request.result + "): " + request.error;
                }
                Complete(completed, new NavigationApiResponse
                {
                    Success = succeeded,
                    StatusCode = request.responseCode,
                    Body = request.downloadHandler == null ? null : request.downloadHandler.text,
                    ErrorMessage = errorMessage
                });
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class DevTunnelCertificateHandler : CertificateHandler
        {
            // SHA-256 of the devtunnel leaf certificate's RSA public key (PKCS#1 DER), verified 2026-11-03.
            private static readonly byte[] PublicKeyHash = {
                0x7a, 0x10, 0xf2, 0xb1, 0x4f, 0xb1, 0xed, 0x10,
                0xa8, 0xcf, 0xe6, 0x89, 0xf8, 0x20, 0x3d, 0x92,
                0x5e, 0xc6, 0xea, 0x0a, 0x72, 0x02, 0x78, 0x8f,
                0x21, 0x80, 0x36, 0x0e, 0xfe, 0xf8, 0xf1, 0x67
            };

            protected override bool ValidateCertificate(byte[] certificateData)
            {
                try
                {
                    byte[] der = certificateData;
                    if (der == null || der.Length == 0) return false;
                    if (der[0] == (byte)'-')
                    {
                        string pem = Encoding.ASCII.GetString(der);
                        const string begin = "-----BEGIN CERTIFICATE-----";
                        const string end = "-----END CERTIFICATE-----";
                        int start = pem.IndexOf(begin, StringComparison.Ordinal);
                        if (start < 0) return false;
                        start += begin.Length;
                        int finish = pem.IndexOf(end, start, StringComparison.Ordinal);
                        if (finish < 0) return false;
                        der = Convert.FromBase64String(pem.Substring(start, finish - start));
                    }

                    using (var certificate = new X509Certificate2(der))
                    using (var sha = SHA256.Create())
                    {
                        DateTime now = DateTime.UtcNow;
                        if (now < certificate.NotBefore.ToUniversalTime() ||
                            now > certificate.NotAfter.ToUniversalTime()) return false;
                        byte[] actual = sha.ComputeHash(certificate.GetPublicKey());
                        if (actual.Length != PublicKeyHash.Length) return false;
                        int difference = 0;
                        for (int index = 0; index < actual.Length; index++)
                            difference |= actual[index] ^ PublicKeyHash[index];
                        return difference == 0;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
#endif

        private bool TryGetBaseUri(out Uri uri)
        {
            uri = null;
            if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out uri)) return false;
            return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        }

        private static NavigationApiResponse Failure(string message)
        {
            return new NavigationApiResponse { Success = false, StatusCode = 0, ErrorMessage = message };
        }

        private static void Complete(Action<NavigationApiResponse> completed, NavigationApiResponse response)
        {
            if (completed != null) completed(response);
        }
    }
}
