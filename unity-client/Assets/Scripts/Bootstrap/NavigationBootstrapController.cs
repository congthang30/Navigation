using System;
using System.Collections;
using UnityEngine;
using Vps.UnityClient.Api;
using Vps.UnityClient.Diagnostics;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Bootstrap
{
    public enum NavigationBootstrapState
    {
        Boot,
        FetchingConfig,
        FetchingManifest,
        NoActiveMap,
        ManifestLoaded,
        Error
    }

    /// <summary>Loads the only supported public navigation contract: anchor-local Site Graph V2.</summary>
    public sealed class NavigationBootstrapController : MonoBehaviour
    {
        [SerializeField] private NavigationApiClient apiClient = null;
        private bool useEmbeddedManifest = true;
        private PublicNavigationConfigV2 activeConfig;

        public event Action<NavigationBootstrapState> StateChanged;
        public NavigationBootstrapState State { get; private set; }
        public PublishedSiteGraphManifest Manifest { get; private set; }
        public string ErrorMessage { get; private set; }
        public string ActiveFloorId => activeConfig == null ? null : activeConfig.ActiveFloorId;
        public string ActiveVersionId => activeConfig == null ? null : activeConfig.ActiveVersionId;
        public bool IsUsingLocalOverride { get; private set; }
        public string LocalManifestPathOverride { get; set; }

        public bool UseEmbeddedManifest
        {
            get => useEmbeddedManifest;
            set => useEmbeddedManifest = value;
        }

        private void Awake()
        {
            State = NavigationBootstrapState.Boot;
            if (useEmbeddedManifest)
            {
                LoadEmbeddedOrLocalManifest();
            }
        }

        private void Start()
        {
            if (Manifest == null)
            {
                Reload();
            }
        }

        public void Reload()
        {
            StopAllCoroutines();
            activeConfig = null;
            Manifest = null;
            ErrorMessage = null;
            IsUsingLocalOverride = false;

            if (useEmbeddedManifest)
            {
                if (LoadEmbeddedOrLocalManifest())
                {
                    return;
                }
            }

            NavigationApiClient client = apiClient != null ? apiClient : GetComponent<NavigationApiClient>();
            if (client == null)
            {
                Fail("Navigation API client is not configured.");
                return;
            }
            StartCoroutine(FetchFromApi(client));
        }

        public bool LoadEmbeddedOrLocalManifest()
        {
            StopAllCoroutines();
            activeConfig = null;
            Manifest = null;
            ErrorMessage = null;
            IsUsingLocalOverride = false;
            SetState(NavigationBootstrapState.Boot);

            string configJson = ReadEmbeddedConfigJson();
            ProcessConfigJson(configJson);
            if (State == NavigationBootstrapState.FetchingManifest &&
                LocalSiteGraphStore.HasLocalOverride(LocalManifestPathOverride))
            {
                if (LocalSiteGraphStore.TryLoadManifest(
                        activeConfig.ActiveFloorId,
                        activeConfig.ActiveVersionId,
                        out PublishedSiteGraphManifest localManifest,
                        out string localError,
                        LocalManifestPathOverride))
                {
                    Manifest = localManifest;
                    IsUsingLocalOverride = true;
                    SetState(NavigationBootstrapState.ManifestLoaded);
                    VpsSystemDiagnostics.Record("NAV", "local offline manifest loaded: floor=" + ActiveFloorId + " pois=" +
                        (localManifest.Graph != null && localManifest.Graph.Pois != null ? localManifest.Graph.Pois.Length : 0));
                    return true;
                }

                VpsSystemDiagnostics.Record("NAV", "local offline manifest invalid, falling back to embedded: " + localError);
            }

            return LoadEmbeddedManifest();
        }

        public bool LoadEmbeddedManifest()
        {
            StopAllCoroutines();
            activeConfig = null;
            Manifest = null;
            ErrorMessage = null;
            IsUsingLocalOverride = false;
            SetState(NavigationBootstrapState.Boot);

            string configJson = ReadEmbeddedConfigJson();
            string manifestJson = ReadEmbeddedManifestJson();

            ProcessConfigJson(configJson);
            if (State != NavigationBootstrapState.FetchingManifest)
            {
                return false;
            }

            bool mismatch = ProcessManifestJson(manifestJson);
            if (mismatch || State != NavigationBootstrapState.ManifestLoaded)
            {
                return false;
            }

            VpsSystemDiagnostics.Record("NAV", "embedded manifest loaded successfully: floor=" + ActiveFloorId + " version=" + ActiveVersionId);
            return true;
        }

        public bool ApplyAndSaveLocalManifest(PublishedSiteGraphManifest updatedManifest, out string error)
        {
            if (updatedManifest == null)
            {
                error = "Bản đồ cập nhật không hợp lệ.";
                return false;
            }

            if (activeConfig == null)
            {
                ProcessConfigJson(ReadEmbeddedConfigJson());
            }

            if (activeConfig == null ||
                !string.Equals(updatedManifest.FloorId, activeConfig.ActiveFloorId, StringComparison.Ordinal) ||
                !string.Equals(updatedManifest.VersionId, activeConfig.ActiveVersionId, StringComparison.Ordinal))
            {
                error = "Phiên bản bản đồ không khớp với cấu hình hiện tại.";
                return false;
            }

            if (!LocalSiteGraphStore.SaveManifest(updatedManifest, out error, LocalManifestPathOverride))
            {
                return false;
            }

            if (!LocalSiteGraphStore.TryLoadManifest(
                    activeConfig.ActiveFloorId,
                    activeConfig.ActiveVersionId,
                    out PublishedSiteGraphManifest verifiedManifest,
                    out error,
                    LocalManifestPathOverride))
            {
                return false;
            }

            Manifest = verifiedManifest;
            IsUsingLocalOverride = true;
            ErrorMessage = null;
            SetState(NavigationBootstrapState.ManifestLoaded);
            VpsSystemDiagnostics.Record("NAV", "saved local offline manifest: pois=" +
                (verifiedManifest.Graph != null && verifiedManifest.Graph.Pois != null ? verifiedManifest.Graph.Pois.Length : 0));
            return true;
        }

        public bool ResetLocalManifestToDefault()
        {
            LocalSiteGraphStore.DeleteLocalOverride(LocalManifestPathOverride);
            return LoadEmbeddedManifest();
        }

        private static string ReadEmbeddedConfigJson()
        {
            string configJson = null;
            try
            {
                TextAsset configAsset = Resources.Load<TextAsset>("ActiveNavigationConfig");
                if (configAsset != null && !string.IsNullOrWhiteSpace(configAsset.text))
                {
                    configJson = configAsset.text;
                }
            }
            catch (Exception ex)
            {
                VpsSystemDiagnostics.Record("NAV", "Resources config load exception: " + ex.Message);
            }
            return string.IsNullOrWhiteSpace(configJson) ? FallbackEmbeddedConfigJson : configJson;
        }

        private static string ReadEmbeddedManifestJson()
        {
            string manifestJson = null;
            try
            {
                TextAsset manifestAsset = Resources.Load<TextAsset>("ActiveNavigationManifest");
                if (manifestAsset != null && !string.IsNullOrWhiteSpace(manifestAsset.text))
                {
                    manifestJson = manifestAsset.text;
                }
            }
            catch (Exception ex)
            {
                VpsSystemDiagnostics.Record("NAV", "Resources manifest load exception: " + ex.Message);
            }
            return string.IsNullOrWhiteSpace(manifestJson) ? FallbackEmbeddedManifestJson : manifestJson;
        }

        public void ProcessConfigJson(string json)
        {
            activeConfig = null;
            Manifest = null;
            ErrorMessage = null;
            PublicNavigationConfigV2 config;
            string error;
            if (!SiteGraphV2ContractParser.TryParseConfig(json, out config, out error))
            {
                Fail(error);
                return;
            }
            activeConfig = config;
            if (config.ActiveFloorId == null)
            {
                SetState(NavigationBootstrapState.NoActiveMap);
                return;
            }
            SetState(NavigationBootstrapState.FetchingManifest);
        }

        public bool ProcessManifestJson(string json)
        {
            if (activeConfig == null || activeConfig.ActiveFloorId == null || activeConfig.ActiveVersionId == null)
            {
                Fail("An active V2 config is required before loading a manifest.");
                return false;
            }
            PublishedSiteGraphManifest manifest;
            string error;
            bool pointerMismatch;
            if (!SiteGraphV2ContractParser.TryParseManifest(
                    json, activeConfig.ActiveFloorId, activeConfig.ActiveVersionId,
                    out manifest, out error, out pointerMismatch))
            {
                Fail(error);
                return pointerMismatch;
            }
            Manifest = manifest;
            SetState(NavigationBootstrapState.ManifestLoaded);
            return false;
        }

        private IEnumerator FetchFromApi(NavigationApiClient client)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                SetState(NavigationBootstrapState.FetchingConfig);
                NavigationApiResponse configResponse = null;
                yield return client.GetConfig(response => configResponse = response);
                if (configResponse == null || !configResponse.Success)
                {
                    Fail(configResponse == null ? "Navigation config request did not complete." : configResponse.ErrorMessage);
                    yield break;
                }
                ProcessConfigJson(configResponse.Body);
                if (State != NavigationBootstrapState.FetchingManifest) yield break;

                NavigationApiResponse manifestResponse = null;
                yield return client.GetManifest(activeConfig.ActiveFloorId, response => manifestResponse = response);
                if (manifestResponse == null)
                {
                    Fail("Navigation manifest request did not complete.");
                    yield break;
                }
                if (!manifestResponse.Success)
                {
                    if (manifestResponse.StatusCode == 404 && attempt == 0) continue;
                    Fail(manifestResponse.ErrorMessage);
                    yield break;
                }
                if (!ProcessManifestJson(manifestResponse.Body)) yield break;
            }
            Fail("The active V2 navigation config changed repeatedly while loading its manifest.");
        }

        private void Fail(string message)
        {
            activeConfig = null;
            Manifest = null;
            IsUsingLocalOverride = false;
            ErrorMessage = string.IsNullOrWhiteSpace(message) ? "V2 navigation data is invalid." : message;
            VpsSystemDiagnostics.Record("NAV", "bootstrap failed: " + ErrorMessage);
            SetState(NavigationBootstrapState.Error);
        }

        private void SetState(NavigationBootstrapState state)
        {
            State = state;
            VpsSystemDiagnostics.SetState("Navigation bootstrap", state.ToString());
            StateChanged?.Invoke(state);
        }

        private const string FallbackEmbeddedConfigJson = "{\"schemaVersion\":2,\"activeFloorId\":\"6ae860aca2f51d025151f60c\",\"activeVersionId\":\"6aec0890f6fec623b1161e00\",\"configRevision\":7}";

        private const string FallbackEmbeddedManifestJson = @"{
  ""schemaVersion"": 2,
  ""versionId"": ""6aec0890f6fec623b1161e00"",
  ""version"": 2,
  ""floorId"": ""6ae860aca2f51d025151f60c"",
  ""buildingId"": ""6ae85d5539948d1a21852a61"",
  ""siteBinding"": {
    ""siteId"": ""a49d0de5-70ef-4af7-9734-e5a400e65660"",
    ""productionAssetId"": ""adf4d9e5-cb02-41d3-bd4f-73425dc30a9e"",
    ""payloadSha256"": ""8678da52669aece5788d92ab0282c210a44611256bf4b36eca321921d14b6ca1""
  },
  ""sourceDraftRevision"": 2,
  ""siteMeshSha256"": ""b65cd3de05db81e05706d9769183ea568d7f8bf3a7709abc5a06a629ca8e1080"",
  ""graph"": {
    ""nodes"": [
      {
        ""id"": ""n-16ae7c623ce0f3568d"",
        ""kind"": ""junction"",
        ""anchorLocal"": { ""x"": -2.048655079567118, ""y"": -1.018922873290467, ""z"": 2.332226501670164 },
        ""provenance"": ""admin""
      },
      {
        ""id"": ""n-3538bb439e0bbeb85e"",
        ""kind"": ""waypoint"",
        ""anchorLocal"": { ""x"": -0.29226258397102356, ""y"": -1.030983328819275, ""z"": -0.37945300340652466 },
        ""provenance"": ""generated""
      },
      {
        ""id"": ""n-79193cbce8b1e56a52"",
        ""kind"": ""waypoint"",
        ""anchorLocal"": { ""x"": 1.5598070621490479, ""y"": -1.0010409355163574, ""z"": 4.996452808380127 },
        ""provenance"": ""generated""
      },
      {
        ""id"": ""n-a2df9d9b50c0bb184d"",
        ""kind"": ""waypoint"",
        ""anchorLocal"": { ""x"": -0.17462368309497833, ""y"": -1.0200504064559937, ""z"": -0.4434594511985779 },
        ""provenance"": ""generated""
      },
      {
        ""id"": ""n-a472f7ef4e3a6d6f09"",
        ""kind"": ""waypoint"",
        ""anchorLocal"": { ""x"": -3.75127911567688, ""y"": -1.0508068799972534, ""z"": 1.2051860094070435 },
        ""provenance"": ""generated""
      }
    ],
    ""edges"": [
      {
        ""id"": ""e-1afd4e82b3fec36cc4"",
        ""fromNodeId"": ""n-16ae7c623ce0f3568d"",
        ""toNodeId"": ""n-a472f7ef4e3a6d6f09"",
        ""bidirectional"": true,
        ""enabled"": true,
        ""costMultiplier"": 1.0,
        ""lengthM"": 2.0420983006268507,
        ""geometry"": [
          { ""x"": -2.048655079567118, ""y"": -1.018922873290467, ""z"": 2.332226501670164 },
          { ""x"": -3.75127911567688, ""y"": -1.0508068799972534, ""z"": 1.2051860094070435 }
        ],
        ""provenance"": ""admin""
      },
      {
        ""id"": ""e-761f26f626380d6f4a"",
        ""fromNodeId"": ""n-a2df9d9b50c0bb184d"",
        ""toNodeId"": ""n-16ae7c623ce0f3568d"",
        ""bidirectional"": true,
        ""enabled"": true,
        ""costMultiplier"": 1.0,
        ""lengthM"": 3.3490935273962137,
        ""geometry"": [
          { ""x"": -0.17462368309497833, ""y"": -1.0200504064559937, ""z"": -0.4434594511985779 },
          { ""x"": -2.048655079567118, ""y"": -1.018922873290467, ""z"": 2.332226501670164 }
        ],
        ""provenance"": ""admin""
      },
      {
        ""id"": ""e-9dd81760a409acfe0b"",
        ""fromNodeId"": ""n-3538bb439e0bbeb85e"",
        ""toNodeId"": ""n-a2df9d9b50c0bb184d"",
        ""bidirectional"": true,
        ""enabled"": true,
        ""costMultiplier"": 1.0,
        ""lengthM"": 0.13436988185483517,
        ""geometry"": [
          { ""x"": -0.29226258397102356, ""y"": -1.030983328819275, ""z"": -0.37945300340652466 },
          { ""x"": -0.17462368309497833, ""y"": -1.0200504064559937, ""z"": -0.4434594511985779 }
        ],
        ""provenance"": ""generated""
      },
      {
        ""id"": ""e-b15f77a560e036957d"",
        ""fromNodeId"": ""n-16ae7c623ce0f3568d"",
        ""toNodeId"": ""n-79193cbce8b1e56a52"",
        ""bidirectional"": true,
        ""enabled"": true,
        ""costMultiplier"": 1.0,
        ""lengthM"": 4.485467713100027,
        ""geometry"": [
          { ""x"": -2.048655079567118, ""y"": -1.018922873290467, ""z"": 2.332226501670164 },
          { ""x"": 1.5598070621490479, ""y"": -1.0010409355163574, ""z"": 4.996452808380127 }
        ],
        ""provenance"": ""admin""
      }
    ],
    ""pois"": [
      {
        ""id"": ""p-0efc38ad2a3586bfdf"",
        ""label"": ""đi"",
        ""category"": ""room"",
        ""isActive"": true,
        ""nodeId"": ""n-a2df9d9b50c0bb184d"",
        ""anchorLocal"": { ""x"": -0.17462368309497833, ""y"": -1.0200504064559937, ""z"": -0.4434594511985779 },
        ""provenance"": ""captured"",
        ""sourceMarkerId"": ""c3eb8f4f-37fc-42d6-a870-6daf77d8a784""
      },
      {
        ""id"": ""p-6bb08a26abc7b21e3c"",
        ""label"": ""đến"",
        ""category"": ""room"",
        ""isActive"": true,
        ""nodeId"": ""n-79193cbce8b1e56a52"",
        ""anchorLocal"": { ""x"": 1.5598070621490479, ""y"": -1.0010409355163574, ""z"": 4.996452808380127 },
        ""provenance"": ""captured"",
        ""sourceMarkerId"": ""e24b6eb9-a72b-4af4-9e0c-e1edadca6f2c""
      },
      {
        ""id"": ""p-c5632f5915852f2595"",
        ""label"": ""cửa"",
        ""category"": ""room"",
        ""isActive"": true,
        ""nodeId"": ""n-a472f7ef4e3a6d6f09"",
        ""anchorLocal"": { ""x"": -3.75127911567688, ""y"": -1.0508068799972534, ""z"": 1.2051860094070435 },
        ""provenance"": ""captured"",
        ""sourceMarkerId"": ""9509883d-3a34-4b24-92ac-e19287f41cc2""
      }
    ]
  },
  ""publishedAt"": ""2026-11-05T04:39:44.697Z""
}";
    }
}
