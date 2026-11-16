#if UNITY_EDITOR && UNITY_ANDROID
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;

namespace Vps.UnityClient.Editor
{
    /// <summary>
    /// Ensures the exported Android Gradle manifest declares package-visibility queries for
    /// Text-to-Speech services and Google Speech Services (required on Android 11+ / API 30+).
    /// </summary>
    public sealed class AndroidTtsManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        private const string TtsServiceAction = "android.intent.action.TTS_SERVICE";
        private const string GoogleTtsPackage = "com.google.android.tts";
        private static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 999;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            EnsureTtsQueriesInManifest(manifestPath);
        }

        public static void EnsureTtsQueriesInManifest(string manifestPath)
        {
            if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath)) return;

            XDocument doc = XDocument.Load(manifestPath);
            XElement manifest = doc.Root;
            if (manifest == null || manifest.Name.LocalName != "manifest") return;

            XElement queries = manifest.Elements("queries").FirstOrDefault();
            if (queries == null)
            {
                queries = new XElement("queries");
                manifest.Add(queries);
            }

            bool hasTtsIntent = queries.Elements("intent")
                .SelectMany(intent => intent.Elements("action"))
                .Any(action => string.Equals(
                    (string)action.Attribute(AndroidNs + "name"),
                    TtsServiceAction,
                    System.StringComparison.Ordinal));
            if (!hasTtsIntent)
            {
                queries.Add(new XElement(
                    "intent",
                    new XElement("action", new XAttribute(AndroidNs + "name", TtsServiceAction))));
            }

            bool hasGoogleTtsPkg = queries.Elements("package")
                .Any(pkg => string.Equals(
                    (string)pkg.Attribute(AndroidNs + "name"),
                    GoogleTtsPackage,
                    System.StringComparison.Ordinal));
            if (!hasGoogleTtsPkg)
            {
                queries.Add(new XElement(
                    "package",
                    new XAttribute(AndroidNs + "name", GoogleTtsPackage)));
            }

            doc.Save(manifestPath);
        }
    }
}
#endif
