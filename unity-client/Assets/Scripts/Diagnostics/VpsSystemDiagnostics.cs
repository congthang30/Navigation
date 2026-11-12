using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.Profiling;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Vps.UnityClient.Diagnostics
{
    /// <summary>On-device, bounded diagnostics for UI input, AR/VPS state, API requests and Unity errors.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class VpsSystemDiagnostics : MonoBehaviour
    {
        private const int MaxEntries = 500;
        private const int MaxEntryLength = 1600;
        private static readonly object Sync = new object();
        private static readonly Queue<string> Entries = new Queue<string>(MaxEntries);
        private static readonly Dictionary<string, string> States = new Dictionary<string, string>();
        private static readonly Regex SecretAssignment = new Regex(
            @"(?i)(?:\x22|\x27)?\b(authorization|access[-_ ]?token|session[-_ ]?token|[a-z0-9_-]*api[-_ ]?key|anchor[-_ ]?payload)\b(?:\x22|\x27)?\s*[:=]\s*(?:\x22[^\x22]*\x22|\x27[^\x27]*\x27|[^,\s;]+)",
            RegexOptions.Compiled);
        private static readonly Regex BearerValue = new Regex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*", RegexOptions.Compiled);
        private static readonly Regex JwtValue = new Regex(@"\b[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\b", RegexOptions.Compiled);
        private static readonly Regex ObjectIdValue = new Regex(@"\b[a-fA-F0-9]{24}\b", RegexOptions.Compiled);
        private static readonly Regex GuidValue = new Regex(@"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b", RegexOptions.Compiled);

        private static VpsSystemDiagnostics instance;
        private Vector2 logScroll;
        private bool panelOpen;
        private bool touchLogging;
        private string exportStatus = string.Empty;
        private int lastObservedArState = -1;
        private int lastObservedArNotTrackingReason = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateBeforeSceneLoad()
        {
            if (instance != null) return;
            var host = new GameObject("VPS System Diagnostics");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<VpsSystemDiagnostics>();
        }

        public static void Record(string category, string message)
        {
            AddEntry(category + " | " + message);
        }

        public static void SetState(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string safeValue = Sanitize(value ?? string.Empty);
            lock (Sync)
            {
                if (States.TryGetValue(name, out string previous) && previous == safeValue) return;
                States[name] = safeValue;
                AddEntryLocked("STATE | " + name + " = " + safeValue);
            }
        }

        private void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnUnityLog;
            Record("BOOT", "Diagnostics ready; persistentDataPath=" + Application.persistentDataPath);
            RefreshRuntimeState();
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnUnityLog;
            SetTouchLogging(false);
        }

        private void OnApplicationPause(bool paused)
        {
            Record("APP", "pause=" + paused);
        }

        private void OnApplicationFocus(bool focused)
        {
            Record("APP", "focus=" + focused);
        }

        private void Update()
        {
            RefreshRuntimeState();
            if (!touchLogging) return;

            Mouse mouse = Mouse.current;
            if (!Application.isMobilePlatform && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 position = mouse.position.ReadValue();
                Record("POINTER", "mouse down position=" + Mathf.RoundToInt(position.x) + "," + Mathf.RoundToInt(position.y));
            }
            if (!Application.isMobilePlatform && mouse != null && mouse.leftButton.wasReleasedThisFrame)
            {
                Vector2 position = mouse.position.ReadValue();
                Record("POINTER", "mouse up position=" + Mathf.RoundToInt(position.x) + "," + Mathf.RoundToInt(position.y));
            }
        }

        private void SetTouchLogging(bool value)
        {
            if (touchLogging == value) return;
            touchLogging = value;
            if (value)
            {
                // Raw TouchControl phases persist after release; observe events instead of logging them every frame.
                EnhancedTouchSupport.Enable();
                EnhancedTouch.onFingerDown += OnTouchChanged;
                EnhancedTouch.onFingerUp += OnTouchChanged;
            }
            else
            {
                EnhancedTouch.onFingerDown -= OnTouchChanged;
                EnhancedTouch.onFingerUp -= OnTouchChanged;
                EnhancedTouchSupport.Disable();
            }
        }

        private void OnTouchChanged(Finger finger)
        {
            if (!touchLogging) return;
            EnhancedTouch touch = finger.currentTouch;
            Vector2 position = touch.screenPosition;
            Record("TOUCH", "finger=" + touch.touchId + " phase=" + touch.phase +
                " position=" + Mathf.RoundToInt(position.x) + "," + Mathf.RoundToInt(position.y));
        }

        private void OnGUI()
        {
            if (!panelOpen) return;
            float scale = Mathf.Clamp(Screen.width / 900f, 0.72f, 1.25f);
            Rect safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
            float top = Screen.height - safe.yMax;
            int previousDepth = GUI.depth;
            GUI.depth = -32000;

            float width = Mathf.Min(safe.width - 16f * scale, 760f * scale);
            float height = Mathf.Min(safe.height - 16f * scale, 980f * scale);
            Rect panel = new Rect(safe.x + (safe.width - width) * 0.5f, top + (safe.height - height) * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 10f * scale, panel.y + 8f * scale, panel.width - 20f * scale, panel.height - 16f * scale));
            GUILayout.BeginHorizontal();
            GUILayout.Label("VPS SYSTEM DEBUG", GUILayout.Height(32f * scale));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Đóng", GUILayout.Width(72f * scale), GUILayout.Height(32f * scale)))
            {
                panelOpen = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(BuildSystemSummary());
            GUILayout.Label("Bật Ghi touch, đóng bảng rồi thao tác lỗi; mở lại để xem và sao chép log.");
            GUILayout.BeginHorizontal();
            SetTouchLogging(GUILayout.Toggle(touchLogging, "Ghi touch", GUILayout.Width(120f * scale)));
            if (GUILayout.Button("Sao chép log", GUILayout.Height(34f * scale)))
            {
                GUIUtility.systemCopyBuffer = BuildExport();
                exportStatus = "Đã chép log đã lọc thông tin nhạy cảm.";
            }
            if (GUILayout.Button("Lưu file", GUILayout.Height(34f * scale))) SaveExport();
            if (GUILayout.Button("Xóa", GUILayout.Width(60f * scale), GUILayout.Height(34f * scale))) ClearEntries();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(exportStatus)) GUILayout.Label(exportStatus);

            logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.ExpandHeight(true));
            string[] rows = GetEntries();
            for (int index = rows.Length - 1; index >= 0; index--)
                GUILayout.Label(rows[index], GUILayout.ExpandWidth(true));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.depth = previousDepth;
        }

        private void RefreshRuntimeState()
        {
            int arState = (int)ARSession.state;
            if (arState != lastObservedArState)
            {
                lastObservedArState = arState;
                SetState("ARSession", ((ARSessionState)arState).ToString());
            }
            int notTrackingReason = (int)ARSession.notTrackingReason;
            if (notTrackingReason != lastObservedArNotTrackingReason)
            {
                lastObservedArNotTrackingReason = notTrackingReason;
                SetState("AR not tracking reason", ARSession.notTrackingReason.ToString());
            }
        }

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Warning && type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            string message = Truncate(condition, 4096);
            if (!string.IsNullOrWhiteSpace(stackTrace)) message += "\n" + Truncate(stackTrace, 4096);
            AddEntry(type.ToString().ToUpperInvariant() + " | " + message);
        }

        private static void AddEntry(string message)
        {
            lock (Sync) AddEntryLocked(message);
        }

        private static void AddEntryLocked(string message)
        {
            string safe = Sanitize(message);
            if (safe.Length > MaxEntryLength) safe = safe.Substring(0, MaxEntryLength) + " [truncated]";
            string line = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + " | " + safe;
            while (Entries.Count >= MaxEntries) Entries.Dequeue();
            Entries.Enqueue(line);
        }

        private static string Sanitize(string value)
        {
            string safe = value ?? string.Empty;
            safe = BearerValue.Replace(safe, "Bearer [REDACTED]");
            safe = JwtValue.Replace(safe, "[JWT REDACTED]");
            safe = SecretAssignment.Replace(safe, "$1=[REDACTED]");
            safe = ObjectIdValue.Replace(safe, "[ID]");
            safe = GuidValue.Replace(safe, "[ID]");
            return safe;
        }

        private static string Truncate(string value, int limit)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= limit) return value ?? string.Empty;
            return value.Substring(0, limit) + " [truncated]";
        }

        private string BuildSystemSummary()
        {
            var summary = new StringBuilder();
            summary.AppendLine("Device: " + SystemInfo.deviceModel + " | " + SystemInfo.operatingSystem);
            summary.AppendLine("Unity: " + Application.unityVersion + " | App: " + Application.version + " | Scene: " + SceneManager.GetActiveScene().name);
            summary.AppendLine("Graphics: " + SystemInfo.graphicsDeviceType);
            summary.AppendLine("Memory: system=" + SystemInfo.systemMemorySize + "MB graphics=" + SystemInfo.graphicsMemorySize + "MB allocated=" + (Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)) + "MB");
            summary.AppendLine("Battery: " + (SystemInfo.batteryLevel < 0f ? "unavailable" : (SystemInfo.batteryLevel * 100f).ToString("0") + "%"));
            summary.AppendLine("Display: " + Screen.width + "x" + Screen.height + " @ " + Screen.dpi.ToString("0") + " dpi | SafeArea: " + Screen.safeArea);
            summary.AppendLine("Network: " + Application.internetReachability + " | XR: " + (XRSettings.enabled ? XRSettings.loadedDeviceName : "not loaded"));
#if UNITY_ANDROID && !UNITY_EDITOR
            summary.AppendLine("Permissions: camera=" + UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.CAMERA") +
                " fineLocation=" + UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.ACCESS_FINE_LOCATION"));
#endif
            summary.AppendLine("Sensors: gyroscope=" + SystemInfo.supportsGyroscope);
            Touchscreen touchscreen = Touchscreen.current;
            int activeTouches = 0;
            if (touchscreen != null)
            {
                foreach (TouchControl touch in touchscreen.touches)
                {
                    UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
                    if (phase == UnityEngine.InputSystem.TouchPhase.Began || phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                        phase == UnityEngine.InputSystem.TouchPhase.Stationary) activeTouches++;
                }
            }
            summary.AppendLine("AR: " + ARSession.state + " | Touches: " + activeTouches +
                " | FPS: " + (1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)).ToString("0"));
            Dictionary<string, string> snapshot;
            lock (Sync) snapshot = new Dictionary<string, string>(States);
            foreach (KeyValuePair<string, string> state in snapshot)
                summary.AppendLine(state.Key + ": " + state.Value);
            return summary.ToString();
        }

        private static string[] GetEntries()
        {
            lock (Sync) return Entries.ToArray();
        }

        private string BuildExport()
        {
            var output = new StringBuilder();
            output.AppendLine("=== VPS SYSTEM DIAGNOSTICS ===");
            output.Append(BuildSystemSummary());
            output.AppendLine("=== EVENTS (UTC) ===");
            foreach (string entry in GetEntries()) output.AppendLine(entry);
            return output.ToString();
        }

        private void SaveExport()
        {
            string path = Path.Combine(Application.persistentDataPath, "vps-system-diagnostics.txt");
            try
            {
                File.WriteAllText(path, BuildExport(), new UTF8Encoding(false));
                exportStatus = "Đã lưu log: " + path;
            }
            catch (Exception exception)
            {
                exportStatus = "Lưu log lỗi: " + exception.GetType().Name;
                Record("DIAGNOSTICS", exportStatus);
            }
        }

        private static void ClearEntries()
        {
            lock (Sync) Entries.Clear();
            Record("DIAGNOSTICS", "log buffer cleared");
        }
    }
}
