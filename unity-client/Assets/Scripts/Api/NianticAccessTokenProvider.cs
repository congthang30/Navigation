using System;
using System.Collections;
using System.Text;
using NianticSpatial.NSDK.AR.Auth;
using UnityEngine;
using UnityEngine.Networking;
using Vps.UnityClient.Diagnostics;

namespace Vps.UnityClient.Api
{
    /// <summary>Obtains short-lived NSDK access tokens from the app backend or directly from Niantic Identity service.</summary>
    public sealed class NianticAccessTokenProvider : MonoBehaviour
    {
        private const string NianticIdentityTokenUrl = "https://spatial-identity.nianticspatial.com/oauth/token";
        public const string DefaultNianticApiKey = "WtsAhILI4Czu2RXsGLWfqL9ru_7n5MeGExVOHLnqM3A";
        private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);

        private bool directNianticAuth = true;
        private string nianticApiKey = DefaultNianticApiKey;
        private NavigationApiClient apiClient;

        private string accessToken = string.Empty;
        private string accessTokenSession = string.Empty;
        private DateTime accessTokenExpiresAtUtc = DateTime.MinValue;
        private string lastApplicationSessionToken = string.Empty;
        private bool requestInFlight;
        private bool refreshStopped = true;
        private int stateGeneration;
        private Coroutine refreshCoroutine;

        public bool DirectNianticAuth
        {
            get => directNianticAuth;
            set => directNianticAuth = value;
        }

        public string NianticApiKey
        {
            get => nianticApiKey;
            set => nianticApiKey = value;
        }

        public void Initialize(NavigationApiClient client)
        {
            if (client != null) apiClient = client;
        }

        public IEnumerator EnsureAccessToken(string applicationSessionToken, Action<bool, string> completed)
        {
            return EnsureAccessTokenInternal(applicationSessionToken, completed, true);
        }

        private IEnumerator EnsureAccessTokenInternal(
            string applicationSessionToken,
            Action<bool, string> completed,
            bool maintainRefreshLoop)
        {
            VpsSystemDiagnostics.Record("AUTH", "access-token ensure started adminSession=" + !string.IsNullOrWhiteSpace(applicationSessionToken));
            int requestGeneration = stateGeneration;
            while (requestInFlight)
            {
                yield return null;
                if (requestGeneration != stateGeneration)
                {
                    Complete(completed, false, "Mobile authentication session ended.");
                    yield break;
                }
            }

            if (apiClient == null) apiClient = GetComponent<NavigationApiClient>();

            string bearerToken = applicationSessionToken == null ? string.Empty : applicationSessionToken.Trim();

            if (HasCurrentAccessToken(bearerToken, RefreshWindow))
            {
                if (TryApplyAccessToken(accessToken))
                {
                    VpsSystemDiagnostics.SetState("Niantic token", "cached and applied; expirySeconds=" + Mathf.Max(0, (int)(accessTokenExpiresAtUtc - DateTime.UtcNow).TotalSeconds));
                    lastApplicationSessionToken = bearerToken;
                    if (maintainRefreshLoop) StartRefreshLoop();
                    Complete(completed, true, null);
                    yield break;
                }
                ClearCachedAccessToken();
            }

            requestInFlight = true;
            string newAccessToken = null;
            DateTime newAccessExpiry = DateTime.MinValue;
            string fetchError = null;

            if (directNianticAuth || apiClient == null)
            {
                VpsSystemDiagnostics.Record("AUTH", "direct Niantic token exchange started");
                yield return FetchDirectNianticToken(response =>
                {
                    if (response != null && response.Success && TryReadAccessToken(response.Body, out newAccessToken, out newAccessExpiry))
                    {
                        fetchError = null;
                    }
                    else
                    {
                        fetchError = response == null ? "Direct Niantic token exchange did not complete." : response.ErrorMessage;
                    }
                });
            }
            else
            {
                VpsSystemDiagnostics.Record("AUTH", "backend access-token request started");
                NavigationApiResponse tokenResponse = null;
                yield return apiClient.GetNianticAccessToken(
                    bearerToken,
                    response => tokenResponse = response);

                if (tokenResponse != null && tokenResponse.Success && TryReadAccessToken(tokenResponse.Body, out newAccessToken, out newAccessExpiry))
                {
                    fetchError = null;
                }
                else
                {
                    VpsSystemDiagnostics.Record("AUTH", "backend access-token failed; falling back to direct exchange");
                    yield return FetchDirectNianticToken(response =>
                    {
                        if (response != null && response.Success && TryReadAccessToken(response.Body, out newAccessToken, out newAccessExpiry))
                        {
                            fetchError = null;
                        }
                        else
                        {
                            fetchError = tokenResponse == null ? "Niantic access token request did not complete." : tokenResponse.ErrorMessage;
                        }
                    });
                }
            }

            if (requestGeneration != stateGeneration)
            {
                requestInFlight = false;
                Complete(completed, false, "Mobile authentication session ended.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(newAccessToken) || newAccessExpiry <= DateTime.UtcNow)
            {
                requestInFlight = false;
                VpsSystemDiagnostics.Record("AUTH", "Niantic token fetch failed: " + fetchError);
                VpsSystemDiagnostics.SetState("Niantic token", "unavailable");
                if (HasCurrentAccessToken(bearerToken, TimeSpan.Zero) && TryApplyAccessToken(accessToken))
                {
                    lastApplicationSessionToken = bearerToken;
                    if (maintainRefreshLoop) StartRefreshLoop();
                    Complete(completed, true, null);
                    yield break;
                }

                ClearCachedAccessToken();
                Complete(completed, false, string.IsNullOrWhiteSpace(fetchError)
                    ? "Niantic access token request did not complete."
                    : fetchError);
                yield break;
            }

            accessToken = newAccessToken;
            accessTokenSession = bearerToken;
            accessTokenExpiresAtUtc = newAccessExpiry;
            lastApplicationSessionToken = bearerToken;
            bool applied = TryApplyAccessToken(accessToken);
            requestInFlight = false;
            if (!applied)
            {
                VpsSystemDiagnostics.Record("AUTH", "NSDK rejected the access token");
                VpsSystemDiagnostics.SetState("Niantic token", "received but NSDK rejected it");
                ClearCachedAccessToken();
                Complete(completed, false, "NSDK rejected the access token.");
                yield break;
            }

            VpsSystemDiagnostics.Record("AUTH", "token applied to NSDK; expirySeconds=" + Mathf.Max(0, (int)(accessTokenExpiresAtUtc - DateTime.UtcNow).TotalSeconds));
            VpsSystemDiagnostics.SetState("Niantic token", "active; expirySeconds=" + Mathf.Max(0, (int)(accessTokenExpiresAtUtc - DateTime.UtcNow).TotalSeconds));
            if (maintainRefreshLoop) StartRefreshLoop();
            Complete(completed, true, null);
        }

        private IEnumerator FetchDirectNianticToken(Action<NavigationApiResponse> callback)
        {
            string key = string.IsNullOrWhiteSpace(nianticApiKey) ? DefaultNianticApiKey : nianticApiKey.Trim();
            string bodyJson = "{\"grantType\":\"exchange_api_key_access_token\",\"apiKey\":\"" + key + "\"}";
            byte[] bodyRaw = Encoding.UTF8.GetBytes(bodyJson);

            using (var request = new UnityWebRequest(NianticIdentityTokenUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                request.timeout = 15;

                yield return request.SendWebRequest();

                bool success = request.result == UnityWebRequest.Result.Success &&
                               request.responseCode >= 200 && request.responseCode < 300;

                NavigationApiResponse response = new NavigationApiResponse
                {
                    Success = success,
                    StatusCode = request.responseCode,
                    Body = request.downloadHandler?.text,
                    ErrorMessage = success ? null : ("Niantic OAuth error: " + request.error + " (code " + request.responseCode + ")")
                };

                callback?.Invoke(response);
            }
        }

        public void Logout()
        {
            VpsSystemDiagnostics.Record("AUTH", "logout cleared NSDK token cache");
            stateGeneration++;
            StopRefreshLoop();
            ClearCachedAccessToken();
            lastApplicationSessionToken = string.Empty;
        }

        public void StopRefreshing()
        {
            StopRefreshLoop();
        }

        private bool HasCurrentAccessToken(string bearerToken, TimeSpan minimumLifetime)
        {
            return !string.IsNullOrWhiteSpace(accessToken) &&
                   string.Equals(accessTokenSession, bearerToken, StringComparison.Ordinal) &&
                   accessTokenExpiresAtUtc > DateTime.UtcNow.Add(minimumLifetime);
        }

        private void StartRefreshLoop()
        {
            refreshStopped = false;
            if (refreshCoroutine == null && isActiveAndEnabled) refreshCoroutine = StartCoroutine(RefreshLoop());
        }

        private IEnumerator RefreshLoop()
        {
            while (!refreshStopped)
            {
                yield return new WaitForSecondsRealtime(30f);
                if (refreshStopped) break;
                bool accessTokenNeedsRefresh = !HasCurrentAccessToken(lastApplicationSessionToken, RefreshWindow);
                if (!accessTokenNeedsRefresh) continue;

                bool refreshed = false;
                DateTime expiryBeforeRefresh = accessTokenExpiresAtUtc;
                yield return EnsureAccessTokenInternal(lastApplicationSessionToken, (success, _) => refreshed = success, false);
                if ((!refreshed || accessTokenExpiresAtUtc <= expiryBeforeRefresh) && !refreshStopped)
                    yield return new WaitForSecondsRealtime(240f);
            }
            refreshCoroutine = null;
        }

        private void StopRefreshLoop()
        {
            refreshStopped = true;
        }

        private void ClearCachedAccessToken()
        {
            accessToken = string.Empty;
            accessTokenSession = string.Empty;
            accessTokenExpiresAtUtc = DateTime.MinValue;
            try { AuthClient.StaticLogout(); }
            catch (Exception) { }
        }

        private static bool TryApplyAccessToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            try
            {
                AuthClient.SetAccessToken(token);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryReadAccessToken(string json, out string token, out DateTime expiresAtUtc)
        {
            token = string.Empty;
            expiresAtUtc = DateTime.MinValue;
            try
            {
                NianticAccessTokenResponse response = JsonUtility.FromJson<NianticAccessTokenResponse>(json);
                if (response == null || string.IsNullOrWhiteSpace(response.accessToken) || response.expiresAt <= 0)
                    return false;

                expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(response.expiresAt).UtcDateTime;
                if (expiresAtUtc <= DateTime.UtcNow) return false;
                token = response.accessToken;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Complete(Action<bool, string> completed, bool success, string message)
        {
            completed?.Invoke(success, message);
        }
    }
}
