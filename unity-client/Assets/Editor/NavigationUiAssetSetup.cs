using System.IO;
using UnityEditor;
using UnityEditor.Build;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

public static class NavigationUiAssetSetup
{
    private const string PanelSettingsPath = "Assets/Resources/UI/NavigationPanelSettings.asset";
    private const string DefaultThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";
    private const string RegularTtfPath = "Assets/Resources/UI/Fonts/Inter-Regular.ttf";
    private const string SemiBoldTtfPath = "Assets/Resources/UI/Fonts/Inter-SemiBold.ttf";
    private const string RegularFontAssetPath = "Assets/Resources/UI/Fonts/Inter-Regular SDF.asset";
    private const string SemiBoldFontAssetPath = "Assets/Resources/UI/Fonts/Inter-SemiBold SDF.asset";

    private const string BrandProductName = "Robot Navigation";
    private const string InAppLogoPath = "Assets/Resources/UI/AppLogo.png";
    private const string AdaptiveBgIconPath = "Assets/Branding/AppIconAdaptiveBg.png";
    private const string AdaptiveFgIconPath = "Assets/Branding/AppIconAdaptiveFg.png";
    private const string RoundIconPath = "Assets/Branding/AppIconRound.png";
    private const string LegacyIconPath = "Assets/Branding/AppIconLegacy.png";

    [MenuItem("Tools/Navigation UI/Apply App Branding")]
    public static void ApplyAppBranding()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        ConfigureBrandingTexture(InAppLogoPath, alphaIsTransparency: true);
        ConfigureBrandingTexture(AdaptiveBgIconPath, alphaIsTransparency: false);
        ConfigureBrandingTexture(AdaptiveFgIconPath, alphaIsTransparency: true);
        ConfigureBrandingTexture(RoundIconPath, alphaIsTransparency: true);
        ConfigureBrandingTexture(LegacyIconPath, alphaIsTransparency: true);

        PlayerSettings.productName = BrandProductName;
        PlayerSettings.SplashScreen.backgroundColor = new Color(0.957f, 0.969f, 0.984f, 1f);

        var bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveBgIconPath);
        var fgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveFgIconPath);
        var roundTex = AssetDatabase.LoadAssetAtPath<Texture2D>(RoundIconPath);
        var legacyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(LegacyIconPath);

        if (legacyTex != null)
        {
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { legacyTex }, IconKind.Application);
        }

#if UNITY_ANDROID
        AssignAndroidIcons(bgTex, fgTex, roundTex, legacyTex);
#else
        Debug.LogWarning("[NavigationUiAssetSetup] Active build target is not Android; Android platform icons were skipped.");
#endif

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[NavigationUiAssetSetup] Applied branding '{BrandProductName}' and Android icons.");
    }

#if UNITY_ANDROID
    private static void AssignAndroidIcons(Texture2D bgTex, Texture2D fgTex, Texture2D roundTex, Texture2D legacyTex)
    {
        if (bgTex != null && fgTex != null)
        {
            var adaptiveIcons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive);
            for (int i = 0; i < adaptiveIcons.Length; i++)
            {
                adaptiveIcons[i].SetTextures(bgTex, fgTex);
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive, adaptiveIcons);
        }

        if (roundTex != null)
        {
            var roundIcons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Round);
            for (int i = 0; i < roundIcons.Length; i++)
            {
                roundIcons[i].SetTextures(roundTex);
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Round, roundIcons);
        }

        if (legacyTex != null)
        {
            var legacyIcons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Legacy);
            for (int i = 0; i < legacyIcons.Length; i++)
            {
                legacyIcons[i].SetTextures(legacyTex);
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Legacy, legacyIcons);
        }
    }
#endif

    private static void ConfigureBrandingTexture(string assetPath, bool alphaIsTransparency)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[NavigationUiAssetSetup] TextureImporter not found for {assetPath}");
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = alphaIsTransparency;
        importer.mipmapEnabled = false;
        importer.isReadable = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    [MenuItem("Tools/Navigation UI/Refresh UI Toolkit Assets")]
    public static void EnsureAssets()
    {
        CreateOrUpdateFontAsset(RegularTtfPath, RegularFontAssetPath);
        CreateOrUpdateFontAsset(SemiBoldTtfPath, SemiBoldFontAssetPath);

        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (panel == null)
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, PanelSettingsPath);
        }

        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(390, 844);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = 0.5f;

        var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(DefaultThemePath);
        if (theme != null)
        {
            panel.themeStyleSheet = theme;
        }

        EditorUtility.SetDirty(panel);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[NavigationUiAssetSetup] Navigation UI assets verified.");
    }

    private static void CreateOrUpdateFontAsset(string ttfPath, string assetPath)
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null)
        {
            Debug.LogWarning($"[NavigationUiAssetSetup] Missing font at {ttfPath}");
            return;
        }

        var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
        if (existing != null)
        {
            return;
        }

        var fontAsset = FontAsset.CreateFontAsset(font);
        if (fontAsset == null)
        {
            Debug.LogWarning($"[NavigationUiAssetSetup] Could not create FontAsset from {ttfPath}");
            return;
        }

        fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fontAsset.isMultiAtlasTexturesEnabled = true;

        AssetDatabase.CreateAsset(fontAsset, assetPath);
        if (fontAsset.atlasTexture != null)
        {
            fontAsset.atlasTexture.name = fontAsset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
        }

        if (fontAsset.material != null)
        {
            fontAsset.material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        EditorUtility.SetDirty(fontAsset);
    }
}
