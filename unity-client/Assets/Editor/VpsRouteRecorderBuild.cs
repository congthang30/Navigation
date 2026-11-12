using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Vps.UnityClient.Editor
{
    public static class VpsRouteRecorderBuild
    {
        public static void BuildAndroid()
        {
            EnsureRequiredShadersIncluded();
            EnsureNavigationModelPrefab();
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes are configured in EditorBuildSettings.");

            var outputPath = Environment.GetEnvironmentVariable("VPS_ANDROID_APK");
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = "/tmp/vps-route-recorder.apk";

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            };

            var previousHttpOption = PlayerSettings.insecureHttpOption;
            try
            {
                PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Android build failed: " + report.SummarizeErrors());

                try
                {
                    string altPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Builds/vps-standalone-navigation.apk"));
                    string dir = System.IO.Path.GetDirectoryName(altPath);
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    if (outputPath != altPath && System.IO.File.Exists(outputPath))
                    {
                        System.IO.File.Copy(outputPath, altPath, true);
                    }
                }
                catch (Exception) {}

                Debug.Log("Route recorder Android build succeeded: " + outputPath);
            }
            finally
            {
                PlayerSettings.insecureHttpOption = previousHttpOption;
            }
        }

        private static void EnsureRequiredShadersIncluded()
        {
            string[] shaderNames =
            {
                "Vps/ARRouteLine",
                "Vps/AROcclusionStabilizationMesh",
                "Nsdk/ZBufferOcclusion",
                "Nsdk/OcclusionMesh",
                "Nsdk/CopyEyeDepth"
            };

            var graphicsSettingsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (graphicsSettingsObj == null) return;

            var serializedObject = new SerializedObject(graphicsSettingsObj);
            var arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");
            if (arrayProp == null) return;

            bool modified = false;
            foreach (string shaderName in shaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null) continue;

                bool exists = false;
                for (int i = 0; i < arrayProp.arraySize; i++)
                {
                    if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    int index = arrayProp.arraySize;
                    arrayProp.InsertArrayElementAtIndex(index);
                    arrayProp.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                    modified = true;
                }
            }

            if (modified)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
        }

        private static void EnsureNavigationModelPrefab()
        {
            string fbxPath = "Assets/Resources/navigation.fbx";
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer != null)
            {
                bool dirty = false;
                if (importer.importAnimation)
                {
                    importer.importAnimation = false;
                    dirty = true;
                }
                if (importer.animationType != ModelImporterAnimationType.None)
                {
                    importer.animationType = ModelImporterAnimationType.None;
                    dirty = true;
                }
                if (dirty)
                {
                    importer.SaveAndReimport();
                }
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model != null)
            {
                string prefabPath = "Assets/Resources/navigation.prefab";
                GameObject instance = UnityEngine.Object.Instantiate(model);
                try
                {
                    foreach (var cam in instance.GetComponentsInChildren<Camera>(true))
                        UnityEngine.Object.DestroyImmediate(cam);
                    foreach (var l in instance.GetComponentsInChildren<Light>(true))
                        UnityEngine.Object.DestroyImmediate(l);

                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                    Debug.Log("[Build] Successfully saved " + prefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
    }
}
