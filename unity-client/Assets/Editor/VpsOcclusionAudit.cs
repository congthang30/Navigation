using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vps.UnityClient.Editor
{
    /// <summary>
    /// Read-only audit of the configuration that affects AR depth occlusion.
    /// Used to verify assumptions (graphics API, render pipeline, shader availability, companion pivot,
    /// AR subsystem descriptors) without a device, from the editor or from batch mode.
    ///
    /// Usage (batch mode):
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Vps.UnityClient.Editor.VpsOcclusionAudit.RunFullAudit -quit
    /// </summary>
    public static class VpsOcclusionAudit
    {
        /// <summary>Runs every audit section.</summary>
        public static void RunFullAudit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==== VpsOcclusionAudit ====");
            AppendGraphicsAudit(sb);
            AppendShaderAudit(sb);
            AppendModelAudit(sb);
            AppendSubsystemAudit(sb);
            sb.AppendLine("==== END ====");
            Debug.Log(sb.ToString());
        }

        /// <summary>Graphics API, render pipeline and quality settings.</summary>
        public static void RunGraphicsAudit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==== VpsOcclusionAudit (graphics) ====");
            AppendGraphicsAudit(sb);
            sb.AppendLine("==== END ====");
            Debug.Log(sb.ToString());
        }

        /// <summary>Companion model pivot and material/shader information.</summary>
        public static void RunModelAudit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==== VpsOcclusionAudit (model) ====");
            AppendModelAudit(sb);
            sb.AppendLine("==== END ====");
            Debug.Log(sb.ToString());
        }

        /// <summary>AR subsystem descriptor availability before an AR session runs.</summary>
        public static void RunSubsystemAudit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==== VpsOcclusionAudit (subsystems) ====");
            AppendSubsystemAudit(sb);
            sb.AppendLine("==== END ====");
            Debug.Log(sb.ToString());
        }

        private static void AppendGraphicsAudit(StringBuilder sb)
        {
            sb.AppendLine($"Unity version: {Application.unityVersion}");
            sb.AppendLine($"Editor graphics device: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
            sb.AppendLine($"Android graphics APIs (explicit): [{string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Select(a => a.ToString()))}]");
            sb.AppendLine($"Android uses default (Auto) graphics APIs: {PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android)}");
            sb.AppendLine($"iOS graphics APIs: [{string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.iOS).Select(a => a.ToString()))}]");
            sb.AppendLine($"Color space: {PlayerSettings.colorSpace}");
            sb.AppendLine($"Active render pipeline asset: {(GraphicsSettings.defaultRenderPipeline == null ? "Built-in RP" : GraphicsSettings.defaultRenderPipeline.name)}");
            sb.AppendLine($"Quality level: {QualitySettings.GetQualityLevel()} ({QualitySettings.names[QualitySettings.GetQualityLevel()]})");
            sb.AppendLine($"vSyncCount: {QualitySettings.vSyncCount} · targetFrameRate: {Application.targetFrameRate}");
            sb.AppendLine($"Android min SDK: {PlayerSettings.Android.minSdkVersion} · target SDK: {PlayerSettings.Android.targetSdkVersion}");
            sb.AppendLine($"Graphics Jobs enabled: {PlayerSettings.graphicsJobs}");
            sb.AppendLine($"Occlusion stabilization supported on this device: {SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan}");
        }

        private static void AppendShaderAudit(StringBuilder sb)
        {
            sb.AppendLine("--- Shader availability ---");
            foreach (string name in new[]
            {
                "Vps/ARRouteLine",
                "Vps/ARCompanionOcclusion",
                "Vps/AROcclusionStabilizationMesh",
                "Nsdk/ZBufferOcclusion",
                "Nsdk/OcclusionMesh",
                "Nsdk/CopyEyeDepth"
            })
            {
                sb.AppendLine($"  {name}: {(Shader.Find(name) != null ? "FOUND" : "MISSING")}");
            }
        }

        private static void AppendModelAudit(StringBuilder sb)
        {
            sb.AppendLine("--- Companion model ---");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/navigation.prefab");
            if (prefab == null)
            {
                sb.AppendLine("  navigation.prefab MISSING");
                return;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                sb.AppendLine($"  Renderer count: {renderers.Length}");

                bool hasBounds = false;
                Bounds bounds = new Bounds();
                foreach (Renderer r in renderers)
                {
                    if (r == null) continue;
                    if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                    else bounds.Encapsulate(r.bounds);
                }

                if (hasBounds)
                {
                    Vector3 rootPos = instance.transform.position;
                    sb.AppendLine($"  World bounds size: {bounds.size}");
                    sb.AppendLine($"  Bounds min Y relative to root origin: {bounds.min.y - rootPos.y:0.0000}");
                    sb.AppendLine($"  Bounds max Y relative to root origin: {bounds.max.y - rootPos.y:0.0000}");
                    sb.AppendLine("  (min Y must be ~0 so the companion can stand on the floor without sinking)");
                }

                var materials = new System.Collections.Generic.HashSet<Material>();
                foreach (Renderer r in renderers)
                {
                    if (r == null) continue;
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m != null) materials.Add(m);
                    }
                }

                sb.AppendLine($"  Unique materials: {materials.Count}");
                foreach (Material m in materials)
                {
                    sb.AppendLine($"    '{m.name}' shader='{m.shader.name}' queue={m.renderQueue}");
                    sb.AppendLine($"      shader properties: {DescribeShader(m.shader)}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AppendSubsystemAudit(StringBuilder sb)
        {
            sb.AppendLine("--- AR subsystem descriptors (no session running) ---");

            var occlusionDescriptors = new System.Collections.Generic.List<UnityEngine.XR.ARSubsystems.XROcclusionSubsystemDescriptor>();
            UnityEngine.SubsystemManager.GetSubsystemDescriptors(occlusionDescriptors);
            sb.AppendLine($"  XROcclusionSubsystemDescriptor count: {occlusionDescriptors.Count}");
            foreach (UnityEngine.XR.ARSubsystems.XROcclusionSubsystemDescriptor d in occlusionDescriptors)
            {
                sb.AppendLine($"    id='{d.id}' environmentDepthImageSupported={d.environmentDepthImageSupported}");
            }

            var meshDescriptors = new System.Collections.Generic.List<UnityEngine.XR.XRMeshSubsystemDescriptor>();
            UnityEngine.SubsystemManager.GetSubsystemDescriptors(meshDescriptors);
            sb.AppendLine($"  XRMeshSubsystemDescriptor count: {meshDescriptors.Count}");
            foreach (UnityEngine.XR.XRMeshSubsystemDescriptor d in meshDescriptors)
            {
                sb.AppendLine($"    id='{d.id}'");
            }
        }

        private static string DescribeShader(Shader shader)
        {
            if (shader == null) return "MISSING";
            StringBuilder sb = new StringBuilder();
            sb.Append(shader.name).Append(" [");
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(shader.GetPropertyName(i));
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
