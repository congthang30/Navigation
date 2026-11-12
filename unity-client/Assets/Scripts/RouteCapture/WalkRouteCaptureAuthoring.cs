using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Sites;
using NianticSpatial.NSDK.AR.Subsystems;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.XRSubsystems;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Vps.UnityClient.Api;
using Vps.UnityClient.Diagnostics;
using Vps.UnityClient.Navigation;
using Vps.UnityClient.RouteCapture;
using Vps.UnityClient.Vps;

namespace Vps.UnityClient.Authoring
{
    /// <summary>Uses NSDK Sites and VPS2 to record segmented anchor-local routes and submit them for graph generation.</summary>
    public sealed class WalkRouteCaptureAuthoring : MonoBehaviour
    {
        private const string NsdkVersion = "4.1.0-26051913";
        private const double TrackedStableSeconds = 0.5d;
        private const double SampleIntervalSeconds = 0.2d;
        private const int MaximumCaptureSamples = 15000;
        private const float MaximumMeshDistanceMeters = 4f;
        private const float MaximumFloorHeightDeltaMeters = 0.75f;
        private const double CameraStartupTimeoutSeconds = 12d;
        private const double ArTrackingTimeoutSeconds = 30d;

        private readonly string[] poiCategories = { "room", "service", "entrance", "elevator", "stairs", "restroom", "other" };
        private readonly string[] poiCategoryLabels = { "Phòng", "Dịch vụ", "Lối vào", "Thang máy", "Cầu thang", "WC", "Khác" };
        private string floorId = "";
        private string pendingFloorId = "";
        private string status = "Chọn tổ chức, Site và Production VPS asset để bắt đầu.";
        private string poiLabel = "";
        private string poiCategory = "room";
        private string sessionToken = "";
        private string selectedPayload = "";
        private string payloadSha256 = "";
        private string pendingCapturePath = "";
        private string pendingCaptureJson = "";
        private bool panelOpen;
        private bool arHostReady;
        private bool sitesBusy;
        private bool floorsLoading;
        private bool floorsLoaded;
        private bool floorsFailed;
        private bool meshLoading;
        private bool meshRequestInFlight;
        private bool meshDownloadFailed;
        private bool meshReady;
        private bool meshPreviewUploaded;
        private bool sessionRequestBusy;
        private bool trackingRequestAccepted;
        private bool scanViewOpen;
        private bool locateStarting;
        private bool checkingArService;
        private bool arServiceReady;
        private Coroutine locateRoutine;
        private ARCameraManager arCameraManager;
        private ARCameraBackground arCameraBackground;
        private int cameraFrameCount;
        private double lastCameraFrameAt = -1d;
        private readonly List<Camera> suspendedCameras = new List<Camera>();
        private bool stableTracking;
        private bool capturing;
        private bool uploadBusy;
        private double trackedSince;
        private double lastSampleAt = -1d;
        private double noFloorPointSince = -1d;
        private int meshRequestGeneration;
        private int floorQueryGeneration;
        private int selectedOrganizationIndex = -1;
        private int selectedSiteIndex = -1;
        private int selectedAssetIndex = -1;
        private int selectedFloorIndex = -1;
        private int selectedPoiCategoryIndex;
        private bool organizationsLoaded;
        private bool sitesLoaded;
        private OrganizationInfo[] organizations = Array.Empty<OrganizationInfo>();
        private SiteInfo[] sites = Array.Empty<SiteInfo>();
        private AssetInfo[] productionAssets = Array.Empty<AssetInfo>();
        private RecordableFloorOption[] selectableFloors = Array.Empty<RecordableFloorOption>();
        private Vector2 scroll;
        private Camera arCamera;
        private ARSession arSession;
        private ARInputManager arInputManager;
        private XROrigin xrOrigin;
        private ARVps2Manager vps2Manager;
        private NsdkVpsLocalizationAdapter localizationAdapter;
        private LocationMeshManager meshManager;
        private SitesClientManager sitesClient;
        private NavigationApiClient apiClient;
        private NianticAccessTokenProvider nianticAccessTokenProvider;
        private Transform trackedAnchor;
        private GameObject siteMesh;
        private SiteMeshUploadDocument siteMeshPreview;
        private RouteCaptureBuilder captureBuilder;
        private WalkCaptureDocument previewCapture;
        private bool recorderUiStylesReady;
        private float recorderUiStyleScale = -1f;
        private float nextDiagnosticsSnapshotAt;
        private string lastDiagnosticsSnapshot = string.Empty;
        private Font recorderUiFont;
        private Texture2D recorderPanelTexture;
        private Texture2D recorderSurfaceTexture;
        private Texture2D recorderInputTexture;
        private Texture2D recorderPrimaryTexture;
        private Texture2D recorderPrimaryHoverTexture;
        private Texture2D recorderPrimaryActiveTexture;
        private Texture2D recorderSecondaryTexture;
        private Texture2D recorderSecondaryHoverTexture;
        private Texture2D recorderSecondaryActiveTexture;
        private Texture2D recorderOverlaySurfaceTexture;
        private Texture2D recorderOverlaySecondaryTexture;
        private Texture2D recorderOverlaySecondaryHoverTexture;
        private Texture2D recorderOverlaySecondaryActiveTexture;
        private Texture2D recorderOverlayInsetTexture;
        private Texture2D recorderTrackingTexture;
        private Texture2D recorderCircleTexture;
        private Texture2D recorderCameraPanelTexture;
        private Texture2D recorderCameraBadgeTexture;
        private Texture2D recorderDangerTexture;
        private Texture2D recorderDangerHoverTexture;
        private Texture2D recorderDangerActiveTexture;
        private Texture2D recorderMetricCardTexture;
        private Texture2D recorderCheckTexture;
        private GUIStyle recorderPanelStyle;
        private GUIStyle recorderTitleStyle;
        private GUIStyle recorderSectionStyle;
        private GUIStyle recorderBodyStyle;
        private GUIStyle recorderMutedStyle;
        private GUIStyle recorderMetaStyle;
        private GUIStyle recorderBadgeStyle;
        private GUIStyle recorderWaitingBadgeStyle;
        private GUIStyle recorderMetricStyle;
        private GUIStyle recorderSurfaceStyle;
        private GUIStyle recorderMapEmptyStyle;
        private GUIStyle recorderMetricValueStyle;
        private GUIStyle recorderPrimaryButtonStyle;
        private GUIStyle recorderSecondaryButtonStyle;
        private GUIStyle recorderInputStyle;
        private GUIStyle recorderMapCaptionStyle;
        private GUIStyle recorderPoiStyle;
        private GUIStyle recorderCategoryStyle;
        private GUIStyle recorderSelectionStyle;
        private GUIStyle recorderSelectionSelectedStyle;
        private GUIStyle recorderCameraPanelStyle;
        private GUIStyle recorderOverlaySurfaceStyle;
        private GUIStyle recorderOverlayMetricCardStyle;
        private GUIStyle recorderOverlayMetricStyle;
        private GUIStyle recorderOverlayMetricValueStyle;
        private GUIStyle recorderOverlayBodyStyle;
        private GUIStyle recorderOverlayTitleStyle;
        private GUIStyle recorderOverlayInputStyle;
        private GUIStyle recorderCameraTitleStyle;
        private GUIStyle recorderCameraMetaStyle;
        private GUIStyle recorderCameraBadgeStyle;
        private GUIStyle recorderCameraWaitingStyle;
        private GUIStyle recorderCameraDangerButtonStyle;
        private GUIStyle recorderOverlaySecondaryButtonStyle;
        private GUIStyle recorderMetricCardStyle;
        private DateTime sessionExpiryUtc;

        private const string PrefKeyAdminJwt = "vps.admin.jwt";
        private const string PrefKeyAdminEmail = "vps.admin.email";
        private const string PrefKeyAdminRole = "vps.admin.role";
        private const string PrefKeyAdminExpiresAt = "vps.admin.expiresAt";

        public bool IsPanelOpen => panelOpen;
        public string AdminEmail { get; private set; } = "";
        public string AdminRole { get; private set; } = "";
        public string AdminAccessToken => sessionToken;
        public event Action OnPanelClosed;
        public event Action<string> OnAdminSessionExpired;

        private void Awake()
        {
            apiClient = GetComponent<NavigationApiClient>();
            if (apiClient == null) apiClient = gameObject.AddComponent<NavigationApiClient>();
            nianticAccessTokenProvider = GetComponent<NianticAccessTokenProvider>();
            if (nianticAccessTokenProvider == null) nianticAccessTokenProvider = gameObject.AddComponent<NianticAccessTokenProvider>();
            nianticAccessTokenProvider.Initialize(apiClient);
            sitesClient = GetComponent<SitesClientManager>();
            if (sitesClient == null) sitesClient = gameObject.AddComponent<SitesClientManager>();
            sitesClient.RequestTimeoutSeconds = 20;
            LoadSavedAdminJwtSession();
            LoadNextPendingCapture();
        }

        public bool HasValidAdminJwtSession()
        {
            return !string.IsNullOrWhiteSpace(sessionToken) &&
                   sessionToken.Split('.').Length == 3 &&
                   DateTime.UtcNow < sessionExpiryUtc.AddMinutes(-1);
        }

        public void ApplyAdminJwtSession(string accessToken, string email, string role, DateTime expiresAtUtc)
        {
            sessionToken = accessToken == null ? "" : accessToken.Trim();
            AdminEmail = email == null ? "" : email.Trim();
            AdminRole = string.IsNullOrWhiteSpace(role) ? "admin" : role.Trim();
            sessionExpiryUtc = expiresAtUtc;

            PlayerPrefs.SetString(PrefKeyAdminJwt, sessionToken);
            PlayerPrefs.SetString(PrefKeyAdminEmail, AdminEmail);
            PlayerPrefs.SetString(PrefKeyAdminRole, AdminRole);
            PlayerPrefs.SetString(PrefKeyAdminExpiresAt, sessionExpiryUtc.ToString("O", CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        public void ClearAdminJwtSession()
        {
            sessionToken = "";
            AdminEmail = "";
            AdminRole = "";
            sessionExpiryUtc = DateTime.MinValue;

            PlayerPrefs.DeleteKey(PrefKeyAdminJwt);
            PlayerPrefs.DeleteKey(PrefKeyAdminEmail);
            PlayerPrefs.DeleteKey(PrefKeyAdminRole);
            PlayerPrefs.DeleteKey(PrefKeyAdminExpiresAt);
            PlayerPrefs.Save();
        }

        private void LoadSavedAdminJwtSession()
        {
            string savedJwt = PlayerPrefs.GetString(PrefKeyAdminJwt, "");
            string savedExpires = PlayerPrefs.GetString(PrefKeyAdminExpiresAt, "");
            if (string.IsNullOrWhiteSpace(savedJwt) ||
                !DateTime.TryParse(savedExpires, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsedExpiry) ||
                DateTime.UtcNow >= parsedExpiry.AddMinutes(-1))
            {
                ClearAdminJwtSession();
                return;
            }
            sessionToken = savedJwt;
            sessionExpiryUtc = parsedExpiry;
            AdminEmail = PlayerPrefs.GetString(PrefKeyAdminEmail, "");
            AdminRole = PlayerPrefs.GetString(PrefKeyAdminRole, "admin");
        }

        public void OpenRecorderPanel()
        {
            if (!HasValidAdminJwtSession())
            {
                OnAdminSessionExpired?.Invoke("Vui lòng đăng nhập tài khoản Admin để truy cập VPS Map.");
                return;
            }
            panelOpen = true;
            StartCoroutine(PrepareRecorderPanel());
        }

        private IEnumerator PrepareRecorderPanel()
        {
            sessionRequestBusy = true;
            sitesBusy = true;
            bool tokenReady = false;
            string tokenError = null;
            yield return nianticAccessTokenProvider.EnsureAccessToken(sessionToken, (success, error) =>
            {
                tokenReady = success;
                tokenError = error;
            });
            sessionRequestBusy = false;
            sitesBusy = false;
            if (!panelOpen)
            {
                nianticAccessTokenProvider.StopRefreshing();
                yield break;
            }
            if (!tokenReady)
            {
                status = string.IsNullOrWhiteSpace(tokenError)
                    ? "Không lấy được access token Niantic từ backend."
                    : tokenError;
                yield break;
            }

            status = "Chọn Site, VPS asset và tầng. Bấm định vị để mở camera quét không gian.";
        }

        public void LogoutAndClose()
        {
            string tokenToRevoke = sessionToken;
            ClearAdminJwtSession();
            if (capturing) FinishCapture("sessionEnded");
            if (scanViewOpen || trackingRequestAccepted) StopTracking();
            if (nianticAccessTokenProvider != null) nianticAccessTokenProvider.Logout();
            panelOpen = false;
            if (!string.IsNullOrWhiteSpace(tokenToRevoke) && apiClient != null)
            {
                StartCoroutine(apiClient.AdminLogoutJwt(tokenToRevoke, _ => { }));
            }
            OnAdminSessionExpired?.Invoke("Đã đăng xuất tài khoản Admin.");
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextDiagnosticsSnapshotAt)
            {
                nextDiagnosticsSnapshotAt = Time.unscaledTime + 1f;
                string snapshot = "panel=" + panelOpen + " arHost=" + arHostReady +
                    " scanView=" + scanViewOpen + " locateStarting=" + locateStarting +
                    " sitesBusy=" + sitesBusy + " trackingAccepted=" + trackingRequestAccepted +
                    " stableTracking=" + stableTracking + " meshLoading=" + meshLoading +
                    " meshReady=" + meshReady + " uploadBusy=" + uploadBusy +
                    " arSessionRunning=" + (arSession != null && arSession.subsystem != null && arSession.subsystem.running) +
                    " arNotTrackingReason=" + ARSession.notTrackingReason +
                    " inputSubsystemRunning=" + (arInputManager != null && arInputManager.subsystem != null && arInputManager.subsystem.running) +
                    " cameraEnabled=" + (arCamera != null && arCamera.isActiveAndEnabled) +
                    " cameraSubsystemRunning=" + (arCameraManager != null && arCameraManager.subsystem != null && arCameraManager.subsystem.running) +
                    " cameraFramesReceived=" + (cameraFrameCount > 0) +
                    " backgroundRendering=" + (arCameraBackground != null && arCameraBackground.backgroundRenderingEnabled);
                if (!string.Equals(snapshot, lastDiagnosticsSnapshot, StringComparison.Ordinal))
                {
                    lastDiagnosticsSnapshot = snapshot;
                    VpsSystemDiagnostics.SetState("Recorder", snapshot);
                }
                VpsSystemDiagnostics.SetState("Recorder auth", HasValidAdminJwtSession() ? "Admin session valid" : "Admin session missing/expired");
            }
            if (!panelOpen || !arHostReady) return;
            if (arInputManager != null && arInputManager.subsystem != null && !arInputManager.subsystem.running)
                arInputManager.subsystem.Start();
            if (!trackingRequestAccepted || localizationAdapter == null) return;

            bool tracked = HasCameraPreview && localizationAdapter.CurrentTrackingState == NormalizedVpsTrackingState.TRACKED;
            bool wasStable = stableTracking;
            stableTracking = tracked &&
                trackedSince > 0d && Time.realtimeSinceStartupAsDouble - trackedSince >= TrackedStableSeconds;
            if (tracked && trackedSince <= 0d) trackedSince = Time.realtimeSinceStartupAsDouble;
            if (!tracked)
            {
                trackedSince = 0d;
                stableTracking = false;
                if (meshLoading || meshRequestInFlight)
                {
                    meshRequestGeneration++;
                    meshLoading = false;
                    meshDownloadFailed = true;
                }
                if (capturing && captureBuilder != null && captureBuilder.CurrentSegment != null)
                {
                    captureBuilder.EndSegment("trackingLost", DateTime.UtcNow);
                    lastSampleAt = -1d;
                    status = "Tracking bị gián đoạn; capture đã tách đoạn. Đang chờ định vị lại để tiếp tục.";
                }
            }
            else if (!wasStable && stableTracking && capturing && captureBuilder != null && captureBuilder.CurrentSegment == null)
            {
                captureBuilder.BeginSegment(DateTime.UtcNow);
                lastSampleAt = -1d;
                noFloorPointSince = -1d;
                status = "Tracking ổn định trở lại; capture tiếp tục ở đoạn mới.";
            }

            if (stableTracking && !meshReady && !meshLoading && !meshRequestInFlight && !meshDownloadFailed)
                StartCoroutine(DownloadSiteMesh());
            if (capturing && stableTracking && captureBuilder != null && captureBuilder.CurrentSegment != null)
                CaptureSampleIfDue();
        }

        private void OnGUI()
        {
            if (!panelOpen) return;
            if (!HasValidAdminJwtSession())
            {
                CloseRecorderPanel();
                OnAdminSessionExpired?.Invoke("Phiên đăng nhập Admin đã hết hạn. Vui lòng đăng nhập lại.");
                return;
            }

            EnsureRecorderUiStyles();
            float scale = GetRecorderUiScale();

            if (scanViewOpen || trackingRequestAccepted)
            {
                DrawActiveRecorderOverlay(scale);
                return;
            }

            Rect safeArea = Screen.safeArea;
            if (safeArea.width < 1f || safeArea.height < 1f)
                safeArea = new Rect(0f, 0f, Screen.width, Screen.height);
            float safeTop = Screen.height - safeArea.yMax;
            Rect panelRect = new Rect(safeArea.x, safeTop, safeArea.width, safeArea.height);
            GUILayout.BeginArea(panelRect, recorderPanelStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Ghi tuyến", recorderTitleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Đăng xuất", recorderSecondaryButtonStyle, GUILayout.Width(96f * scale), GUILayout.Height(34f * scale)))
            {
                LogoutAndClose();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                return;
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(AdminEmail))
            {
                GUILayout.Label("Đăng nhập với " + AdminEmail, recorderMetaStyle);
            }

            if (trackingRequestAccepted)
            {
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                DrawActiveRecorderUi(scale);
                GUILayout.EndScrollView();
                GUILayout.Space(6f * scale);
                if (GUILayout.Button("Về trang chủ", recorderSecondaryButtonStyle, GUILayout.Height(42f * scale)))
                    CloseRecorderPanel();
            }
            else
            {
                GUILayout.Space(8f * scale);
                GUILayout.Label("Thiết lập bản đồ và chọn tầng cần ghi.", recorderBodyStyle);
                GUILayout.BeginVertical(recorderSurfaceStyle);
                GUILayout.Label(status, recorderMetaStyle, GUILayout.MinHeight(34f * scale));
                GUILayout.EndVertical();
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                GUILayout.Label("Camera sẽ mở khi bấm định vị sau khi chọn tầng.", recorderMetaStyle);

                DrawSiteSelection();
                DrawFloorSelection(scale);

                if (selectedAssetIndex >= 0 && selectedAssetIndex < productionAssets.Length)
                {
                    AssetInfo asset = productionAssets[selectedAssetIndex];
                    GUILayout.Label("VPS asset · " + asset.Name, recorderMetaStyle);
                    string disabledReason = GetLocateSiteSetupBlockReason();
                    bool canStart = disabledReason == null;
                    VpsSystemDiagnostics.SetState("Locate Site button", (canStart ? "enabled" : "disabled") + " reason=" + (disabledReason ?? "ready to open camera"));
                    GUI.enabled = canStart;
                    if (GUILayout.Button("Định vị Site và tải mesh" + (string.IsNullOrEmpty(floorId) ? "" : " · " + GetSelectedFloorLabel()), recorderPrimaryButtonStyle, GUILayout.Height(50f * scale)))
                    {
                        VpsSystemDiagnostics.Record("INPUT", "button callback: Locate Site and download mesh");
                        BeginTracking(asset);
                    }
                    GUI.enabled = true;
                    if (!canStart) GUILayout.Label(disabledReason, recorderMetaStyle);
                }

                if (pendingCaptureJson.Length > 0)
                {
                    GUILayout.Label("Capture đang chờ gửi. Gửi sẽ tạo graph draft để Admin review, không tự publish.", recorderMetaStyle);
                    GUILayout.Label("Floor đích: " + (string.IsNullOrEmpty(pendingFloorId) ? "chọn Floor phù hợp" : GetFloorLabel(pendingFloorId)), recorderMetaStyle);
                    GUI.enabled = !uploadBusy && !string.IsNullOrWhiteSpace(pendingFloorId) && floorId == pendingFloorId;
                    if (GUILayout.Button(uploadBusy ? "Đang gửi capture…" : "Gửi capture để tạo graph", recorderPrimaryButtonStyle, GUILayout.Height(50f * scale)))
                        StartCoroutine(UploadPendingCapture());
                    GUI.enabled = true;
                }
                GUILayout.EndScrollView();
                GUILayout.Space(6f * scale);
                if (GUILayout.Button("Về trang chủ", recorderSecondaryButtonStyle, GUILayout.Height(42f * scale)))
                    CloseRecorderPanel();
            }

            GUILayout.EndArea();
        }

        private string GetLocateSiteSetupBlockReason()
        {
            if (locateStarting || sitesBusy || sessionRequestBusy) return "Đang tải dữ liệu Site. Vui lòng chờ.";
            if (VpsWorkflowCoordinator.IsOwnedByOther(this)) return "Hãy dừng quy trình đang sử dụng camera AR trước.";
            if (floorsLoading) return "Đang tải danh sách tầng phù hợp với Site và VPS asset.";
            if (selectedFloorIndex < 0 || selectedFloorIndex >= selectableFloors.Length ||
                selectableFloors[selectedFloorIndex] == null || string.IsNullOrWhiteSpace(floorId) ||
                selectableFloors[selectedFloorIndex].id != floorId)
                return "Hãy chọn tầng đã liên kết với Site và VPS asset để bật nút định vị.";
            return null;
        }

        private bool HasCameraPreview => cameraFrameCount > 0 && lastCameraFrameAt >= 0d &&
            Time.realtimeSinceStartupAsDouble - lastCameraFrameAt < 2d &&
            arCamera != null && arCamera.isActiveAndEnabled &&
            arCameraBackground != null && arCameraBackground.backgroundRenderingEnabled;

        private string GetArReadinessMessage()
        {
            if (!HasCameraPreview) return "Đang mở camera và chờ hình ảnh trực tiếp…";
            if (ARSession.state == ARSessionState.Unsupported) return "Thiết bị hiện không hỗ trợ AR.";
            if (ARSession.state == ARSessionState.NeedsInstall || ARSession.state == ARSessionState.Installing)
                return "Cần cài đặt hoặc cập nhật dịch vụ AR theo hướng dẫn trên màn hình.";
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                switch (ARSession.notTrackingReason)
                {
                    case NotTrackingReason.InsufficientLight:
                        return "Camera chưa theo dõi được: thiếu sáng. Hãy hướng máy vào khu vực sáng hơn.";
                    case NotTrackingReason.InsufficientFeatures:
                        return "Camera chưa theo dõi được: ít chi tiết. Hãy hướng máy vào đồ vật hoặc bề mặt có hoa văn.";
                    case NotTrackingReason.ExcessiveMotion:
                        return "Máy di chuyển quá nhanh. Hãy di chuyển chậm lại để camera theo dõi.";
                    case NotTrackingReason.CameraUnavailable:
                        return "Camera hiện chưa khả dụng. Kiểm tra quyền camera và ứng dụng khác đang sử dụng camera.";
                    default:
                        return "Đang chờ AR theo dõi camera. Hướng máy vào khu vực đủ sáng, có chi tiết và di chuyển nhẹ.";
                }
            }
            return "Camera đã sẵn sàng. Đang chuẩn bị định vị Site…";
        }

        private void DrawActiveRecorderOverlay(float scale)
        {
            Rect safeArea = Screen.safeArea;
            if (safeArea.width < 1f || safeArea.height < 1f)
                safeArea = new Rect(0f, 0f, Screen.width, Screen.height);

            float safeTop = Screen.height - safeArea.yMax;
            float safeBottom = Screen.height - safeArea.yMin;
            float margin = 16f * scale;
            float width = Mathf.Max(260f * scale, Mathf.Min(safeArea.width - margin * 2f, 520f * scale));
            float left = safeArea.x + (safeArea.width - width) * 0.5f;
            Rect header = new Rect(left, safeTop + margin, width, 78f * scale);
            GUI.Box(header, GUIContent.none, recorderCameraPanelStyle);

            string stateLabel = !trackingRequestAccepted
                ? (locateStarting ? (HasCameraPreview ? "CHỜ AR" : "MỞ CAMERA") : "CẦN THỬ LẠI")
                : uploadBusy
                ? "ĐANG GỬI"
                : (!string.IsNullOrEmpty(pendingCaptureJson)
                    ? "HOÀN TẤT"
                    : (capturing
                        ? "ĐANG GHI"
                        : (!stableTracking
                            ? "ĐANG ĐỊNH VỊ"
                            : (meshReady ? (meshPreviewUploaded ? "SẴN SÀNG" : "ĐỒNG BỘ MESH") : "ĐANG TẢI MESH"))));
            GUIStyle badgeStyle = stableTracking ? recorderCameraBadgeStyle : recorderCameraWaitingStyle;
            Rect stateRect = new Rect(header.xMax - 122f * scale, header.y + 12f * scale, 108f * scale, 30f * scale);
            GUI.Box(stateRect, stateLabel, badgeStyle);

            GUI.Label(new Rect(header.x + 18f * scale, header.y + 8f * scale, header.width - 164f * scale, 30f * scale),
                "Ghi tuyến", recorderCameraTitleStyle);
            string floorLabel = string.IsNullOrWhiteSpace(GetSelectedFloorLabel()) ? ShortIdentifier(floorId) : GetSelectedFloorLabel();
            GUI.Label(new Rect(header.x + 18f * scale, header.y + 39f * scale, header.width - 36f * scale, 26f * scale),
                GetSelectedSiteName() + "  ·  " + floorLabel, recorderCameraMetaStyle);

            bool oldEnabled = GUI.enabled;
            if (!trackingRequestAccepted)
            {
                float dockHeight = locateStarting ? 156f * scale : 214f * scale;
                Rect dock = new Rect(left, safeBottom - dockHeight - margin, width, dockHeight);
                GUI.Box(dock, GUIContent.none, recorderOverlaySurfaceStyle);
                string guidance = locateStarting && arHostReady ? GetArReadinessMessage() : status;
                GUI.Label(new Rect(dock.x + 18f * scale, dock.y + 14f * scale, dock.width - 36f * scale, 86f * scale),
                    guidance, recorderOverlayBodyStyle);
                if (!locateStarting)
                {
                    Rect retryRect = new Rect(dock.x + 14f * scale, dock.y + 108f * scale, dock.width - 28f * scale, 48f * scale);
                    if (GUI.Button(retryRect, "Thử mở camera và định vị lại", recorderPrimaryButtonStyle) &&
                        selectedAssetIndex >= 0 && selectedAssetIndex < productionAssets.Length)
                        BeginTracking(productionAssets[selectedAssetIndex]);
                }
                Rect cancelRect = new Rect(dock.x + 14f * scale, dock.yMax - 44f * scale, dock.width - 28f * scale, 34f * scale);
                if (GUI.Button(cancelRect, "Quay lại chọn Site / tầng", recorderOverlaySecondaryButtonStyle)) StopTracking();
            }
            else if (capturing)
            {
                float dockHeight = 326f * scale;
                Rect dock = new Rect(left, safeBottom - dockHeight - margin, width, dockHeight);
                GUI.Box(dock, GUIContent.none, recorderOverlaySurfaceStyle);

                float halfWidth = (dock.width - 34f * scale) * 0.5f;
                DrawOverlayMetric(new Rect(dock.x + 14f * scale, dock.y + 10f * scale, halfWidth, 42f * scale),
                    "QUÃNG ĐƯỜNG", FormatDistance(GetCaptureDistance(GetPreviewCapture())), scale);
                DrawOverlayMetric(new Rect(dock.x + 20f * scale + halfWidth, dock.y + 10f * scale, halfWidth, 42f * scale),
                    "NODE / POI", GetCaptureJunctionCount(GetPreviewCapture()) + " / " + GetCapturePoiCount(GetPreviewCapture()), scale);

                Rect poiNameRect = new Rect(dock.x + 14f * scale, dock.y + 58f * scale, dock.width - 28f * scale, 42f * scale);
                poiLabel = GUI.TextField(poiNameRect, poiLabel, recorderOverlayInputStyle);
                if (string.IsNullOrWhiteSpace(poiLabel))
                    GUI.Label(new Rect(poiNameRect.x + 12f * scale, poiNameRect.y + 2f * scale, poiNameRect.width - 24f * scale, poiNameRect.height - 4f * scale),
                        "Tên điểm đến", recorderCameraMetaStyle);

                Rect categoriesRect = new Rect(dock.x + 14f * scale, dock.y + 108f * scale, dock.width - 28f * scale, 58f * scale);
                selectedPoiCategoryIndex = GUI.SelectionGrid(categoriesRect, selectedPoiCategoryIndex, poiCategoryLabels, 4, recorderCategoryStyle);
                poiCategory = poiCategories[Mathf.Clamp(selectedPoiCategoryIndex, 0, poiCategories.Length - 1)];

                float actionGap = 10f * scale;
                float actionWidth = (dock.width - 28f * scale - actionGap) * 0.5f;
                Rect markRect = new Rect(dock.x + 14f * scale, dock.y + 178f * scale, actionWidth, 58f * scale);
                Rect junctionRect = new Rect(markRect.xMax + actionGap, markRect.y, actionWidth, 58f * scale);
                Rect finishRect = new Rect(dock.x + 14f * scale, dock.y + 246f * scale, dock.width - 28f * scale, 54f * scale);
                GUI.enabled = stableTracking && captureBuilder != null && captureBuilder.CurrentSegment != null && !string.IsNullOrWhiteSpace(poiLabel);
                if (GUI.Button(markRect, "Đánh dấu POI", recorderPrimaryButtonStyle)) MarkPoi();
                GUI.enabled = stableTracking && captureBuilder != null && captureBuilder.CurrentSegment != null &&
                    captureBuilder.CurrentSegment.samples.Count > 0 && captureBuilder.TotalSampleCount < MaximumCaptureSamples;
                if (GUI.Button(junctionRect, "Đánh dấu node", recorderSecondaryButtonStyle)) MarkJunction();
                GUI.enabled = true;
                if (GUI.Button(finishRect, "Kết thúc ghi", recorderCameraDangerButtonStyle))
                    FinishCapture("operatorStopped");
            }
            else if (!string.IsNullOrEmpty(pendingCaptureJson))
            {
                float dockHeight = 188f * scale;
                Rect dock = new Rect(left, safeBottom - dockHeight - margin, width, dockHeight);
                GUI.Box(dock, GUIContent.none, recorderOverlaySurfaceStyle);
                GUI.DrawTexture(new Rect(dock.x + 18f * scale, dock.y + 18f * scale, 38f * scale, 38f * scale), recorderCheckTexture, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(dock.x + 66f * scale, dock.y + 14f * scale, dock.width - 82f * scale, 30f * scale),
                    "Đã hoàn tất ghi tuyến", recorderOverlayTitleStyle);
                GUI.Label(new Rect(dock.x + 18f * scale, dock.y + 58f * scale, dock.width - 36f * scale, 24f * scale),
                    GetCaptureJunctionCount(GetPreviewCapture()) + " node · " + GetCapturePoiCount(GetPreviewCapture()) + " POI · " + FormatDistance(GetCaptureDistance(GetPreviewCapture())), recorderCameraMetaStyle);
                Rect uploadRect = new Rect(dock.x + 14f * scale, dock.y + 92f * scale, dock.width - 28f * scale, 54f * scale);
                GUI.enabled = !uploadBusy && !string.IsNullOrWhiteSpace(pendingFloorId) && floorId == pendingFloorId;
                if (GUI.Button(uploadRect, uploadBusy ? "Đang gửi capture…" : "Gửi capture để tạo graph", recorderPrimaryButtonStyle))
                    StartCoroutine(UploadPendingCapture());
                GUI.enabled = true;
                Rect closeRect = new Rect(dock.x + 14f * scale, dock.y + 150f * scale, dock.width - 28f * scale, 32f * scale);
                if (GUI.Button(closeRect, "Dừng định vị", recorderOverlaySecondaryButtonStyle)) StopTracking();
            }
            else
            {
                bool meshNeedsSync = stableTracking && meshReady && !meshPreviewUploaded;
                bool hasSecondaryAction = meshNeedsSync || (meshDownloadFailed && stableTracking && !meshLoading && !meshRequestInFlight);
                float dockHeight = hasSecondaryAction ? 220f * scale : 166f * scale;
                Rect dock = new Rect(left, safeBottom - dockHeight - margin, width, dockHeight);
                GUI.Box(dock, GUIContent.none, recorderOverlaySurfaceStyle);
                string guidance = !HasCameraPreview ? "Đang chờ camera khôi phục hình ảnh trực tiếp…" : stableTracking
                    ? (meshReady ? (meshPreviewUploaded ? "Không gian đã sẵn sàng để ghi." : "Đồng bộ mesh để bật ghi tuyến.") : status)
                    : "Di chuyển camera chậm để nhận diện không gian.";
                GUI.Label(new Rect(dock.x + 18f * scale, dock.y + 12f * scale, dock.width - 36f * scale, 40f * scale), guidance, recorderOverlayBodyStyle);

                if (meshNeedsSync)
                {
                    Rect syncRect = new Rect(dock.x + 14f * scale, dock.y + 58f * scale, dock.width - 28f * scale, 48f * scale);
                    GUI.enabled = !uploadBusy;
                    if (GUI.Button(syncRect, uploadBusy ? "Đang đồng bộ mesh…" : "Đồng bộ mesh lên Admin", recorderOverlaySecondaryButtonStyle))
                        StartCoroutine(UploadSiteMeshPreview());
                    GUI.enabled = true;
                }
                else if (meshDownloadFailed && stableTracking && !meshLoading && !meshRequestInFlight)
                {
                    Rect retryRect = new Rect(dock.x + 14f * scale, dock.y + 58f * scale, dock.width - 28f * scale, 48f * scale);
                    if (GUI.Button(retryRect, "Thử tải mesh lại", recorderOverlaySecondaryButtonStyle))
                    {
                        meshDownloadFailed = false;
                        StartCoroutine(DownloadSiteMesh());
                    }
                }

                float primaryY = hasSecondaryAction ? dock.y + 112f * scale : dock.y + 58f * scale;
                Rect startRect = new Rect(dock.x + 14f * scale, primaryY, dock.width - 28f * scale, 54f * scale);
                GUI.enabled = stableTracking && meshReady && meshPreviewUploaded;
                if (GUI.Button(startRect, "Bắt đầu ghi tuyến", recorderPrimaryButtonStyle)) BeginCapture();
                GUI.enabled = true;

                Rect stopRect = new Rect(dock.x + 14f * scale, dock.yMax - 42f * scale, dock.width - 28f * scale, 34f * scale);
                if (GUI.Button(stopRect, "Dừng định vị", recorderOverlaySecondaryButtonStyle)) StopTracking();
            }

            GUI.enabled = oldEnabled;
        }

        private void DrawOverlayMetric(Rect rect, string label, string value, float scale)
        {
            GUI.Box(rect, GUIContent.none, recorderOverlayMetricCardStyle);
            GUI.Label(new Rect(rect.x + 8f * scale, rect.y + 3f * scale, rect.width - 16f * scale, 14f * scale), label, recorderOverlayMetricStyle);
            GUI.Label(new Rect(rect.x + 8f * scale, rect.y + 17f * scale, rect.width - 16f * scale, 22f * scale), value, recorderOverlayMetricValueStyle);
        }

        private void DrawActiveRecorderUi(float scale)
        {
            GUILayout.Space(8f * scale);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ghi tuyến", recorderTitleStyle);
            GUILayout.FlexibleSpace();
            GUIStyle badgeStyle = stableTracking ? recorderBadgeStyle : recorderWaitingBadgeStyle;
            GUILayout.Label(stableTracking ? "Đã định vị" : "Đang định vị", badgeStyle, GUILayout.MinWidth(92f * scale), GUILayout.Height(32f * scale));
            GUILayout.EndHorizontal();

            GUILayout.Space(8f * scale);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Site: " + GetSelectedSiteName(), recorderMetaStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Floor · " + ShortIdentifier(floorId), recorderMetaStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f * scale);
            Rect mapRect = GUILayoutUtility.GetRect(0f, 10000f, 236f * scale, 252f * scale, GUILayout.ExpandWidth(true));
            DrawRoutePreview(mapRect, GetPreviewCapture(), scale);

            GUILayout.Space(10f * scale);
            GUILayout.BeginHorizontal();
            DrawMetricCard("Quãng đường", FormatDistance(GetCaptureDistance(GetPreviewCapture())), scale);
            GUILayout.Space(8f * scale);
            DrawMetricCard("Node / POI", GetCaptureJunctionCount(GetPreviewCapture()) + " / " + GetCapturePoiCount(GetPreviewCapture()), scale);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f * scale);
            GUILayout.Label(status, recorderBodyStyle);

            if (meshDownloadFailed && stableTracking && !meshLoading && !meshRequestInFlight)
            {
                GUILayout.Space(6f * scale);
                if (GUILayout.Button("Thử tải mesh lại", recorderSecondaryButtonStyle, GUILayout.Height(44f * scale)))
                {
                    meshDownloadFailed = false;
                    StartCoroutine(DownloadSiteMesh());
                }
            }

            if (meshReady && !meshPreviewUploaded && !uploadBusy)
            {
                GUILayout.Space(6f * scale);
                if (GUILayout.Button("Đồng bộ mesh lên Admin", recorderSecondaryButtonStyle, GUILayout.Height(44f * scale)))
                    StartCoroutine(UploadSiteMeshPreview());
            }

            if (capturing)
            {
                GUILayout.Space(8f * scale);
                GUILayout.Label("Tên POI", recorderMetaStyle);
                poiLabel = GUILayout.TextField(poiLabel, recorderInputStyle, GUILayout.Height(46f * scale));
                selectedPoiCategoryIndex = GUILayout.SelectionGrid(selectedPoiCategoryIndex, poiCategoryLabels, 4, recorderCategoryStyle, GUILayout.Height(68f * scale));
                poiCategory = poiCategories[Mathf.Clamp(selectedPoiCategoryIndex, 0, poiCategories.Length - 1)];

                GUI.enabled = stableTracking && captureBuilder != null && captureBuilder.CurrentSegment != null && !string.IsNullOrWhiteSpace(poiLabel);
                if (GUILayout.Button("Đánh dấu POI", recorderPrimaryButtonStyle, GUILayout.Height(50f * scale))) MarkPoi();
                GUI.enabled = stableTracking && captureBuilder != null && captureBuilder.CurrentSegment != null &&
                    captureBuilder.CurrentSegment.samples.Count > 0 && captureBuilder.TotalSampleCount < MaximumCaptureSamples;
                if (GUILayout.Button("Đánh dấu node rẽ/giao", recorderSecondaryButtonStyle, GUILayout.Height(50f * scale))) MarkJunction();
                GUI.enabled = true;

                GUILayout.Space(6f * scale);
                if (GUILayout.Button("Kết thúc ghi tuyến", recorderSecondaryButtonStyle, GUILayout.Height(50f * scale)))
                    FinishCapture("operatorStopped");
            }
            else if (!string.IsNullOrEmpty(pendingCaptureJson))
            {
                GUILayout.Space(8f * scale);
                GUILayout.Label("Floor đích: " + (string.IsNullOrEmpty(pendingFloorId) ? "chọn Floor phù hợp" : GetFloorLabel(pendingFloorId)));
                GUI.enabled = !uploadBusy && !string.IsNullOrWhiteSpace(pendingFloorId) && floorId == pendingFloorId;
                if (GUILayout.Button(uploadBusy ? "Đang gửi capture…" : "Gửi capture để tạo graph", recorderPrimaryButtonStyle, GUILayout.Height(50f * scale)))
                    StartCoroutine(UploadPendingCapture());
                GUI.enabled = true;
            }
            else
            {
                GUILayout.Space(8f * scale);
                GUI.enabled = stableTracking && meshReady && meshPreviewUploaded;
                if (GUILayout.Button("Bắt đầu ghi tuyến", recorderPrimaryButtonStyle, GUILayout.Height(52f * scale))) BeginCapture();
                GUI.enabled = true;
            }

            GUILayout.Space(8f * scale);
            if (GUILayout.Button("Dừng định vị", recorderSecondaryButtonStyle, GUILayout.Height(44f * scale))) StopTracking();
        }

        private void DrawMetricCard(string label, string value, float scale)
        {
            GUILayout.BeginVertical(recorderSurfaceStyle, GUILayout.Height(70f * scale), GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            GUILayout.Label(label, recorderMetricStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label(value, recorderMetricValueStyle, GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private void DrawRoutePreview(Rect rect, WalkCaptureDocument capture, float scale)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(rect, recorderSurfaceTexture);
            string siteName = GetSelectedSiteName();
            GUI.Label(new Rect(rect.x + 12f * scale, rect.y + 8f * scale, rect.width * 0.68f, 22f * scale), siteName, recorderMapCaptionStyle);
            GUI.Label(new Rect(rect.xMax - 112f * scale, rect.y + 8f * scale, 100f * scale, 22f * scale), "Floor · " + ShortIdentifier(floorId), recorderMapCaptionStyle);

            Rect plot = new Rect(rect.x + 14f * scale, rect.y + 38f * scale, rect.width - 28f * scale, rect.height - 78f * scale);
            Color gridColor = new Color(0.28f, 0.36f, 0.45f, 0.24f);
            for (int line = 1; line <= 4; line++)
            {
                float x = Mathf.Lerp(plot.x, plot.xMax, line / 5f);
                float y = Mathf.Lerp(plot.y, plot.yMax, line / 5f);
                DrawUiLine(new Vector2(x, plot.y), new Vector2(x, plot.yMax), gridColor, Mathf.Max(1f, scale));
                DrawUiLine(new Vector2(plot.x, y), new Vector2(plot.xMax, y), gridColor, Mathf.Max(1f, scale));
            }

            bool hasPoints;
            float minX, maxX, minZ, maxZ;
            GetCaptureBounds(capture, out hasPoints, out minX, out maxX, out minZ, out maxZ);
            if (hasPoints)
            {
                if (capture.segments != null)
                    for (int i = 0; i < capture.segments.Count; i++)
                        DrawRouteSegment(capture.segments[i], plot, minX, maxX, minZ, maxZ, scale);
                if (captureBuilder != null && captureBuilder.Capture == capture)
                    DrawRouteSegment(captureBuilder.CurrentSegment, plot, minX, maxX, minZ, maxZ, scale);

                DrawPoiMarkers(capture, plot, minX, maxX, minZ, maxZ, scale);
                DrawJunctionMarkers(capture, plot, minX, maxX, minZ, maxZ, scale);
                RouteSampleDocument first = GetFirstRouteSample(capture);
                RouteSampleDocument last = GetLastRouteSample(capture);
                if (first != null)
                {
                    Vector2 startPoint = MapRoutePoint(first, plot, minX, maxX, minZ, maxZ);
                    DrawRouteMarker(startPoint, NavigationUiPalette.BrandBlue, 16f * scale);
                    GUI.Label(new Rect(startPoint.x + 8f * scale, startPoint.y - 10f * scale, 68f * scale, 20f * scale), "Bắt đầu", recorderMapCaptionStyle);
                }
                if (last != null)
                {
                    Vector2 currentPoint = MapRoutePoint(last, plot, minX, maxX, minZ, maxZ);
                    DrawRouteMarker(currentPoint, NavigationUiPalette.Success, 15f * scale);
                    GUI.Label(new Rect(currentPoint.x + 8f * scale, currentPoint.y - 22f * scale, 82f * scale, 20f * scale), capturing ? "Hiện tại" : "Kết thúc", recorderMapCaptionStyle);
                }
            }
            else
            {
                GUI.Label(plot, stableTracking ? "Bắt đầu ghi để xem quỹ đạo thực tế" : "Đang chờ VPS2 Tracking ổn định…", recorderMapEmptyStyle);
            }

            bool isRecording = capturing;
            string footer = isRecording ? (stableTracking ? "Đang ghi quỹ đạo" : "Tạm dừng · mất tracking") :
                (!string.IsNullOrEmpty(pendingCaptureJson) ? "Capture đã lưu · chờ gửi" : "Quỹ đạo anchor-local");
            GUI.Label(new Rect(rect.x + 12f * scale, rect.yMax - 31f * scale, rect.width - 80f * scale, 22f * scale), footer, recorderMapCaptionStyle);
            if (isRecording)
            {
                GUI.color = NavigationUiPalette.DangerHover;
                GUI.DrawTexture(new Rect(rect.xMax - 61f * scale, rect.yMax - 25f * scale, 9f * scale, 9f * scale), recorderCircleTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(rect.xMax - 48f * scale, rect.yMax - 31f * scale, 42f * scale, 22f * scale), "REC", recorderMapCaptionStyle);
            }
        }

        private void EnsureRecorderUiStyles()
        {
            float styleScale = GetRecorderUiScale();
            if (recorderUiStylesReady && Mathf.Approximately(recorderUiStyleScale, styleScale)) return;
            if (recorderUiStylesReady) ReleaseRecorderUiTextures();
            recorderUiStylesReady = false;

            Color ink = NavigationUiPalette.TextOnLight;
            Color muted = NavigationUiPalette.MutedOnLight;
            Color white = NavigationUiPalette.TextOnDark;
            recorderUiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (recorderUiFont == null) recorderUiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            recorderPanelTexture = CreateRoundedTexture(NavigationUiPalette.LightSurface, 9, NavigationUiPalette.LightBorder, 0.7f);
            recorderSurfaceTexture = CreateRoundedTexture(NavigationUiPalette.LightSurface, 8, NavigationUiPalette.LightBorder, 0.7f);
            recorderInputTexture = CreateRoundedTexture(Color.white, 6, NavigationUiPalette.LightBorder, 0.7f);
            recorderPrimaryTexture = CreateRoundedTexture(NavigationUiPalette.BrandBlue, 11, NavigationUiPalette.BrandBlueBorder, 0.7f);
            recorderPrimaryHoverTexture = CreateRoundedTexture(NavigationUiPalette.BrandBlueHover, 11, NavigationUiPalette.BrandBlueBorder, 0.7f);
            recorderPrimaryActiveTexture = CreateRoundedTexture(NavigationUiPalette.BrandBluePressed, 11, NavigationUiPalette.BrandBlueBorder, 0.7f);
            recorderSecondaryTexture = CreateRoundedTexture(NavigationUiPalette.LightSurface, 8, NavigationUiPalette.LightBorder, 0.7f);
            recorderSecondaryHoverTexture = CreateRoundedTexture(NavigationUiPalette.LightSurfaceHover, 8, NavigationUiPalette.BrandBlueBorder, 0.7f);
            recorderSecondaryActiveTexture = CreateRoundedTexture(NavigationUiPalette.LightSurfacePressed, 8, NavigationUiPalette.BrandBlueBorder, 0.7f);
            recorderOverlaySurfaceTexture = CreateRoundedTexture(NavigationUiPalette.DarkPill, 14, NavigationUiPalette.DarkBorder, 0.7f);
            recorderOverlaySecondaryTexture = CreateRoundedTexture(NavigationUiPalette.DarkSecondary, 8, NavigationUiPalette.DarkBorder, 0.7f);
            recorderOverlaySecondaryHoverTexture = CreateRoundedTexture(NavigationUiPalette.DarkSecondaryHover, 8, NavigationUiPalette.DarkBorder, 0.7f);
            recorderOverlaySecondaryActiveTexture = CreateRoundedTexture(NavigationUiPalette.DarkSecondaryPressed, 8, NavigationUiPalette.DarkBorder, 0.7f);
            recorderOverlayInsetTexture = CreateRoundedTexture(NavigationUiPalette.DarkInset, 7, NavigationUiPalette.DarkBorder, 0.5f);
            recorderTrackingTexture = CreateRoundedTexture(NavigationUiPalette.SuccessSurfaceLight, 8, NavigationUiPalette.SuccessTextLight, 0.7f);
            recorderCameraPanelTexture = CreateRoundedTexture(NavigationUiPalette.DarkGlass, 10, NavigationUiPalette.DarkBorder, 0.7f);
            recorderCameraBadgeTexture = CreateRoundedTexture(NavigationUiPalette.SuccessSurfaceDark, 8, NavigationUiPalette.SuccessTextDark, 0.7f);
            recorderDangerTexture = CreateRoundedTexture(NavigationUiPalette.Danger, 10, NavigationUiPalette.DangerBorder, 0.7f);
            recorderDangerHoverTexture = CreateRoundedTexture(NavigationUiPalette.DangerHover, 10, NavigationUiPalette.DangerBorder, 0.7f);
            recorderDangerActiveTexture = CreateRoundedTexture(NavigationUiPalette.DangerPressed, 10, NavigationUiPalette.DangerPressedBorder, 0.7f);
            recorderMetricCardTexture = CreateRoundedTexture(NavigationUiPalette.LightSurfaceMuted, 7, NavigationUiPalette.LightBorder, 0.5f);
            recorderCircleTexture = CreateCircleTexture();
            recorderCheckTexture = CreateCheckTexture(NavigationUiPalette.Success);

            recorderPanelStyle = new GUIStyle(GUI.skin.box);
            SetStyleBackground(recorderPanelStyle, recorderPanelTexture);
            recorderPanelStyle.padding = new RectOffset(ScaleUiPixels(20f, styleScale), ScaleUiPixels(20f, styleScale), ScaleUiPixels(18f, styleScale), ScaleUiPixels(18f, styleScale));
            recorderPanelStyle.border = new RectOffset(9, 9, 9, 9);

            recorderTitleStyle = CreateRecorderTextStyle(20, FontStyle.Bold, ink, TextAnchor.MiddleLeft);
            recorderSectionStyle = CreateRecorderTextStyle(15, FontStyle.Bold, ink, TextAnchor.MiddleLeft);
            recorderBodyStyle = CreateRecorderTextStyle(14, FontStyle.Normal, NavigationUiPalette.BodyOnLight, TextAnchor.MiddleLeft);
            recorderMutedStyle = CreateRecorderTextStyle(12, FontStyle.Normal, muted, TextAnchor.MiddleCenter);
            recorderMetaStyle = CreateRecorderTextStyle(13, FontStyle.Normal, muted, TextAnchor.MiddleLeft);
            recorderBadgeStyle = CreateRecorderTextStyle(13, FontStyle.Bold, NavigationUiPalette.SuccessTextLight, TextAnchor.MiddleCenter);
            SetStyleBackground(recorderBadgeStyle, recorderTrackingTexture);
            recorderBadgeStyle.padding = new RectOffset(10, 10, 4, 4);
            recorderBadgeStyle.border = new RectOffset(8, 8, 8, 8);
            recorderWaitingBadgeStyle = CreateRecorderTextStyle(13, FontStyle.Bold, new Color(0.58f, 0.37f, 0.05f, 1f), TextAnchor.MiddleCenter);
            SetStyleBackground(recorderWaitingBadgeStyle, recorderSecondaryTexture);
            recorderWaitingBadgeStyle.padding = new RectOffset(10, 10, 4, 4);
            recorderWaitingBadgeStyle.border = new RectOffset(8, 8, 8, 8);
            recorderMetricStyle = CreateRecorderTextStyle(10, FontStyle.Bold, muted, TextAnchor.MiddleCenter);
            recorderSurfaceStyle = new GUIStyle(GUI.skin.box);
            SetStyleBackground(recorderSurfaceStyle, recorderSurfaceTexture);
            recorderSurfaceStyle.padding = new RectOffset(ScaleUiPixels(14f, styleScale), ScaleUiPixels(14f, styleScale), ScaleUiPixels(12f, styleScale), ScaleUiPixels(12f, styleScale));
            recorderSurfaceStyle.border = new RectOffset(8, 8, 8, 8);
            recorderMetricCardStyle = new GUIStyle(GUI.skin.box);
            SetStyleBackground(recorderMetricCardStyle, recorderMetricCardTexture);
            recorderMetricCardStyle.border = new RectOffset(7, 7, 7, 7);
            recorderMetricValueStyle = CreateRecorderTextStyle(16, FontStyle.Bold, ink, TextAnchor.MiddleCenter);
            recorderPrimaryButtonStyle = CreateRecorderButtonStyle(
                recorderPrimaryTexture, white, recorderPrimaryHoverTexture, recorderPrimaryActiveTexture, 11);
            recorderSecondaryButtonStyle = CreateRecorderButtonStyle(
                recorderSecondaryTexture, ink, recorderSecondaryHoverTexture, recorderSecondaryActiveTexture, 8);
            recorderOverlaySecondaryButtonStyle = CreateRecorderButtonStyle(
                recorderOverlaySecondaryTexture, white, recorderOverlaySecondaryHoverTexture, recorderOverlaySecondaryActiveTexture, 8);
            recorderCameraDangerButtonStyle = CreateRecorderButtonStyle(
                recorderDangerTexture, white, recorderDangerHoverTexture, recorderDangerActiveTexture, 10);
            recorderInputStyle = new GUIStyle(GUI.skin.textField);
            SetStyleBackground(recorderInputStyle, recorderInputTexture);
            recorderInputStyle.normal.textColor = ink;
            recorderInputStyle.focused.textColor = ink;
            recorderInputStyle.fontSize = Mathf.RoundToInt(15f * GetRecorderUiScale());
            recorderInputStyle.padding = new RectOffset(ScaleUiPixels(12f, styleScale), ScaleUiPixels(12f, styleScale), ScaleUiPixels(8f, styleScale), ScaleUiPixels(8f, styleScale));
            recorderMapCaptionStyle = CreateRecorderTextStyle(12, FontStyle.Normal, muted, TextAnchor.MiddleLeft);
            recorderMapEmptyStyle = CreateRecorderTextStyle(14, FontStyle.Normal, muted, TextAnchor.MiddleCenter);
            recorderPoiStyle = CreateRecorderTextStyle(11, FontStyle.Bold, ink, TextAnchor.MiddleLeft);

            recorderOverlaySurfaceStyle = new GUIStyle(GUI.skin.box);
            SetStyleBackground(recorderOverlaySurfaceStyle, recorderOverlaySurfaceTexture);
            recorderOverlaySurfaceStyle.padding = new RectOffset(18, 18, 14, 14);
            recorderOverlaySurfaceStyle.border = new RectOffset(14, 14, 14, 14);
            recorderOverlayMetricCardStyle = new GUIStyle(GUI.skin.box);
            SetStyleBackground(recorderOverlayMetricCardStyle, recorderOverlayInsetTexture);
            recorderOverlayMetricCardStyle.border = new RectOffset(7, 7, 7, 7);
            recorderOverlayMetricStyle = CreateRecorderTextStyle(
                10, FontStyle.Bold, NavigationUiPalette.MutedOnDark, TextAnchor.MiddleCenter);
            recorderOverlayMetricValueStyle = CreateRecorderTextStyle(
                16, FontStyle.Bold, NavigationUiPalette.TextOnDark, TextAnchor.MiddleCenter);
            recorderOverlayBodyStyle = CreateRecorderTextStyle(
                14, FontStyle.Normal, NavigationUiPalette.TextOnDark, TextAnchor.MiddleLeft);
            recorderOverlayTitleStyle = CreateRecorderTextStyle(
                20, FontStyle.Bold, NavigationUiPalette.TextOnDark, TextAnchor.MiddleLeft);
            recorderOverlayInputStyle = new GUIStyle(GUI.skin.textField);
            SetStyleBackground(recorderOverlayInputStyle, recorderOverlayInsetTexture);
            recorderOverlayInputStyle.font = recorderUiFont;
            recorderOverlayInputStyle.fontSize = Mathf.RoundToInt(15f * GetRecorderUiScale());
            recorderOverlayInputStyle.normal.textColor = NavigationUiPalette.TextOnDark;
            recorderOverlayInputStyle.hover.textColor = NavigationUiPalette.TextOnDark;
            recorderOverlayInputStyle.focused.textColor = NavigationUiPalette.TextOnDark;
            recorderOverlayInputStyle.active.textColor = NavigationUiPalette.TextOnDark;
            recorderOverlayInputStyle.padding = new RectOffset(ScaleUiPixels(12f, styleScale), ScaleUiPixels(12f, styleScale), ScaleUiPixels(8f, styleScale), ScaleUiPixels(8f, styleScale));

            recorderCameraPanelStyle = new GUIStyle(GUI.skin.box);
            SetStyleBackground(recorderCameraPanelStyle, recorderCameraPanelTexture);
            recorderCameraPanelStyle.padding = new RectOffset(14, 14, 10, 10);
            recorderCameraPanelStyle.border = new RectOffset(10, 10, 10, 10);
            recorderCameraTitleStyle = CreateRecorderTextStyle(20, FontStyle.Bold, white, TextAnchor.MiddleLeft);
            recorderCameraMetaStyle = CreateRecorderTextStyle(12, FontStyle.Normal, NavigationUiPalette.MutedOnDark, TextAnchor.MiddleLeft);
            recorderCameraBadgeStyle = CreateRecorderTextStyle(11, FontStyle.Bold, NavigationUiPalette.SuccessTextDark, TextAnchor.MiddleCenter);
            SetStyleBackground(recorderCameraBadgeStyle, recorderCameraBadgeTexture);
            recorderCameraBadgeStyle.border = new RectOffset(8, 8, 8, 8);
            recorderCameraBadgeStyle.padding = new RectOffset(8, 8, 4, 4);
            recorderCameraWaitingStyle = CreateRecorderTextStyle(11, FontStyle.Bold, NavigationUiPalette.WarningText, TextAnchor.MiddleCenter);
            SetStyleBackground(recorderCameraWaitingStyle, recorderCameraPanelTexture);
            recorderCameraWaitingStyle.border = new RectOffset(8, 8, 8, 8);
            recorderCameraWaitingStyle.padding = new RectOffset(8, 8, 4, 4);

            recorderCategoryStyle = new GUIStyle(GUI.skin.button);
            SetStyleBackground(recorderCategoryStyle, recorderOverlaySecondaryTexture);
            recorderCategoryStyle.hover.background = recorderOverlaySecondaryHoverTexture;
            recorderCategoryStyle.active.background = recorderOverlaySecondaryActiveTexture;
            recorderCategoryStyle.onNormal.background = recorderPrimaryTexture;
            recorderCategoryStyle.onHover.background = recorderPrimaryHoverTexture;
            recorderCategoryStyle.onActive.background = recorderPrimaryActiveTexture;
            recorderCategoryStyle.onFocused.background = recorderPrimaryTexture;
            recorderCategoryStyle.normal.textColor = white;
            recorderCategoryStyle.hover.textColor = white;
            recorderCategoryStyle.active.textColor = white;
            recorderCategoryStyle.focused.textColor = white;
            recorderCategoryStyle.onNormal.textColor = white;
            recorderCategoryStyle.onHover.textColor = white;
            recorderCategoryStyle.onActive.textColor = white;
            recorderCategoryStyle.onFocused.textColor = white;
            recorderCategoryStyle.fontSize = Mathf.RoundToInt(10f * GetRecorderUiScale());
            recorderCategoryStyle.fontStyle = FontStyle.Bold;
            recorderCategoryStyle.alignment = TextAnchor.MiddleCenter;
            recorderCategoryStyle.padding = new RectOffset(ScaleUiPixels(6f, styleScale), ScaleUiPixels(6f, styleScale), ScaleUiPixels(3f, styleScale), ScaleUiPixels(3f, styleScale));
            recorderCategoryStyle.border = new RectOffset(8, 8, 8, 8);

            recorderSelectionStyle = CreateRecorderOptionStyle(false, styleScale);
            recorderSelectionSelectedStyle = CreateRecorderOptionStyle(true, styleScale);
            recorderUiStyleScale = styleScale;
            recorderUiStylesReady = true;
        }

        private GUIStyle CreateRecorderOptionStyle(bool selected, float scale)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                font = recorderUiFont,
                fontSize = Mathf.RoundToInt(15f * scale),
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(
                    ScaleUiPixels(14f, scale),
                    ScaleUiPixels(12f, scale),
                    ScaleUiPixels(5f, scale),
                    ScaleUiPixels(5f, scale)),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(8, 8, 8, 8)
            };

            if (selected)
            {
                SetStyleBackground(style, recorderPrimaryTexture);
                style.hover.background = recorderPrimaryHoverTexture;
                style.active.background = recorderPrimaryActiveTexture;
                style.focused.background = recorderPrimaryTexture;
                SetRecorderOptionTextColor(style, NavigationUiPalette.TextOnDark);
            }
            else
            {
                SetStyleBackground(style, recorderSurfaceTexture);
                style.hover.background = recorderSecondaryHoverTexture;
                style.active.background = recorderSecondaryActiveTexture;
                style.focused.background = recorderSecondaryHoverTexture;
                SetRecorderOptionTextColor(style, NavigationUiPalette.TextOnLight);
            }

            return style;
        }

        private static void SetRecorderOptionTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private static int ScaleUiPixels(float pixels, float scale)
        {
            return Mathf.Max(0, Mathf.RoundToInt(pixels * scale));
        }

        private int DrawRecorderOptionList(string[] labels, int selectedIndex, float scale)
        {
            if (labels == null || labels.Length == 0) return -1;

            int nextIndex = selectedIndex;
            float rowHeight = 46f * scale;
            float rowGap = 5f * scale;
            for (int index = 0; index < labels.Length; index++)
            {
                GUIStyle rowStyle = index == selectedIndex ? recorderSelectionSelectedStyle : recorderSelectionStyle;
                string label = string.IsNullOrWhiteSpace(labels[index]) ? "Chưa đặt tên" : labels[index];
                if (GUILayout.Button(label, rowStyle, GUILayout.MinHeight(rowHeight), GUILayout.ExpandWidth(true)))
                    nextIndex = index;
                if (index < labels.Length - 1) GUILayout.Space(rowGap);
            }

            return nextIndex;
        }

        private float GetRecorderUiScale()
        {
            float widthScale = Screen.width / 390f;
            float heightScale = Screen.height / 740f;
            return Mathf.Clamp(Mathf.Min(widthScale, heightScale), 0.85f, 2f);
        }

        private GUIStyle CreateRecorderTextStyle(int fontSize, FontStyle fontStyle, Color textColor, TextAnchor alignment)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.font = recorderUiFont;
            style.fontSize = Mathf.RoundToInt(fontSize * GetRecorderUiScale());
            style.fontStyle = fontStyle;
            style.alignment = alignment;
            style.wordWrap = true;
            style.normal.textColor = textColor;
            return style;
        }

        private GUIStyle CreateRecorderButtonStyle(
            Texture2D background,
            Color textColor,
            Texture2D hoverBackground = null,
            Texture2D activeBackground = null,
            int borderPixels = 20)
        {
            float scale = GetRecorderUiScale();
            GUIStyle style = new GUIStyle(GUI.skin.button);
            SetStyleBackground(style, background);
            style.font = recorderUiFont;
            style.hover.background = hoverBackground != null ? hoverBackground : background;
            style.active.background = activeBackground != null ? activeBackground : background;
            style.focused.background = background;
            style.fontSize = Mathf.RoundToInt(18f * GetRecorderUiScale());
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            style.wordWrap = true;
            style.clipping = TextClipping.Clip;
            style.padding = new RectOffset(
                ScaleUiPixels(12f, scale),
                ScaleUiPixels(12f, scale),
                ScaleUiPixels(6f, scale),
                ScaleUiPixels(6f, scale));
            style.border = new RectOffset(borderPixels, borderPixels, borderPixels, borderPixels);
            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = textColor;
            style.focused.textColor = textColor;
            style.onNormal.textColor = textColor;
            style.onHover.textColor = textColor;
            style.onActive.textColor = textColor;
            style.onFocused.textColor = textColor;
            return style;
        }

        private static void SetStyleBackground(GUIStyle style, Texture2D background)
        {
            style.normal.background = background;
            style.hover.background = background;
            style.active.background = background;
            style.focused.background = background;
            style.onNormal.background = background;
            style.onHover.background = background;
            style.onActive.background = background;
            style.onFocused.background = background;
        }

        private static Texture2D CreateRoundedTexture(Color color, int radius, Color borderColor, float borderWidth)
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nearestX = Mathf.Clamp(x, radius, size - 1 - radius);
                    float nearestY = Mathf.Clamp(y, radius, size - 1 - radius);
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(nearestX, nearestY));
                    bool inside = distance <= radius;
                    if (!inside)
                        pixels[y * size + x] = new Color(color.r, color.g, color.b, 0f);
                    else
                        pixels[y * size + x] = borderWidth > 0f && distance >= radius - borderWidth
                            ? borderColor
                            : color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateCircleTexture()
        {
            const int size = 24;
            float radius = size * 0.5f - 1f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) <= radius ? Color.white : Color.clear;
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateCheckTexture(Color color)
        {
            const int size = 64;
            const float stroke = 5f;
            Vector2 first = new Vector2(13f, 33f);
            Vector2 middle = new Vector2(27f, 47f);
            Vector2 last = new Vector2(52f, 18f);
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float distance = Mathf.Min(DistanceToSegment(point, first, middle), DistanceToSegment(point, middle, last));
                    float alpha = Mathf.Clamp01(stroke - distance + 0.5f);
                    Color pixel = color;
                    pixel.a *= alpha;
                    pixels[y * size + x] = pixel;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon) return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        private void DrawUiLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude < 0.01f) return;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.color = color;
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, delta.magnitude, thickness), Texture2D.whiteTexture);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void DrawRouteMarker(Vector2 point, Color color, float diameter)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(point.x - diameter * 0.5f, point.y - diameter * 0.5f, diameter, diameter), recorderCircleTexture);
            GUI.color = oldColor;
        }

        private WalkCaptureDocument GetPreviewCapture()
        {
            return captureBuilder != null ? captureBuilder.Capture : previewCapture;
        }

        private string GetSelectedSiteName()
        {
            return selectedSiteIndex >= 0 && selectedSiteIndex < sites.Length && !string.IsNullOrWhiteSpace(sites[selectedSiteIndex].Name)
                ? sites[selectedSiteIndex].Name
                : "Site";
        }

        private static string ShortIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "—";
            string trimmed = value.Trim();
            return trimmed.Length <= 10 ? trimmed : trimmed.Substring(0, 8) + "…";
        }

        private static string FormatDistance(float meters)
        {
            return meters.ToString("0.0", CultureInfo.InvariantCulture) + " m";
        }

        private static int GetCapturePoiCount(WalkCaptureDocument capture)
        {
            return capture == null || capture.poiMarkers == null ? 0 : capture.poiMarkers.Count;
        }

        private static int GetCaptureJunctionCount(WalkCaptureDocument capture)
        {
            return capture == null || capture.junctionMarkers == null ? 0 : capture.junctionMarkers.Count;
        }

        private float GetCaptureDistance(WalkCaptureDocument capture)
        {
            if (capture == null) return 0f;
            float distance = 0f;
            if (capture.segments != null)
                for (int i = 0; i < capture.segments.Count; i++) distance += GetSegmentDistance(capture.segments[i]);
            if (captureBuilder != null && captureBuilder.Capture == capture)
                distance += GetSegmentDistance(captureBuilder.CurrentSegment);
            return distance;
        }

        private static float GetSegmentDistance(TrackingSegmentDocument segment)
        {
            if (segment == null || segment.samples == null || segment.samples.Count < 2) return 0f;
            float result = 0f;
            for (int i = 1; i < segment.samples.Count; i++)
            {
                AnchorLocalPointDocument a = segment.samples[i - 1].anchorLocal;
                AnchorLocalPointDocument b = segment.samples[i].anchorLocal;
                result += Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            }
            return result;
        }

        private void GetCaptureBounds(WalkCaptureDocument capture, out bool hasPoints, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            hasPoints = false;
            minX = maxX = minZ = maxZ = 0f;
            if (capture == null) return;
            if (capture.segments != null)
                for (int i = 0; i < capture.segments.Count; i++) IncludeSegmentBounds(capture.segments[i], ref hasPoints, ref minX, ref maxX, ref minZ, ref maxZ);
            if (captureBuilder != null && captureBuilder.Capture == capture)
                IncludeSegmentBounds(captureBuilder.CurrentSegment, ref hasPoints, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        private static void IncludeSegmentBounds(TrackingSegmentDocument segment, ref bool hasPoints, ref float minX, ref float maxX, ref float minZ, ref float maxZ)
        {
            if (segment == null || segment.samples == null) return;
            for (int i = 0; i < segment.samples.Count; i++)
            {
                AnchorLocalPointDocument point = segment.samples[i].anchorLocal;
                if (point == null) continue;
                if (!hasPoints)
                {
                    minX = maxX = point.x;
                    minZ = maxZ = point.z;
                    hasPoints = true;
                }
                else
                {
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minZ = Mathf.Min(minZ, point.z);
                    maxZ = Mathf.Max(maxZ, point.z);
                }
            }
        }

        private void DrawRouteSegment(TrackingSegmentDocument segment, Rect plot, float minX, float maxX, float minZ, float maxZ, float scale)
        {
            if (segment == null || segment.samples == null || segment.samples.Count < 2) return;
            int stride = Mathf.Max(1, segment.samples.Count / 120);
            int previousIndex = 0;
            Vector2 previous = MapRoutePoint(segment.samples[0], plot, minX, maxX, minZ, maxZ);
            for (int i = stride; i < segment.samples.Count; i += stride)
            {
                Vector2 next = MapRoutePoint(segment.samples[i], plot, minX, maxX, minZ, maxZ);
                DrawUiLine(previous, next, NavigationUiPalette.BrandBlue, 4f * scale);
                previous = next;
                previousIndex = i;
            }
            if (previousIndex != segment.samples.Count - 1)
            {
                Vector2 last = MapRoutePoint(segment.samples[segment.samples.Count - 1], plot, minX, maxX, minZ, maxZ);
                DrawUiLine(previous, last, NavigationUiPalette.BrandBlue, 4f * scale);
            }
        }

        private static Vector2 MapRoutePoint(RouteSampleDocument sample, Rect plot, float minX, float maxX, float minZ, float maxZ)
        {
            float span = Mathf.Max(0.6f, Mathf.Max(maxX - minX, maxZ - minZ));
            float fit = Mathf.Min(plot.width, plot.height) * 0.82f;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            return new Vector2(
                plot.center.x + ((sample.anchorLocal.x - centerX) / span) * fit,
                plot.center.y - ((sample.anchorLocal.z - centerZ) / span) * fit);
        }

        private void DrawPoiMarkers(WalkCaptureDocument capture, Rect plot, float minX, float maxX, float minZ, float maxZ, float scale)
        {
            if (capture == null || capture.poiMarkers == null) return;
            for (int i = 0; i < capture.poiMarkers.Count; i++)
            {
                CapturedPoiMarkerDocument marker = capture.poiMarkers[i];
                RouteSampleDocument sample = FindRouteSample(capture, marker.segmentId, marker.sampleSequence);
                if (sample == null) continue;
                Vector2 point = MapRoutePoint(sample, plot, minX, maxX, minZ, maxZ);
                DrawRouteMarker(point, new Color(1f, 0.72f, 0.25f, 1f), 12f * scale);
                if (!string.IsNullOrWhiteSpace(marker.label))
                    GUI.Label(new Rect(point.x + 7f * scale, point.y - 14f * scale, 96f * scale, 18f * scale), marker.label, recorderPoiStyle);
            }
        }

        private void DrawJunctionMarkers(WalkCaptureDocument capture, Rect plot, float minX, float maxX, float minZ, float maxZ, float scale)
        {
            if (capture == null || capture.junctionMarkers == null) return;
            for (int i = 0; i < capture.junctionMarkers.Count; i++)
            {
                CapturedJunctionMarkerDocument marker = capture.junctionMarkers[i];
                RouteSampleDocument sample = FindRouteSample(capture, marker.segmentId, marker.sampleSequence);
                if (sample == null) continue;
                Vector2 point = MapRoutePoint(sample, plot, minX, maxX, minZ, maxZ);
                DrawRouteMarker(point, new Color(0.24f, 0.72f, 1f, 1f), 14f * scale);
                GUI.Label(new Rect(point.x + 8f * scale, point.y - 14f * scale, 76f * scale, 18f * scale), "Node", recorderPoiStyle);
            }
        }

        private RouteSampleDocument FindRouteSample(WalkCaptureDocument capture, string segmentId, int sampleSequence)
        {
            if (capture == null || capture.segments == null) return null;
            for (int i = 0; i < capture.segments.Count; i++)
            {
                TrackingSegmentDocument segment = capture.segments[i];
                if (segment == null || segment.segmentId != segmentId || segment.samples == null) continue;
                return sampleSequence >= 0 && sampleSequence < segment.samples.Count ? segment.samples[sampleSequence] : null;
            }
            TrackingSegmentDocument current = captureBuilder != null && captureBuilder.Capture == capture ? captureBuilder.CurrentSegment : null;
            if (current != null && current.segmentId == segmentId && current.samples != null && sampleSequence >= 0 && sampleSequence < current.samples.Count)
                return current.samples[sampleSequence];
            return null;
        }

        private RouteSampleDocument GetFirstRouteSample(WalkCaptureDocument capture)
        {
            if (capture == null) return null;
            if (capture.segments != null)
                for (int i = 0; i < capture.segments.Count; i++)
                    if (capture.segments[i] != null && capture.segments[i].samples != null && capture.segments[i].samples.Count > 0)
                        return capture.segments[i].samples[0];
            TrackingSegmentDocument current = captureBuilder != null && captureBuilder.Capture == capture ? captureBuilder.CurrentSegment : null;
            return current != null && current.samples != null && current.samples.Count > 0 ? current.samples[0] : null;
        }

        private RouteSampleDocument GetLastRouteSample(WalkCaptureDocument capture)
        {
            if (capture == null) return null;
            if (captureBuilder != null && captureBuilder.Capture == capture && captureBuilder.CurrentSegment != null &&
                captureBuilder.CurrentSegment.samples != null && captureBuilder.CurrentSegment.samples.Count > 0)
                return captureBuilder.CurrentSegment.samples[captureBuilder.CurrentSegment.samples.Count - 1];
            if (capture.segments != null)
                for (int i = capture.segments.Count - 1; i >= 0; i--)
                    if (capture.segments[i] != null && capture.segments[i].samples != null && capture.segments[i].samples.Count > 0)
                        return capture.segments[i].samples[capture.segments[i].samples.Count - 1];
            return null;
        }

        private void CloseRecorderPanel()
        {
            if (capturing) FinishCapture("sessionEnded");
            if (scanViewOpen || trackingRequestAccepted) StopTracking();
            if (nianticAccessTokenProvider != null) nianticAccessTokenProvider.Logout();
            panelOpen = false;
            OnPanelClosed?.Invoke();
        }

        private void ReleaseRecorderUiTextures()
        {
            if (recorderPanelTexture != null) Destroy(recorderPanelTexture);
            if (recorderSurfaceTexture != null) Destroy(recorderSurfaceTexture);
            if (recorderInputTexture != null) Destroy(recorderInputTexture);
            if (recorderPrimaryTexture != null) Destroy(recorderPrimaryTexture);
            if (recorderPrimaryHoverTexture != null) Destroy(recorderPrimaryHoverTexture);
            if (recorderPrimaryActiveTexture != null) Destroy(recorderPrimaryActiveTexture);
            if (recorderSecondaryTexture != null) Destroy(recorderSecondaryTexture);
            if (recorderSecondaryHoverTexture != null) Destroy(recorderSecondaryHoverTexture);
            if (recorderSecondaryActiveTexture != null) Destroy(recorderSecondaryActiveTexture);
            if (recorderOverlaySurfaceTexture != null) Destroy(recorderOverlaySurfaceTexture);
            if (recorderOverlaySecondaryTexture != null) Destroy(recorderOverlaySecondaryTexture);
            if (recorderOverlaySecondaryHoverTexture != null) Destroy(recorderOverlaySecondaryHoverTexture);
            if (recorderOverlaySecondaryActiveTexture != null) Destroy(recorderOverlaySecondaryActiveTexture);
            if (recorderOverlayInsetTexture != null) Destroy(recorderOverlayInsetTexture);
            if (recorderTrackingTexture != null) Destroy(recorderTrackingTexture);
            if (recorderCircleTexture != null) Destroy(recorderCircleTexture);
            if (recorderCameraPanelTexture != null) Destroy(recorderCameraPanelTexture);
            if (recorderCameraBadgeTexture != null) Destroy(recorderCameraBadgeTexture);
            if (recorderDangerTexture != null) Destroy(recorderDangerTexture);
            if (recorderDangerHoverTexture != null) Destroy(recorderDangerHoverTexture);
            if (recorderDangerActiveTexture != null) Destroy(recorderDangerActiveTexture);
            if (recorderMetricCardTexture != null) Destroy(recorderMetricCardTexture);
            if (recorderCheckTexture != null) Destroy(recorderCheckTexture);
        }

        private void DrawSiteSelection()
        {
            GUILayout.Label("01  ·  Kết nối Niantic", recorderSectionStyle);
            if (!organizationsLoaded)
            {
                GUI.enabled = !sitesBusy;
                if (GUILayout.Button(sitesBusy ? "Đang tải tổ chức…" : "Tải tổ chức từ Niantic", recorderSecondaryButtonStyle, GUILayout.Height(44f * GetRecorderUiScale())))
                    StartCoroutine(LoadOrganizations());
                GUI.enabled = true;
            }
            else if (organizations.Length == 0)
            {
                GUILayout.Label("Không có tổ chức khả dụng với cấu hình hiện tại.", recorderMetaStyle);
                if (GUILayout.Button("Thử tải lại", recorderSecondaryButtonStyle, GUILayout.Height(40f * GetRecorderUiScale()))) StartCoroutine(LoadOrganizations());
            }
            else
            {
                string[] organizationLabels = new string[organizations.Length];
                for (int index = 0; index < organizations.Length; index++)
                    organizationLabels[index] = string.IsNullOrWhiteSpace(organizations[index].Name) ? organizations[index].Id : organizations[index].Name;
                GUILayout.Label("Tổ chức", recorderMetaStyle);
                int nextOrganization = DrawRecorderOptionList(organizationLabels, selectedOrganizationIndex, GetRecorderUiScale());
                if (nextOrganization != selectedOrganizationIndex)
                {
                    selectedOrganizationIndex = nextOrganization;
                    selectedSiteIndex = -1;
                    selectedAssetIndex = -1;
                    sites = Array.Empty<SiteInfo>();
                    productionAssets = Array.Empty<AssetInfo>();
                    sitesLoaded = false;
                    ResetSelectableFloors();
                }

                if (selectedOrganizationIndex >= 0 && selectedOrganizationIndex < organizations.Length && !sitesLoaded)
                {
                    GUI.enabled = !sitesBusy;
                    if (GUILayout.Button(sitesBusy ? "Đang tải Site…" : "Tải Site trong tổ chức", recorderSecondaryButtonStyle, GUILayout.Height(42f * GetRecorderUiScale())))
                        StartCoroutine(LoadSites(organizations[selectedOrganizationIndex].Id));
                    GUI.enabled = true;
                }
                if (sitesLoaded && sites.Length == 0) GUILayout.Label("Tổ chức này chưa có Site khả dụng.", recorderMetaStyle);
                if (sitesLoaded && sites.Length > 0)
                {
                    string[] siteLabels = new string[sites.Length];
                    for (int index = 0; index < sites.Length; index++)
                        siteLabels[index] = string.IsNullOrWhiteSpace(sites[index].Name) ? sites[index].Id : sites[index].Name;
                    GUILayout.Label("Site", recorderMetaStyle);
                    int nextSite = DrawRecorderOptionList(siteLabels, selectedSiteIndex, GetRecorderUiScale());
                    if (nextSite != selectedSiteIndex)
                    {
                        selectedSiteIndex = nextSite;
                        selectedAssetIndex = -1;
                        productionAssets = Array.Empty<AssetInfo>();
                        ResetSelectableFloors();
                    }
                    if (selectedSiteIndex >= 0 && selectedSiteIndex < sites.Length && productionAssets.Length == 0)
                    {
                        GUI.enabled = !sitesBusy;
                        if (GUILayout.Button(sitesBusy ? "Đang kiểm tra VPS asset…" : "Tải VPS asset Production", recorderSecondaryButtonStyle, GUILayout.Height(42f * GetRecorderUiScale())))
                            StartCoroutine(LoadProductionVpsAssets(sites[selectedSiteIndex].Id));
                        GUI.enabled = true;
                    }
                    if (productionAssets.Length == 0 && selectedSiteIndex >= 0) GUILayout.Label("Chưa tải VPS asset.", recorderMetaStyle);
                    if (productionAssets.Length > 0)
                    {
                        string[] assetLabels = new string[productionAssets.Length];
                        for (int index = 0; index < productionAssets.Length; index++)
                            assetLabels[index] = string.IsNullOrWhiteSpace(productionAssets[index].Name) ? productionAssets[index].Id : productionAssets[index].Name;
                        GUILayout.Label("VPS asset Production", recorderMetaStyle);
                        int nextAsset = DrawRecorderOptionList(assetLabels, selectedAssetIndex, GetRecorderUiScale());
                        if (nextAsset != selectedAssetIndex)
                        {
                            selectedAssetIndex = nextAsset;
                            ResetSelectableFloors();
                        }
                    }
                }
            }
        }

        private void DrawFloorSelection(float scale)
        {
            GUILayout.Space(8f * scale);
            GUILayout.Label("02  ·  Chọn tầng", recorderSectionStyle);
            if (selectedSiteIndex < 0 || selectedSiteIndex >= sites.Length ||
                selectedAssetIndex < 0 || selectedAssetIndex >= productionAssets.Length)
            {
                GUILayout.Label("Chọn Site và VPS asset trước để tải tầng đã liên kết.", recorderMetaStyle);
                return;
            }

            if (floorsLoading)
            {
                GUILayout.Label("Đang tải tầng khớp với Site và VPS asset…", recorderMetaStyle);
                return;
            }

            if (!floorsLoaded && !floorsFailed)
            {
                StartCoroutine(LoadSelectableFloors(
                    sites[selectedSiteIndex].Id,
                    productionAssets[selectedAssetIndex].Id,
                    ++floorQueryGeneration));
                GUILayout.Label("Đang tải tầng khớp với Site và VPS asset…", recorderMetaStyle);
                return;
            }

            if (floorsFailed)
            {
                GUILayout.Label("Không tải được danh sách tầng.", recorderMetaStyle);
                if (GUILayout.Button("Thử tải lại", recorderSecondaryButtonStyle, GUILayout.Height(42f * scale)))
                {
                    floorsFailed = false;
                    StartCoroutine(LoadSelectableFloors(
                        sites[selectedSiteIndex].Id,
                        productionAssets[selectedAssetIndex].Id,
                        ++floorQueryGeneration));
                }
                return;
            }

            if (selectableFloors.Length == 0)
            {
                GUILayout.Label("Chưa có tầng được liên kết với Site và VPS asset này.", recorderMetaStyle);
                if (GUILayout.Button("Làm mới danh sách tầng", recorderSecondaryButtonStyle, GUILayout.Height(42f * scale)))
                {
                    ResetSelectableFloors();
                    StartCoroutine(LoadSelectableFloors(
                        sites[selectedSiteIndex].Id,
                        productionAssets[selectedAssetIndex].Id,
                        ++floorQueryGeneration));
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingFloorId) && selectedFloorIndex < 0)
                GUILayout.Label("Tầng của capture đã lưu không khớp cấu hình hiện tại. Capture chưa được gửi.", recorderWaitingBadgeStyle);

            string[] floorLabels = new string[selectableFloors.Length];
            for (int index = 0; index < selectableFloors.Length; index++)
                floorLabels[index] = selectableFloors[index].Label;

            GUILayout.Label("Tầng", recorderMetaStyle);
            GUI.enabled = string.IsNullOrEmpty(pendingCaptureJson) || string.IsNullOrEmpty(pendingFloorId);
            int nextFloor = DrawRecorderOptionList(floorLabels, selectedFloorIndex, scale);
            GUI.enabled = true;
            if (nextFloor >= 0 && nextFloor < selectableFloors.Length && nextFloor != selectedFloorIndex)
            {
                selectedFloorIndex = nextFloor;
                floorId = selectableFloors[nextFloor].id;
                if (!string.IsNullOrEmpty(pendingCaptureJson) && string.IsNullOrWhiteSpace(pendingFloorId))
                    SavePendingFloorBinding(floorId);
            }
        }

        private IEnumerator LoadSelectableFloors(string siteId, string productionAssetId, int generation)
        {
            floorsLoading = true;
            bool sessionReady = false;
            yield return EnsureGuestSession(value => sessionReady = value);
            if (generation != floorQueryGeneration) yield break;
            if (!sessionReady)
            {
                floorsLoading = false;
                floorsFailed = true;
                yield break;
            }

            NavigationApiResponse response = null;
            yield return apiClient.GetSelectableFloors(
                siteId, productionAssetId, sessionToken, result => response = result);
            if (generation != floorQueryGeneration) yield break;
            floorsLoading = false;
            if (response == null || !response.Success)
            {
                if (response != null && response.StatusCode == 401)
                {
                    HandleUnauthorizedAdminSession();
                    yield break;
                }
                floorsFailed = true;
                status = "Không tải được Floor từ backend: " + (response == null ? "không có phản hồi" : response.ErrorMessage);
                yield break;
            }

            RecordableFloorListResponse result;
            try { result = JsonUtility.FromJson<RecordableFloorListResponse>(response.Body); }
            catch (Exception)
            {
                floorsFailed = true;
                status = "Backend trả danh sách Floor không hợp lệ.";
                yield break;
            }
            selectableFloors = result != null && result.items != null
                ? result.items
                : Array.Empty<RecordableFloorOption>();
            floorsLoaded = true;
            selectedFloorIndex = -1;

            if (!string.IsNullOrWhiteSpace(pendingFloorId))
                selectedFloorIndex = Array.FindIndex(selectableFloors, floor => floor != null && floor.id == pendingFloorId);
            else if (selectableFloors.Length == 1)
                selectedFloorIndex = 0;

            floorId = selectedFloorIndex >= 0 ? selectableFloors[selectedFloorIndex].id : "";
            if (!string.IsNullOrEmpty(pendingCaptureJson) && string.IsNullOrWhiteSpace(pendingFloorId) && !string.IsNullOrWhiteSpace(floorId))
                SavePendingFloorBinding(floorId);

            status = selectableFloors.Length == 0
                ? "Không tìm thấy Floor có binding khớp Site/Production asset đã chọn."
                : "Đã tải Floor phù hợp với Site/Production asset.";
        }

        private void ResetSelectableFloors()
        {
            floorQueryGeneration++;
            floorsLoading = false;
            floorsLoaded = false;
            floorsFailed = false;
            selectableFloors = Array.Empty<RecordableFloorOption>();
            selectedFloorIndex = -1;
            floorId = "";
        }

        private void SavePendingFloorBinding(string selectedFloorId)
        {
            if (string.IsNullOrWhiteSpace(selectedFloorId) || string.IsNullOrWhiteSpace(pendingCapturePath) ||
                string.IsNullOrWhiteSpace(pendingCaptureJson)) return;
            try
            {
                var queuedCapture = new PendingRouteCaptureQueueDocument
                {
                    floorId = selectedFloorId,
                    captureJson = pendingCaptureJson
                };
                File.WriteAllText(pendingCapturePath, JsonUtility.ToJson(queuedCapture), new UTF8Encoding(false));
                pendingFloorId = selectedFloorId;
                status = "Đã lưu Floor đích cho capture đang chờ.";
            }
            catch (Exception)
            {
                pendingFloorId = "";
                status = "Không lưu được Floor của capture đang chờ; capture chưa được gửi.";
            }
        }

        private string GetSelectedFloorLabel()
        {
            return selectedFloorIndex >= 0 && selectedFloorIndex < selectableFloors.Length && selectableFloors[selectedFloorIndex] != null
                ? selectableFloors[selectedFloorIndex].Label
                : "Chưa chọn Floor";
        }

        private string GetFloorLabel(string targetFloorId)
        {
            for (int index = 0; index < selectableFloors.Length; index++)
                if (selectableFloors[index] != null && selectableFloors[index].id == targetFloorId)
                    return selectableFloors[index].Label;
            return "Floor đã lưu";
        }

        private IEnumerator LoadOrganizations()
        {
            VpsSystemDiagnostics.Record("SITES", "organization query started");
            sitesBusy = true;
            bool tokenReady = false;
            string tokenError = null;
            yield return nianticAccessTokenProvider.EnsureAccessToken(sessionToken, (success, error) =>
            {
                tokenReady = success;
                tokenError = error;
            });
            if (!tokenReady)
            {
                VpsSystemDiagnostics.Record("SITES", "organization query stopped: backend access token unavailable");
                sitesBusy = false;
                status = string.IsNullOrWhiteSpace(tokenError)
                    ? "Không lấy được access token Niantic từ backend."
                    : tokenError;
                yield break;
            }
            Task<OrganizationResult> task;
            try { task = sitesClient.GetSelfOrganizationInfoAsync(); }
            catch (Exception exception) { VpsSystemDiagnostics.Record("SITES", "organization request threw " + exception.GetType().Name); sitesBusy = false; status = "Không thể gọi Niantic Sites API."; yield break; }
            yield return WaitFor(task);
            sitesBusy = false;
            if (task.IsFaulted || task.IsCanceled || task.Result.Status != SitesRequestStatus.Success)
            {
                VpsSystemDiagnostics.Record("SITES", "organization query failed taskStatus=" + task.Status +
                    " requestStatus=" + (task.IsFaulted || task.IsCanceled ? "unavailable" : task.Result.Status + " error=" + task.Result.Error));
                organizationsLoaded = false;
                status = "Không tải được tổ chức Niantic. Kiểm tra quyền Sites của access token backend.";
                yield break;
            }
            organizations = task.Result.Organizations ?? Array.Empty<OrganizationInfo>();
            VpsSystemDiagnostics.Record("SITES", "organization query succeeded count=" + organizations.Length);
            organizationsLoaded = true;
            selectedOrganizationIndex = organizations.Length == 1 ? 0 : -1;
            sitesLoaded = false;
            status = "Đã tải danh sách tổ chức từ Niantic.";
        }

        private IEnumerator LoadSites(string organizationId)
        {
            VpsSystemDiagnostics.Record("SITES", "site list query started");
            sitesBusy = true;
            bool tokenReady = false;
            string tokenError = null;
            yield return nianticAccessTokenProvider.EnsureAccessToken(sessionToken, (success, error) =>
            {
                tokenReady = success;
                tokenError = error;
            });
            if (!tokenReady)
            {
                sitesBusy = false;
                status = string.IsNullOrWhiteSpace(tokenError)
                    ? "Không lấy được access token Niantic từ backend."
                    : tokenError;
                yield break;
            }

            Task<SiteResult> task;
            try { task = sitesClient.GetSitesForOrganizationAsync(organizationId); }
            catch (Exception exception) { VpsSystemDiagnostics.Record("SITES", "site list request threw " + exception.GetType().Name); sitesBusy = false; status = "Không thể gọi Niantic Sites API."; yield break; }
            yield return WaitFor(task);
            sitesBusy = false;
            if (task.IsFaulted || task.IsCanceled || task.Result.Status != SitesRequestStatus.Success)
            {
                VpsSystemDiagnostics.Record("SITES", "site list query failed taskStatus=" + task.Status +
                    " requestStatus=" + (task.IsFaulted || task.IsCanceled ? "unavailable" : task.Result.Status + " error=" + task.Result.Error));
                sitesLoaded = false;
                status = "Không tải được Site trong tổ chức đã chọn.";
                yield break;
            }
            sites = task.Result.Sites ?? Array.Empty<SiteInfo>();
            VpsSystemDiagnostics.Record("SITES", "site list query succeeded count=" + sites.Length);
            sitesLoaded = true;
            selectedSiteIndex = sites.Length == 1 ? 0 : -1;
            if (previewCapture != null && !string.IsNullOrWhiteSpace(previewCapture.siteId))
                selectedSiteIndex = Array.FindIndex(sites, site => site.Id == previewCapture.siteId);
            productionAssets = Array.Empty<AssetInfo>();
            if (selectedSiteIndex >= 0 && selectedSiteIndex < sites.Length && previewCapture != null)
            {
                status = "Đang khôi phục Site/asset của capture đang chờ.";
                StartCoroutine(LoadProductionVpsAssets(sites[selectedSiteIndex].Id));
            }
            else
            {
                status = previewCapture != null
                    ? "Không tìm thấy Site của capture đang chờ trong tổ chức đã chọn."
                    : "Chọn Site để truy vấn Production VPS asset.";
            }
        }

        private IEnumerator LoadProductionVpsAssets(string requestedSiteId)
        {
            VpsSystemDiagnostics.Record("SITES", "Production VPS asset query started");
            sitesBusy = true;
            bool tokenReady = false;
            string tokenError = null;
            yield return nianticAccessTokenProvider.EnsureAccessToken(sessionToken, (success, error) =>
            {
                tokenReady = success;
                tokenError = error;
            });
            if (!tokenReady)
            {
                sitesBusy = false;
                status = string.IsNullOrWhiteSpace(tokenError)
                    ? "Không lấy được access token Niantic từ backend."
                    : tokenError;
                yield break;
            }

            Task<AssetResult> task;
            try { task = sitesClient.GetAssetsForSiteAsync(requestedSiteId); }
            catch (Exception exception) { VpsSystemDiagnostics.Record("SITES", "asset query threw " + exception.GetType().Name); sitesBusy = false; status = "Không thể truy vấn asset của Site."; yield break; }
            yield return WaitFor(task);
            sitesBusy = false;
            if (task.IsFaulted || task.IsCanceled || task.Result.Status != SitesRequestStatus.Success)
            {
                VpsSystemDiagnostics.Record("SITES", "asset query failed taskStatus=" + task.Status +
                    " requestStatus=" + (task.IsFaulted || task.IsCanceled ? "unavailable" : task.Result.Status + " error=" + task.Result.Error));
                productionAssets = Array.Empty<AssetInfo>();
                status = "Không tải được Site assets từ Niantic.";
                yield break;
            }

            IReadOnlyList<AssetInfo> returnedAssets = task.Result.Assets ?? Array.Empty<AssetInfo>();
            var matches = new List<AssetInfo>();
            foreach (AssetInfo asset in returnedAssets)
                if (ProductionVpsAssetFilter.IsEligible(asset, requestedSiteId)) matches.Add(asset);

            productionAssets = matches.ToArray();
            VpsSystemDiagnostics.Record("SITES", "asset query succeeded returned=" + returnedAssets.Count + " eligibleProductionVps=" + productionAssets.Length);
            selectedAssetIndex = productionAssets.Length == 1 ? 0 : -1;
            if (previewCapture != null && !string.IsNullOrWhiteSpace(previewCapture.productionAssetId))
                selectedAssetIndex = Array.FindIndex(
                    productionAssets,
                    asset => asset.Id == previewCapture.productionAssetId);
            status = productionAssets.Length == 0
                ? BuildMissingAssetSummary(returnedAssets)
                : previewCapture != null && selectedAssetIndex < 0
                    ? "Không tìm thấy Production asset của capture đang chờ."
                    : "Đã tải Production VPS asset có anchor payload. Chọn Floor phù hợp với binding trong Admin.";
        }

        private static string BuildMissingAssetSummary(IReadOnlyList<AssetInfo> assets)
        {
            int vpsCount = 0;
            int productionVpsCount = 0;
            int productionWithPayloadCount = 0;
            for (int i = 0; i < assets.Count; i++)
            {
                AssetInfo asset = assets[i];
                if (asset.AssetType != AssetType.VpsInfo) continue;
                vpsCount++;
                if (asset.Deployment != AssetDeploymentType.Production) continue;
                productionVpsCount++;
                if (asset.VpsData.HasValue && !string.IsNullOrWhiteSpace(asset.VpsData.Value.AnchorPayload))
                    productionWithPayloadCount++;
            }
            return string.Format(
                CultureInfo.InvariantCulture,
                "Không tìm thấy Production VPS asset khớp Site. Niantic trả về {0} asset: {1} VPS, {2} VPS Production, {3} Production có payload. Hãy chọn Site khớp với binding Admin.",
                assets.Count, vpsCount, productionVpsCount, productionWithPayloadCount);
        }

        private void EnsureArHost()
        {
            arHostReady = false;
            cameraFrameCount = 0;
            lastCameraFrameAt = -1d;
            VpsSystemDiagnostics.Record("AR", "recorder AR host initialization started");
            VpsSystemDiagnostics.SetState("Recorder AR host", "initializing");
            try
            {
                arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
                if (arSession == null)
                {
                    var sessionObject = new GameObject("AR Session");
                    sessionObject.SetActive(false);
                    arSession = sessionObject.AddComponent<ARSession>();
                }
                arSession.enabled = false;
                arInputManager = arSession.GetComponent<ARInputManager>();
                if (arInputManager == null) arInputManager = arSession.gameObject.AddComponent<ARInputManager>();
                xrOrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
                if (xrOrigin == null)
                {
                    var originObject = new GameObject("XR Origin");
                    // XROrigin must find its camera and offset on its first OnEnable.
                    originObject.SetActive(false);
                    xrOrigin = originObject.AddComponent<XROrigin>();
                    var offset = new GameObject("Camera Floor Offset");
                    offset.transform.SetParent(originObject.transform, false);
                    xrOrigin.CameraFloorOffsetObject = offset;
                }
                xrOrigin.gameObject.SetActive(false);
                arCamera = xrOrigin.Camera;
                if (arCamera == null)
                {
                    Transform parent = xrOrigin.CameraFloorOffsetObject != null ? xrOrigin.CameraFloorOffsetObject.transform : xrOrigin.transform;
                    var cameraObject = new GameObject("AR Camera");
                    cameraObject.transform.SetParent(parent, false);
                    cameraObject.tag = "MainCamera";
                    arCamera = cameraObject.AddComponent<Camera>();
                    xrOrigin.Camera = arCamera;
                }
                arCamera.gameObject.SetActive(true);
                arCamera.tag = "MainCamera";
                arCamera.enabled = true;
                arCamera.targetTexture = null;
                arCamera.rect = new Rect(0f, 0f, 1f, 1f);
                arCamera.clearFlags = CameraClearFlags.Color;
                arCamera.backgroundColor = Color.black;
                arCamera.nearClipPlane = 0.1f;
                arCamera.farClipPlane = 50f;
                arCameraManager = arCamera.GetComponent<ARCameraManager>();
                if (arCameraManager == null) arCameraManager = arCamera.gameObject.AddComponent<ARCameraManager>();
                arCameraManager.enabled = true;
                arCameraBackground = arCamera.GetComponent<ARCameraBackground>();
                if (arCameraBackground == null) arCameraBackground = arCamera.gameObject.AddComponent<ARCameraBackground>();
                arCameraBackground.enabled = true;
                arCameraManager.frameReceived -= OnArCameraFrameReceived;
                arCameraManager.frameReceived += OnArCameraFrameReceived;
                foreach (Camera otherCamera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (otherCamera == arCamera || !otherCamera.enabled) continue;
                    if (!suspendedCameras.Contains(otherCamera)) suspendedCameras.Add(otherCamera);
                    otherCamera.enabled = false;
                }
                AddTrackedPoseDriver(arCamera);

                vps2Manager = FindFirstObjectByType<ARVps2Manager>(FindObjectsInactive.Include);
                if (vps2Manager == null) vps2Manager = xrOrigin.gameObject.AddComponent<ARVps2Manager>();
                vps2Manager.enabled = true;
                localizationAdapter = vps2Manager.GetComponent<NsdkVpsLocalizationAdapter>();
                if (localizationAdapter == null) localizationAdapter = vps2Manager.gameObject.AddComponent<NsdkVpsLocalizationAdapter>();
                Material meshMaterial = Resources.Load<Material>("SiteMeshVertexColor");
                Material standardMaterial = Resources.Load<Material>("SiteMeshStandard");
                // NSDK's MeshDownloadClient constructor calls Shader.Find("Standard") even for textureless meshes.
                if (meshMaterial == null || meshMaterial.shader == null ||
                    standardMaterial == null || standardMaterial.shader == null || Shader.Find("Standard") == null)
                {
                    arHostReady = false;
                    status = "Thiếu Site mesh shader trong Android build; không thể khởi tạo mesh manager.";
                    VpsSystemDiagnostics.SetState("Recorder AR host", "required mesh material/shader missing");
                    VpsSystemDiagnostics.Record("AR", "host initialization stopped: required Site mesh material/shader missing");
                    Debug.LogWarning("[VPS mesh] Required vertex color or Standard shader is missing from the build.");
                    return;
                }
                var meshManagerObject = GameObject.Find("Site Graph Location Mesh Manager");
                if (meshManagerObject == null) meshManagerObject = new GameObject("Site Graph Location Mesh Manager");
                meshManager = meshManagerObject.GetComponent<LocationMeshManager>();
                if (meshManager == null) meshManager = meshManagerObject.AddComponent<LocationMeshManager>();
                meshManager.VertexColorMaterial = meshMaterial;
                meshManager.TexturedMeshMaterial = standardMaterial;

                xrOrigin.gameObject.SetActive(true);
                arSession.gameObject.SetActive(true);
                arSession.enabled = true;
                arInputManager.enabled = true;
                arHostReady = true;
                status = "Đang mở camera và chờ hình ảnh trực tiếp…";
                VpsSystemDiagnostics.SetState("Recorder AR host", "ready; ARSession=" + ARSession.state +
                    " camera=" + (arCamera != null) + " vps2Manager=" + (vps2Manager != null) + " meshManager=" + (meshManager != null));
            }
            catch (Exception exception)
            {
                arHostReady = false;
                status = "Không khởi tạo được AR host. Kiểm tra XR loader và scene configuration.";
                VpsSystemDiagnostics.SetState("Recorder AR host", "failed: " + exception.GetType().Name);
                VpsSystemDiagnostics.Record("AR", "host initialization failed: " + exception.GetType().Name);
            }
        }

        private void OnArCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            if (!scanViewOpen || args.textures == null || args.textures.Count == 0) return;
            if (args.textures[0] == null || args.textures[0].width <= 0 || args.textures[0].height <= 0) return;
            lastCameraFrameAt = Time.realtimeSinceStartupAsDouble;
            if (cameraFrameCount++ == 0)
                VpsSystemDiagnostics.Record("AR", "first camera texture received; camera preview can be shown");
        }

        private void PauseRecorderCamera()
        {
            if (arCameraManager != null) arCameraManager.frameReceived -= OnArCameraFrameReceived;
            if (!VpsWorkflowCoordinator.IsOwnedByOther(this))
            {
                if (arCameraBackground != null) arCameraBackground.enabled = false;
                if (arCameraManager != null) arCameraManager.enabled = false;
                if (arCamera != null) arCamera.enabled = false;
                if (arSession != null) arSession.enabled = false;
                if (arInputManager != null) arInputManager.enabled = false;
                foreach (Camera previousCamera in suspendedCameras)
                    if (previousCamera != null) previousCamera.enabled = true;
            }
            suspendedCameras.Clear();
            arHostReady = false;
            cameraFrameCount = 0;
            lastCameraFrameAt = -1d;
        }

        private static void AddTrackedPoseDriver(Camera camera)
        {
            if (camera.GetComponent<TrackedPoseDriver>() != null) return;
            var poseDriver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            var position = new InputAction("AR Camera Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            position.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotation = new InputAction("AR Camera Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
            poseDriver.positionInput = new InputActionProperty(position);
            poseDriver.rotationInput = new InputActionProperty(rotation);
        }

        private void BeginTracking(AssetInfo asset)
        {
            if (locateStarting || trackingRequestAccepted) return;
            VpsSystemDiagnostics.Record("VPS", "locate action entered; arHostReady=" + arHostReady +
                " siteSelected=" + (selectedSiteIndex >= 0 && selectedSiteIndex < sites.Length) +
                " floorSelected=" + !string.IsNullOrWhiteSpace(floorId) + " assetType=" + asset.AssetType +
                " deployment=" + asset.Deployment);
            if (selectedSiteIndex < 0 || selectedSiteIndex >= sites.Length ||
                asset.AssetType != AssetType.VpsInfo || asset.Deployment != AssetDeploymentType.Production ||
                !asset.VpsData.HasValue || !string.Equals(asset.SiteId, sites[selectedSiteIndex].Id, StringComparison.Ordinal))
            {
                VpsSystemDiagnostics.Record("VPS", "locate rejected: Site/Production asset binding invalid");
                status = "Site/asset không phải Production VPS binding hợp lệ.";
                return;
            }
            if (!IsMongoObjectId(floorId.Trim()) || selectedFloorIndex < 0 || selectedFloorIndex >= selectableFloors.Length ||
                selectableFloors[selectedFloorIndex] == null || selectableFloors[selectedFloorIndex].id != floorId)
            {
                VpsSystemDiagnostics.Record("VPS", "locate rejected: bound Floor selection invalid");
                status = "Hãy chọn một Floor đang bind với Site và Production asset này.";
                return;
            }
            string siteId = sites[selectedSiteIndex].Id;
            string payload = asset.VpsData.Value.AnchorPayload;
            if (!string.Equals(asset.SiteId, siteId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(payload))
            {
                VpsSystemDiagnostics.Record("VPS", "locate rejected: Site or anchor payload mismatch/missing");
                status = "Niantic asset không khớp với Site đã chọn.";
                return;
            }

            if (!VpsWorkflowCoordinator.TryAcquire(this))
            {
                status = "Một quy trình khác đang sử dụng Site anchor. Hãy dừng quy trình đó trước.";
                return;
            }
            scanViewOpen = true;
            locateStarting = true;
            status = "Đang mở camera…";
            locateRoutine = StartCoroutine(BeginTrackingWithAccessToken(payload));
        }

        private IEnumerator BeginTrackingWithAccessToken(string payload)
        {
            try
            {
                // Let OnGUI switch to the camera overlay before starting or waiting on AR.
                yield return null;
                yield return CheckArServiceAvailability();
                if (!arServiceReady) yield break;
                EnsureArHost();
                if (!arHostReady) yield break;

                double deadline = Time.realtimeSinceStartupAsDouble + CameraStartupTimeoutSeconds;
                while (!HasCameraPreview && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                if (!HasCameraPreview)
                {
                    status = GetCameraStartupErrorMessage();
                    VpsSystemDiagnostics.Record("AR", "camera startup timed out; frames=" + cameraFrameCount +
                        " state=" + ARSession.state + " reason=" + ARSession.notTrackingReason);
                    yield break;
                }

                deadline = Time.realtimeSinceStartupAsDouble + ArTrackingTimeoutSeconds;
                while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                if (ARSession.state != ARSessionState.SessionTracking || !HasCameraPreview)
                {
                    status = "AR chưa theo dõi được không gian. " + GetArReadinessMessage() + " Bấm thử lại khi sẵn sàng.";
                    VpsSystemDiagnostics.Record("AR", "AR tracking startup timed out; state=" + ARSession.state +
                        " reason=" + ARSession.notTrackingReason);
                    yield break;
                }
                yield return RequestSiteAnchor(payload);
            }
            finally
            {
                locateStarting = false;
                locateRoutine = null;
                if (!trackingRequestAccepted && !HasCameraPreview) PauseRecorderCamera();
            }
        }

        private IEnumerator CheckArServiceAvailability()
        {
            arServiceReady = false;
            checkingArService = true;
            status = "Đang kiểm tra dịch vụ AR của thiết bị…";
            try
            {
                var sessions = new List<XRSessionSubsystem>();
                SubsystemManager.GetSubsystems(sessions);
                if (sessions.Count == 0)
                {
                    status = "Không tìm thấy dịch vụ AR. Kiểm tra XR loader và khả năng hỗ trợ AR của thiết bị.";
                    yield break;
                }
                XRSessionSubsystem session = sessions[0];
                var availability = session.GetAvailabilityAsync();
                double deadline = Time.realtimeSinceStartupAsDouble + CameraStartupTimeoutSeconds;
                while (availability.keepWaiting && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (availability.keepWaiting)
                {
                    status = "Không kiểm tra được dịch vụ AR. Kiểm tra kết nối mạng rồi thử lại.";
                    yield break;
                }
                VpsSystemDiagnostics.SetState("AR service availability", availability.result.ToString());
                if ((availability.result & SessionAvailability.Supported) == 0)
                {
                    status = "Dịch vụ AR hiện chưa hỗ trợ thiết bị này. Không thể mở camera quét VPS.";
                    yield break;
                }
                if ((availability.result & SessionAvailability.Installed) == 0)
                {
                    if (!session.subsystemDescriptor.supportsInstall)
                    {
                        status = "Thiết bị cần cài đặt hoặc cập nhật dịch vụ AR trước khi quét.";
                        yield break;
                    }
                    status = "Vui lòng hoàn tất cài đặt hoặc cập nhật dịch vụ AR trên màn hình hệ thống.";
                    var installation = session.InstallAsync();
                    yield return installation;
                    VpsSystemDiagnostics.SetState("AR service installation", installation.result.ToString());
                    if (installation.result != SessionInstallationStatus.Success)
                    {
                        status = GetArInstallErrorMessage(installation.result);
                        yield break;
                    }
                }
                // An installed service can still reject the device profile. Actual frames are checked next.
                arServiceReady = true;
            }
            finally { checkingArService = false; }
        }

        private static string GetArInstallErrorMessage(SessionInstallationStatus result)
        {
            return result == SessionInstallationStatus.ErrorDeviceNotCompatible
                ? "Dịch vụ AR xác nhận thiết bị hiện chưa tương thích. Không thể quét VPS trên cấu hình này."
                : result == SessionInstallationStatus.ErrorUserDeclined
                    ? "Cài đặt dịch vụ AR chưa được chấp nhận. Bấm thử lại khi bạn sẵn sàng cài đặt."
                    : "Không cài đặt được dịch vụ AR. Kiểm tra dịch vụ AR và kết nối mạng rồi thử lại.";
        }

        private static string GetCameraStartupErrorMessage()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                return "Ứng dụng chưa có quyền camera. Hãy cấp quyền camera trong cài đặt ứng dụng rồi thử lại.";
            return "ARCore không cung cấp được hình ảnh camera trên thiết bị này. Kiểm tra Google Play Services for AR và khả năng hỗ trợ AR của máy.";
#else
            return "Dịch vụ AR không cung cấp được hình ảnh camera. Kiểm tra quyền camera và khả năng hỗ trợ AR của thiết bị.";
#endif
        }

        private IEnumerator RequestSiteAnchor(string payload)
        {
            VpsSystemDiagnostics.Record("VPS", "token acquisition started for locate");
            bool tokenReady = false;
            string tokenError = null;
            // Keep the shared provider's request alive if the operator cancels this camera flow.
            yield return nianticAccessTokenProvider.StartCoroutine(nianticAccessTokenProvider.EnsureAccessToken(sessionToken, (success, error) =>
            {
                tokenReady = success;
                tokenError = error;
            }));
            if (!tokenReady)
            {
                VpsSystemDiagnostics.Record("VPS", "locate stopped: token unavailable");
                status = string.IsNullOrWhiteSpace(tokenError)
                    ? "Không lấy được access token Niantic từ backend."
                    : tokenError;
                yield break;
            }
            if (!panelOpen || !scanViewOpen || !arHostReady) yield break;
            if (!HasCameraPreview || ARSession.state != ARSessionState.SessionTracking)
            {
                status = "Camera hoặc AR tracking bị gián đoạn. Bấm thử lại để định vị Site.";
                yield break;
            }

            selectedPayload = payload;
            payloadSha256 = Fingerprint(payload);
            if (!VpsWorkflowCoordinator.TryAcquire(this))
            {
                VpsSystemDiagnostics.Record("VPS", "locate rejected: another workflow owns the Site anchor");
                status = "Một quy trình khác đang sử dụng Site anchor. Hãy dừng quy trình đó trước.";
                yield break;
            }
            if (!localizationAdapter.TryTrackAnchor(payload, out trackedAnchor) || trackedAnchor == null)
            {
                VpsSystemDiagnostics.Record("VPS", "NSDK rejected the VPS2 anchor request");
                status = "NSDK không chấp nhận yêu cầu tracking Site.";
                yield break;
            }
            trackingRequestAccepted = true;
            stableTracking = false;
            trackedSince = 0d;
            if (!meshRequestInFlight) meshDownloadFailed = false;
            meshReady = false;
            meshPreviewUploaded = false;
            status = "Yêu cầu đã được nhận; đang chờ VPS2 anchor Tracking.";
            VpsSystemDiagnostics.Record("VPS", "anchor request accepted; waiting for Tracking");
        }

        private IEnumerator DownloadSiteMesh()
        {
            if (meshRequestInFlight)
            {
                VpsSystemDiagnostics.Record("MESH", "download skipped: request already in flight");
                yield break;
            }
            VpsSystemDiagnostics.Record("MESH", "download flow started; trackingAccepted=" + trackingRequestAccepted + " stable=" + stableTracking);
            meshLoading = true;
            bool tokenReady = false;
            string tokenError = null;
            yield return nianticAccessTokenProvider.EnsureAccessToken(sessionToken, (success, error) =>
            {
                tokenReady = success;
                tokenError = error;
            });
            if (!tokenReady)
            {
                VpsSystemDiagnostics.Record("MESH", "download stopped: Niantic access token unavailable");
                meshLoading = false;
                meshDownloadFailed = true;
                status = string.IsNullOrWhiteSpace(tokenError)
                    ? "Không lấy được access token Niantic để tải Site mesh."
                    : tokenError;
                yield break;
            }
            if (!trackingRequestAccepted || !stableTracking || trackedAnchor == null)
            {
                VpsSystemDiagnostics.Record("MESH", "download deferred: anchor not stably tracked");
                meshLoading = false;
                yield break;
            }

            if (meshManager == null || trackedAnchor == null)
            {
                VpsSystemDiagnostics.Record("MESH", "download stopped: LocationMeshManager or anchor missing");
                meshDownloadFailed = true;
                meshLoading = false;
                status = "Mesh manager hoặc VPS2 anchor chưa sẵn sàng; tải mesh đã dừng.";
                yield break;
            }
            int requestGeneration = ++meshRequestGeneration;
            Transform requestedAnchor = trackedAnchor;
            meshLoading = true;
            meshRequestInFlight = true;
            status = "Đang tải Site mesh theo cùng VPS2 anchor payload…";
            Task<GameObject> task;
            float meshRequestStartedAt = Time.realtimeSinceStartup;
            VpsSystemDiagnostics.SetState("Site mesh", "requesting; maxDownloadSizeKb=20480");
            try
            {
                task = meshManager.GetLocationMeshForPayloadAsync(
                    selectedPayload, maxDownloadSizeKb: 20480, addCollider: true, getTexture: false, cancelOnDisable: true);
            }
            catch (Exception exception)
            {
                VpsSystemDiagnostics.Record("MESH", "request start threw " + exception.GetType().Name);
                VpsSystemDiagnostics.SetState("Site mesh", "start failed: " + exception.GetType().Name);
                meshLoading = false;
                meshRequestInFlight = false;
                meshDownloadFailed = true;
                status = "Không thể bắt đầu tải Site mesh (" + exception.GetType().Name + "). Có thể thử lại thủ công.";
                Debug.LogWarning("[VPS mesh] Request start failed; exceptionType=" + exception.GetType().Name + "; maxDownloadSizeKb=20480.");
                yield break;
            }

            if (task == null)
            {
                VpsSystemDiagnostics.Record("MESH", "NSDK returned no mesh task");
                VpsSystemDiagnostics.SetState("Site mesh", "missing task");
                meshLoading = false;
                meshRequestInFlight = false;
                meshDownloadFailed = true;
                status = "NSDK không tạo tác vụ tải Site mesh. Có thể thử lại thủ công.";
                Debug.LogWarning("[VPS mesh] Request returned no task; status=MissingTask; maxDownloadSizeKb=20480.");
                yield break;
            }

            float meshWaitStartedAt = Time.realtimeSinceStartup;
            bool meshWaitSlowReported = false;
            while (!task.IsCompleted)
            {
                if (!meshWaitSlowReported && Time.realtimeSinceStartup - meshWaitStartedAt >= 10f)
                {
                    meshWaitSlowReported = true;
                    VpsSystemDiagnostics.Record("MESH", "NSDK mesh task still pending after 10000ms");
                    VpsSystemDiagnostics.SetState("Site mesh", "pending > 10s");
                }
                yield return null;
            }
            int meshElapsedMs = Mathf.RoundToInt((Time.realtimeSinceStartup - meshRequestStartedAt) * 1000f);
            meshRequestInFlight = false;
            meshLoading = false;
            if (requestGeneration != meshRequestGeneration || !trackingRequestAccepted || !stableTracking || trackedAnchor != requestedAnchor)
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result != null) Destroy(task.Result);
                if (task.IsFaulted)
                    Debug.LogWarning("[VPS mesh] Stale request faulted; exceptionType=" + GetTaskExceptionType(task) + "; maxDownloadSizeKb=20480.");
                VpsSystemDiagnostics.Record("MESH", "completed stale/canceled by state change; taskStatus=" + task.Status + " elapsedMs=" + meshElapsedMs);
                VpsSystemDiagnostics.SetState("Site mesh", "discarded stale result; taskStatus=" + task.Status);
                yield break;
            }
            if (task.IsFaulted)
            {
                VpsSystemDiagnostics.Record("MESH", "task faulted exceptionType=" + GetTaskExceptionType(task) +
                    " faultSite=" + GetTaskExceptionSite(task) + " elapsedMs=" + meshElapsedMs);
                VpsSystemDiagnostics.SetState("Site mesh", "faulted: " + GetTaskExceptionType(task) + " after " + meshElapsedMs + "ms");
                meshDownloadFailed = true;
                status = "Tải Site mesh lỗi (" + GetTaskExceptionType(task) + "). NSDK không trả thông tin đủ để kết luận quyền hay mesh; giới hạn hiện tại 20 MiB. Có thể thử lại thủ công.";
                Debug.LogWarning("[VPS mesh] Request faulted; exceptionType=" + GetTaskExceptionType(task) +
                    "; faultSite=" + GetTaskExceptionSite(task) + "; status=Faulted; maxDownloadSizeKb=20480.");
                yield break;
            }
            if (task.IsCanceled)
            {
                VpsSystemDiagnostics.Record("MESH", "task canceled after " + meshElapsedMs + "ms");
                VpsSystemDiagnostics.SetState("Site mesh", "canceled after " + meshElapsedMs + "ms");
                meshDownloadFailed = true;
                status = "Yêu cầu tải Site mesh đã bị hủy. Có thể thử lại thủ công khi Tracking ổn định.";
                Debug.LogWarning("[VPS mesh] Request canceled; status=Canceled; maxDownloadSizeKb=20480.");
                yield break;
            }
            if (task.Result == null)
            {
                VpsSystemDiagnostics.Record("MESH", "task completed without mesh after " + meshElapsedMs + "ms");
                VpsSystemDiagnostics.SetState("Site mesh", "empty result after " + meshElapsedMs + "ms");
                meshDownloadFailed = true;
                status = "NSDK trả mesh rỗng/null. Site có thể chưa có mesh khả dụng; chưa thể kết luận lỗi quyền.";
                Debug.LogWarning("[VPS mesh] Request completed with null mesh; status=RanToCompletion; maxDownloadSizeKb=20480.");
                yield break;
            }

            siteMesh = task.Result;
            MeshFilter[] downloadedFilters = siteMesh.GetComponentsInChildren<MeshFilter>(true);
            int vertexCount = 0;
            int triangleCount = 0;
            int downloadedMeshCount = 0;
            foreach (MeshFilter filter in downloadedFilters)
            {
                Mesh downloadedMesh = filter == null ? null : filter.sharedMesh;
                if (downloadedMesh == null) continue;
                downloadedMeshCount++;
                vertexCount += downloadedMesh.vertexCount;
                triangleCount += downloadedMesh.triangles.Length / 3;
            }
            VpsSystemDiagnostics.Record("MESH", "download succeeded after " + meshElapsedMs + "ms meshes=" + downloadedMeshCount +
                " vertices=" + vertexCount + " triangles=" + triangleCount);
            VpsSystemDiagnostics.SetState("Site mesh", "downloaded " + vertexCount + " vertices / " + triangleCount + " triangles");
            siteMesh.transform.SetParent(trackedAnchor, false);
            EnsureMeshColliders(siteMesh);
            meshDownloadFailed = false;
            meshReady = true;
            if (SelectedFloorHasCurrentMesh())
            {
                meshPreviewUploaded = true;
                status = "Site mesh đã tải và Floor đã có mesh preview khớp VPS payload. Sẵn sàng ghi tuyến.";
                yield break;
            }
            if (!SiteMeshPreviewBuilder.TryBuild(
                    siteMesh, trackedAnchor, sites[selectedSiteIndex].Id, productionAssets[selectedAssetIndex].Id,
                    payloadSha256, NsdkVersion, DateTime.UtcNow, out siteMeshPreview, out string meshError))
            {
                meshDownloadFailed = true;
                meshReady = false;
                Destroy(siteMesh);
                siteMesh = null;
                status = "Không tạo được mesh preview an toàn: " + meshError;
                yield break;
            }
            status = "Site mesh đã gắn dưới cùng anchor. Đang đồng bộ mesh preview lên Admin.";
            yield return UploadSiteMeshPreview();
        }

        private IEnumerator UploadSiteMeshPreview()
        {
            if (siteMeshPreview == null || uploadBusy) yield break;
            SiteMeshUploadDocument preview = siteMeshPreview;
            string uploadFloorId = floorId.Trim();
            string uploadPayloadSha256 = payloadSha256;
            uploadBusy = true;
            bool sessionReady = false;
            yield return EnsureGuestSession(value => sessionReady = value);
            if (siteMeshPreview != preview || floorId.Trim() != uploadFloorId || payloadSha256 != uploadPayloadSha256)
            {
                uploadBusy = false;
                yield break;
            }
            if (!sessionReady)
            {
                uploadBusy = false;
                status = "Không xác thực được recorder với backend.";
                yield break;
            }
            // JsonUtility omits nested containers such as List<int[]> used for triangle indices.
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(preview);
            if (Encoding.UTF8.GetByteCount(json) > 7_500_000)
            {
                uploadBusy = false;
                status = "Mesh preview vượt giới hạn upload. Cần mesh nhẹ hơn.";
                yield break;
            }
            NavigationApiResponse response = null;
            yield return apiClient.UploadSiteMesh(
                uploadFloorId, json, sessionToken, result => response = result);
            if (siteMeshPreview != preview || floorId.Trim() != uploadFloorId || payloadSha256 != uploadPayloadSha256)
            {
                uploadBusy = false;
                yield break;
            }
            if (response != null && response.StatusCode == 401)
            {
                uploadBusy = false;
                HandleUnauthorizedAdminSession();
                yield break;
            }
            meshPreviewUploaded = response != null && response.Success;
            if (!meshPreviewUploaded)
            {
                NavigationApiResponse floorsResponse = null;
                yield return apiClient.GetSelectableFloors(
                    preview.siteId, preview.productionAssetId, sessionToken,
                    result => floorsResponse = result);
                if (siteMeshPreview != preview || floorId.Trim() != uploadFloorId || payloadSha256 != uploadPayloadSha256)
                {
                    uploadBusy = false;
                    yield break;
                }
                if (floorsResponse != null && floorsResponse.Success)
                {
                    try
                    {
                        RecordableFloorListResponse floors = JsonUtility.FromJson<RecordableFloorListResponse>(floorsResponse.Body);
                        RecordableFloorOption currentFloor = floors?.items == null ? null :
                            Array.Find(floors.items, floor => floor != null && floor.id == uploadFloorId);
                        if (currentFloor != null && currentFloor.meshPayloadSha256 == uploadPayloadSha256)
                        {
                            meshPreviewUploaded = true;
                            if (selectedFloorIndex >= 0 && selectedFloorIndex < selectableFloors.Length &&
                                selectableFloors[selectedFloorIndex] != null && selectableFloors[selectedFloorIndex].id == currentFloor.id)
                                selectableFloors[selectedFloorIndex].meshPayloadSha256 = currentFloor.meshPayloadSha256;
                        }
                    }
                    catch (Exception) { /* Keep the original upload failure when the floor response is invalid. */ }
                }
            }
            uploadBusy = false;
            status = meshPreviewUploaded
                ? "Floor đã có mesh preview khớp VPS payload và sẵn sàng ghi tuyến."
                : "Backend chưa nhận mesh preview: " + (response == null ? "không có phản hồi" : response.ErrorMessage);
        }

        private bool SelectedFloorHasCurrentMesh()
        {
            return selectedFloorIndex >= 0 && selectedFloorIndex < selectableFloors.Length &&
                selectableFloors[selectedFloorIndex] != null && selectableFloors[selectedFloorIndex].id == floorId &&
                !string.IsNullOrEmpty(payloadSha256) &&
                selectableFloors[selectedFloorIndex].meshPayloadSha256 == payloadSha256;
        }

        private void HandleUnauthorizedAdminSession()
        {
            ClearAdminJwtSession();
            if (capturing) FinishCapture("sessionEnded");
            if (scanViewOpen || trackingRequestAccepted) StopTracking();
            panelOpen = false;
            status = "Phiên đăng nhập Admin đã hết hạn. Vui lòng đăng nhập lại.";
            OnAdminSessionExpired?.Invoke(status);
        }

        private IEnumerator EnsureGuestSession(Action<bool> completed)
        {
            if (HasValidAdminJwtSession())
            {
                completed(true);
                yield break;
            }
            if (sessionRequestBusy)
            {
                while (sessionRequestBusy) yield return null;
                completed(HasValidAdminJwtSession());
                yield break;
            }
            HandleUnauthorizedAdminSession();
            completed(false);
        }

        private void BeginCapture()
        {
            if (!stableTracking || !meshPreviewUploaded || pendingCaptureJson.Length > 0) return;
            captureBuilder = new RouteCaptureBuilder();
            if (!captureBuilder.Begin(
                    sites[selectedSiteIndex].Id, productionAssets[selectedAssetIndex].Id, payloadSha256,
                    NsdkVersion, Application.version, DateTime.UtcNow) || !captureBuilder.BeginSegment(DateTime.UtcNow))
            {
                captureBuilder = null;
                status = "Không khởi tạo được capture.";
                return;
            }
            previewCapture = captureBuilder.Capture;
            lastSampleAt = -1d;
            noFloorPointSince = -1d;
            capturing = true;
            status = "Đang ghi quỹ đạo theo Site anchor. Đánh dấu node ở khúc cua hoặc nút giao; POI dùng cho điểm đến.";
        }

        private void CaptureSampleIfDue()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (lastSampleAt >= 0d && now - lastSampleAt < SampleIntervalSeconds) return;
            if (captureBuilder.TotalSampleCount >= MaximumCaptureSamples)
            {
                FinishCapture("operatorStopped");
                status = "Capture đạt giới hạn mẫu và đã được kết thúc an toàn.";
                return;
            }
            if (!TryGetFloorPoint(out Vector3 localPoint))
            {
                if (noFloorPointSince < 0d) noFloorPointSince = now;
                if (now - noFloorPointSince > 1.5d && captureBuilder.CurrentSegment != null)
                {
                    captureBuilder.EndSegment("trackingLost", DateTime.UtcNow);
                    lastSampleAt = -1d;
                    status = "Không thấy sàn trên mesh; quỹ đạo đã tách đoạn để tránh nối qua khoảng trống.";
                }
                return;
            }
            noFloorPointSince = -1d;
            if (captureBuilder.CurrentSegment == null)
            {
                captureBuilder.BeginSegment(DateTime.UtcNow);
            }
            if (captureBuilder.AddSample(localPoint, DateTime.UtcNow)) lastSampleAt = now;
        }

        private bool TryGetFloorPoint(out Vector3 anchorLocalPoint)
        {
            anchorLocalPoint = Vector3.zero;
            if (trackedAnchor == null || arCamera == null || siteMesh == null) return false;
            RaycastHit[] hits = Physics.RaycastAll(
                arCamera.transform.position, -trackedAnchor.up, MaximumMeshDistanceMeters,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int index = 0; index < hits.Length; index++)
            {
                RaycastHit hit = hits[index];
                if (hit.collider == null ||
                    (hit.collider.transform != siteMesh.transform && !hit.collider.transform.IsChildOf(siteMesh.transform))) continue;
                Vector3 localNormal = trackedAnchor.InverseTransformDirection(hit.normal).normalized;
                if (Vector3.Dot(localNormal, Vector3.up) < 0.75f) continue;
                Vector3 local = trackedAnchor.InverseTransformPoint(hit.point);
                if (captureBuilder != null && captureBuilder.CurrentSegment != null && captureBuilder.CurrentSegment.samples.Count > 0)
                {
                    float lastY = captureBuilder.CurrentSegment.samples[captureBuilder.CurrentSegment.samples.Count - 1].anchorLocal.y;
                    if (Mathf.Abs(local.y - lastY) > MaximumFloorHeightDeltaMeters) continue;
                }
                anchorLocalPoint = local;
                return true;
            }
            return false;
        }

        private void MarkPoi()
        {
            if (captureBuilder == null || !stableTracking) return;
            if (!captureBuilder.MarkPoi(poiLabel, poiCategory))
            {
                status = "Nhập tên POI hợp lệ sau khi đã ghi ít nhất hai mẫu.";
                return;
            }
            status = "Đã đánh dấu POI: " + poiLabel.Trim() + ".";
            poiLabel = "";
        }

        private void MarkJunction()
        {
            if (!capturing || captureBuilder == null || !stableTracking) return;
            if (captureBuilder.TotalSampleCount >= MaximumCaptureSamples || !TryGetFloorPoint(out Vector3 localPoint) ||
                !captureBuilder.MarkJunction(localPoint, DateTime.UtcNow))
            {
                status = "Chưa đánh dấu được node. Giữ định vị ổn định trên sàn rồi thử lại.";
                return;
            }
            lastSampleAt = Time.realtimeSinceStartupAsDouble;
            noFloorPointSince = -1d;
            status = "Đã đánh dấu node tại vị trí hiện tại.";
        }

        private void FinishCapture(string reason)
        {
            if (!capturing || captureBuilder == null) return;
            capturing = false;
            WalkCaptureDocument capture = captureBuilder.Finish(reason, DateTime.UtcNow);
            captureBuilder = null;
            previewCapture = capture;
            lastSampleAt = -1d;
            if (capture == null)
            {
                status = "Capture cần ít nhất một đoạn có hai mẫu hợp lệ. Hãy ghi lại tuyến.";
                return;
            }
            SavePendingCapture(floorId.Trim(), capture);
            if (string.IsNullOrWhiteSpace(pendingCaptureJson)) return;
            status = "Capture đã xếp hàng để gửi. Backend sẽ tự sinh graph ở trạng thái cần Admin review.";
        }

        private void SavePendingCapture(string targetFloorId, WalkCaptureDocument capture)
        {
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "route-captures");
                Directory.CreateDirectory(directory);
                pendingCapturePath = Path.Combine(directory, "pending-" + capture.captureId + ".json");
                pendingCaptureJson = JsonUtility.ToJson(capture);
                pendingFloorId = targetFloorId == null ? "" : targetFloorId.Trim();
                var queuedCapture = new PendingRouteCaptureQueueDocument
                {
                    floorId = pendingFloorId,
                    captureJson = pendingCaptureJson
                };
                File.WriteAllText(pendingCapturePath, JsonUtility.ToJson(queuedCapture), new UTF8Encoding(false));
                floorId = pendingFloorId;
            }
            catch (Exception)
            {
                pendingCapturePath = "";
                pendingCaptureJson = "";
                pendingFloorId = "";
                previewCapture = null;
                status = "Không lưu được capture tạm trên thiết bị; tuyến chưa được gửi.";
            }
        }

        private IEnumerator UploadPendingCapture()
        {
            if (uploadBusy || string.IsNullOrEmpty(pendingCaptureJson) || string.IsNullOrWhiteSpace(pendingFloorId) ||
                floorId != pendingFloorId || !IsMongoObjectId(pendingFloorId))
            {
                status = "Hãy chọn đúng Floor đã lưu cùng capture trước khi gửi.";
                yield break;
            }
            uploadBusy = true;
            bool sessionReady = false;
            yield return EnsureGuestSession(value => sessionReady = value);
            if (!sessionReady)
            {
                uploadBusy = false;
                yield break;
            }
            NavigationApiResponse response = null;
            yield return apiClient.UploadRouteCapture(pendingFloorId, pendingCaptureJson, sessionToken, result => response = result);
            uploadBusy = false;
            if (response == null || !response.Success)
            {
                if (response != null && response.StatusCode == 401)
                {
                    HandleUnauthorizedAdminSession();
                    yield break;
                }
                status = "Capture vẫn ở hàng đợi để thử lại: " + (response == null ? "không có phản hồi" : response.ErrorMessage);
                yield break;
            }
            if (!string.IsNullOrWhiteSpace(pendingCapturePath) && File.Exists(pendingCapturePath)) File.Delete(pendingCapturePath);
            pendingCapturePath = "";
            pendingCaptureJson = "";
            pendingFloorId = "";
            previewCapture = null;
            status = "Capture đã nhận; graph đang chờ Admin review và phê duyệt.";
        }

        private void LoadNextPendingCapture()
        {
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "route-captures");
                if (!Directory.Exists(directory)) return;
                string[] files = Directory.GetFiles(directory, "pending-*.json");
                Array.Sort(files, StringComparer.Ordinal);
                if (files.Length == 0) return;
                pendingCapturePath = files[0];
                string queueJson = File.ReadAllText(pendingCapturePath, Encoding.UTF8);
                PendingRouteCaptureQueueDocument queuedCapture = null;
                try { queuedCapture = JsonUtility.FromJson<PendingRouteCaptureQueueDocument>(queueJson); }
                catch (Exception) { }
                if (queuedCapture != null && !string.IsNullOrWhiteSpace(queuedCapture.captureJson))
                {
                    pendingCaptureJson = queuedCapture.captureJson;
                    pendingFloorId = queuedCapture.floorId == null ? "" : queuedCapture.floorId.Trim();
                }
                else
                {
                    pendingCaptureJson = queueJson;
                    pendingFloorId = "";
                }
                previewCapture = JsonUtility.FromJson<WalkCaptureDocument>(pendingCaptureJson);
                if (previewCapture == null || string.IsNullOrWhiteSpace(previewCapture.captureId))
                    throw new InvalidDataException("The queued route capture is invalid.");
                status = string.IsNullOrWhiteSpace(pendingFloorId)
                    ? "Có capture cũ chưa lưu Floor đích. Chọn Site, asset và Floor khớp để tiếp tục gửi."
                    : "Có capture chưa gửi trong hàng đợi thiết bị. Đang khôi phục Site, asset và Floor đã lưu.";
            }
            catch (Exception)
            {
                pendingCapturePath = "";
                pendingCaptureJson = "";
                pendingFloorId = "";
                previewCapture = null;
            }
        }

        private void StopTracking()
        {
            VpsSystemDiagnostics.Record("VPS", "recorder StopTracking called; wasTracking=" + trackingRequestAccepted +
                " meshRequestInFlight=" + meshRequestInFlight);
            if (capturing) FinishCapture("sessionEnded");
            bool hadCameraFlow = scanViewOpen || trackingRequestAccepted;
            if (locateRoutine != null) StopCoroutine(locateRoutine);
            locateRoutine = null;
            locateStarting = false;
            scanViewOpen = false;
            bool wasTracking = trackingRequestAccepted;
            meshRequestGeneration++;
            meshLoading = false;
            meshDownloadFailed = meshRequestInFlight;
            trackingRequestAccepted = false;
            stableTracking = false;
            trackedSince = 0d;
            if (wasTracking && localizationAdapter != null) localizationAdapter.StopTracking();
            if (hadCameraFlow) PauseRecorderCamera();
            VpsWorkflowCoordinator.Release(this);
            trackedAnchor = null;
            if (siteMesh != null) Destroy(siteMesh);
            siteMesh = null;
            siteMeshPreview = null;
            meshReady = false;
            meshPreviewUploaded = false;
            selectedPayload = "";
            payloadSha256 = "";
            status = "Đã dừng VPS2 tracking.";
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && locateStarting && !checkingArService)
            {
                if (locateRoutine != null) StopCoroutine(locateRoutine);
                locateRoutine = null;
                locateStarting = false;
                status = "Quá trình mở camera đã tạm dừng. Bấm thử lại để tiếp tục định vị.";
            }
            if (paused && capturing && captureBuilder != null && captureBuilder.CurrentSegment != null)
            {
                captureBuilder.EndSegment("sessionEnded", DateTime.UtcNow);
                lastSampleAt = -1d;
                stableTracking = false;
                trackedSince = 0d;
                status = "Ứng dụng tạm dừng; capture được tách đoạn để không nối qua thời gian nền.";
            }
        }

        private void OnDisable()
        {
            if (scanViewOpen || trackingRequestAccepted) StopTracking();
        }

        private void OnDestroy()
        {
            if (scanViewOpen || trackingRequestAccepted) StopTracking();
            VpsWorkflowCoordinator.Release(this);
            ReleaseRecorderUiTextures();
            sessionToken = "";
        }

        private static IEnumerator WaitFor<T>(Task<T> task)
        {
            if (task == null)
            {
                VpsSystemDiagnostics.Record("SITES", "SDK returned a missing task");
                yield break;
            }
            float startedAt = Time.realtimeSinceStartup;
            bool slowReported = false;
            while (!task.IsCompleted)
            {
                float elapsed = Time.realtimeSinceStartup - startedAt;
                if (!slowReported && elapsed >= 10f)
                {
                    slowReported = true;
                    VpsSystemDiagnostics.Record("SITES", "SDK task still pending after 10000ms");
                    VpsSystemDiagnostics.SetState("Niantic Sites request", "pending > 10s");
                }
                yield return null;
            }
            VpsSystemDiagnostics.Record("SITES", "SDK task completed status=" + task.Status +
                " elapsedMs=" + Mathf.RoundToInt((Time.realtimeSinceStartup - startedAt) * 1000f));
            VpsSystemDiagnostics.SetState("Niantic Sites request", task.Status.ToString());
        }

        private static string GetTaskExceptionType(Task task)
        {
            Exception exception = task == null || task.Exception == null ? null : task.Exception.GetBaseException();
            return exception == null ? "UnknownException" : exception.GetType().Name;
        }

        private static string GetTaskExceptionSite(Task task)
        {
            try
            {
                Exception exception = task == null || task.Exception == null ? null : task.Exception.GetBaseException();
                if (exception == null) return "Unavailable";
                System.Diagnostics.StackFrame[] frames = new System.Diagnostics.StackTrace(exception, false).GetFrames();
                if (frames == null || frames.Length == 0) return "Unavailable";
                var site = new StringBuilder();
                for (int index = 0; index < frames.Length && index < 6; index++)
                {
                    var method = frames[index].GetMethod();
                    if (method == null) continue;
                    if (site.Length > 0) site.Append(" > ");
                    site.Append(method.DeclaringType == null ? "UnknownType" : method.DeclaringType.FullName);
                    site.Append('.').Append(method.Name);
                }
                return site.Length == 0 ? "Unavailable" : site.ToString();
            }
            catch (Exception)
            {
                return "Unavailable";
            }
        }

        private static void EnsureMeshColliders(GameObject root)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
        }

        private static string Fingerprint(string payload)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static bool IsMongoObjectId(string value)
        {
            if (value == null || value.Length != 24) return false;
            for (int index = 0; index < value.Length; index++)
                if (!Uri.IsHexDigit(value[index])) return false;
            return true;
        }
    }
}
