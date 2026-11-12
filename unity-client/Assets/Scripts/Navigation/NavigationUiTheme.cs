using System;
using UnityEngine;

namespace Vps.UnityClient.Navigation
{
    /// <summary>Shared semantic colors for navigation and route-authoring surfaces.</summary>
    public static class NavigationUiPalette
    {
        public static readonly Color BrandBlue = new Color(0.31f, 0.49f, 0.96f, 1.00f);
        public static readonly Color BrandBlueHover = new Color(0.39f, 0.55f, 0.98f, 1.00f);
        public static readonly Color BrandBluePressed = new Color(0.24f, 0.41f, 0.91f, 1.00f);
        public static readonly Color BrandBlueBorder = new Color(0.58f, 0.69f, 1.00f, 0.95f);
        public static readonly Color Danger = new Color(0.88f, 0.16f, 0.22f, 0.95f);
        public static readonly Color DangerHover = new Color(0.94f, 0.24f, 0.30f, 1.00f);
        public static readonly Color DangerPressed = new Color(0.68f, 0.10f, 0.15f, 1.00f);
        public static readonly Color DangerBorder = new Color(1.00f, 0.35f, 0.40f, 0.80f);
        public static readonly Color DangerPressedBorder = new Color(1.00f, 0.25f, 0.30f, 0.90f);
        public static readonly Color Success = new Color(0.08f, 0.58f, 0.37f, 1.00f);
        public static readonly Color SuccessSurfaceLight = new Color(0.89f, 0.97f, 0.92f, 1.00f);
        public static readonly Color SuccessSurfaceDark = new Color(0.08f, 0.24f, 0.20f, 0.95f);
        public static readonly Color SuccessTextLight = new Color(0.07f, 0.43f, 0.26f, 1.00f);
        public static readonly Color SuccessTextDark = new Color(0.68f, 1.00f, 0.79f, 1.00f);
        public static readonly Color WarningText = new Color(1.00f, 0.84f, 0.49f, 1.00f);
        public static readonly Color DarkGlass = new Color(0.025f, 0.045f, 0.085f, 0.92f);
        public static readonly Color DarkCard = new Color(0.035f, 0.060f, 0.110f, 0.975f);
        public static readonly Color DarkPill = new Color(0.070f, 0.105f, 0.170f, 0.96f);
        public static readonly Color DarkInset = new Color(0.045f, 0.075f, 0.125f, 0.98f);
        public static readonly Color DarkBorder = new Color(0.24f, 0.34f, 0.47f, 0.70f);
        public static readonly Color DarkSecondary = new Color(0.095f, 0.145f, 0.225f, 1.00f);
        public static readonly Color DarkSecondaryHover = new Color(0.12f, 0.19f, 0.29f, 0.98f);
        public static readonly Color DarkSecondaryPressed = new Color(0.16f, 0.25f, 0.37f, 1.00f);
        public static readonly Color LightSurface = new Color(0.985f, 0.992f, 1.000f, 1.00f);
        public static readonly Color LightSurfaceMuted = new Color(0.94f, 0.96f, 0.99f, 1.00f);
        public static readonly Color LightSurfaceHover = new Color(0.89f, 0.93f, 1.00f, 1.00f);
        public static readonly Color LightSurfacePressed = new Color(0.84f, 0.89f, 0.98f, 1.00f);
        public static readonly Color LightBorder = new Color(0.79f, 0.84f, 0.91f, 1.00f);
        public static readonly Color TextOnDark = new Color(0.98f, 0.99f, 1.00f, 1.00f);
        public static readonly Color MutedOnDark = new Color(0.70f, 0.77f, 0.86f, 1.00f);
        public static readonly Color TextOnLight = new Color(0.09f, 0.13f, 0.21f, 1.00f);
        public static readonly Color BodyOnLight = new Color(0.22f, 0.28f, 0.38f, 1.00f);
        public static readonly Color MutedOnLight = new Color(0.36f, 0.42f, 0.52f, 1.00f);
    }

    /// <summary>
    /// Premium mobile UI theme and procedural vector icon generator for AR navigation.
    /// Provides anti-aliased 9-slice glassmorphism surfaces and crisp vector icons.
    /// Eliminates all broken unicode emojis and zero per-frame garbage collection.
    /// </summary>
    public sealed class NavigationUiTheme
    {
        // 9-slice card and button styles
        public GUIStyle TopBannerStyle { get; private set; }
        public GUIStyle BottomPillStyle { get; private set; }
        public GUIStyle ModalCardStyle { get; private set; }
        public GUIStyle PrimaryButtonStyle { get; private set; }
        public GUIStyle SecondaryButtonStyle { get; private set; }
        public GUIStyle DangerButtonStyle { get; private set; }
        public GUIStyle IconButtonStyle { get; private set; }
        public GUIStyle CardNormalStyle { get; private set; }
        public GUIStyle CardSelectedStyle { get; private set; }
        public GUIStyle InsetPanelStyle { get; private set; }
        public GUIStyle SearchFieldStyle { get; private set; }
        public GUIStyle LauncherCardStyle { get; private set; }
        public GUIStyle LauncherPrimaryCardStyle { get; private set; }
        public GUIStyle LightModalCardStyle { get; private set; }
        public GUIStyle LightInsetPanelStyle { get; private set; }
        public GUIStyle LightSearchFieldStyle { get; private set; }
        public GUIStyle LightIconButtonStyle { get; private set; }
        public GUIStyle LightSecondaryButtonStyle { get; private set; }
        public GUIStyle LightSliderStyle { get; private set; }
        public GUIStyle LightSliderThumbStyle { get; private set; }
        public GUIStyle LightCardNormalStyle { get; private set; }
        public GUIStyle LightCardSelectedStyle { get; private set; }
        public GUIStyle WhitePillBarStyle { get; private set; }
        public GUIStyle ReadinessMintCardStyle { get; private set; }
        public GUIStyle LightDockCardStyle { get; private set; }
        public GUIStyle PrimaryPillButtonStyle { get; private set; }
        public GUIStyle DangerPillButtonStyle { get; private set; }
        public GUIStyle HeaderBreadcrumbStyle { get; private set; }
        public GUIStyle HeaderSubtitleStyle { get; private set; }
        public GUIStyle PillButtonTextStyle { get; private set; }
        public GUIStyle PoiCardNormalStyle { get; private set; }
        public GUIStyle PoiCardSelectedStyle { get; private set; }

        // Typography styles
        public GUIStyle TitleStyle { get; private set; }
        public GUIStyle SubtitleStyle { get; private set; }
        public GUIStyle BadgeStyle { get; private set; }
        public GUIStyle CompanionTextStyle { get; private set; }
        public GUIStyle ButtonTextStyle { get; private set; }
        public GUIStyle CardTitleStyle { get; private set; }
        public GUIStyle CardSubtitleStyle { get; private set; }
        public GUIStyle EyebrowStyle { get; private set; }
        public GUIStyle BodyStyle { get; private set; }
        public GUIStyle CaptionStyle { get; private set; }
        public GUIStyle MetricStyle { get; private set; }
        public GUIStyle LauncherBrandArStyle { get; private set; }
        public float LauncherBrandUnityWidth { get; private set; }
        public GUIStyle LauncherBrandClipStyle { get; private set; }
        public GUIStyle LauncherCardTitleStyle { get; private set; }
        public GUIStyle LauncherCardSubtitleStyle { get; private set; }
        public GUIStyle LauncherPrimaryTitleStyle { get; private set; }
        public GUIStyle LauncherPrimarySubtitleStyle { get; private set; }
        public GUIStyle LightTitleStyle { get; private set; }
        public GUIStyle LightSubtitleStyle { get; private set; }
        public GUIStyle LightBodyStyle { get; private set; }
        public GUIStyle LightCaptionStyle { get; private set; }
        public GUIStyle LightEyebrowStyle { get; private set; }
        public GUIStyle LightCardTitleStyle { get; private set; }
        public GUIStyle LightCardSubtitleStyle { get; private set; }
        public GUIStyle LightButtonTextStyle { get; private set; }
        public GUIStyle LightDangerStyle { get; private set; }

        // Crisp procedural vector icon textures
        public Texture2D IconPin { get; private set; }
        public Texture2D IconClose { get; private set; }
        public Texture2D IconGear { get; private set; }
        public Texture2D IconArrow { get; private set; }
        public Texture2D IconBack { get; private set; }
        public Texture2D IconDoor { get; private set; }
        public Texture2D IconCheck { get; private set; }
        public Texture2D IconSuccess { get; private set; }
        public Texture2D IconCompanion { get; private set; }
        public Texture2D IconSearch { get; private set; }
        public Texture2D IconArProject { get; private set; }
        public Texture2D IconVpsMap { get; private set; }
        public Texture2D IconWalk { get; private set; }
        public Texture2D IconLock { get; private set; }
        public Texture2D SheetHandle { get; private set; }
        public Texture2D LauncherBackground { get; private set; }
        public Texture2D IconNavArrow { get; private set; }
        public Texture2D IconRadioSelected { get; private set; }
        public Texture2D IconRadioUnselected { get; private set; }
        public Texture2D IconSuccessBadge { get; private set; }
        public Texture2D IconBoxBlue { get; private set; }
        public Texture2D IconBackDark { get; private set; }
        public Texture2D IconCloseDark { get; private set; }
        public Texture2D BottomGradientTexture { get; private set; }

        private Font uiFont;
        private readonly GUIContent measureContent = new GUIContent();
        private bool initialized;

        public void Initialize()
        {
            if (initialized) return;

            uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Modern color palette (Slate, Cyan, Rose)
            Color darkGlass = NavigationUiPalette.DarkGlass;
            Color darkCard = NavigationUiPalette.DarkCard;
            Color pillDark = NavigationUiPalette.DarkPill;
            Color borderDark = NavigationUiPalette.DarkBorder;
            Color borderCyan = NavigationUiPalette.BrandBlueBorder;
            Color primaryBlue = NavigationUiPalette.BrandBlue;
            Color primaryActive = NavigationUiPalette.BrandBluePressed;
            Color dangerRed = NavigationUiPalette.Danger;
            Color dangerActive = NavigationUiPalette.DangerPressed;
            Color textWhite = NavigationUiPalette.TextOnDark;
            Color textCyan = new Color(0.36f, 0.86f, 1.00f, 1.00f);
            Color textMuted = NavigationUiPalette.MutedOnDark;
            Color textQuiet = new Color(0.54f, 0.63f, 0.75f, 1.00f);

            // Generate crisp vector icons
            Color brandBlue = NavigationUiPalette.BrandBlue;
            Color ink = NavigationUiPalette.TextOnLight;
            Color inkMuted = NavigationUiPalette.MutedOnLight;
            Color lightBorder = NavigationUiPalette.LightBorder;
            IconPin = GeneratePinIcon(48, textCyan);
            IconClose = GenerateCloseIcon(48, textWhite);
            IconGear = GenerateGearIcon(48, textWhite);
            IconArrow = GenerateArrowIcon(48, textWhite);
            IconBack = GenerateBackIcon(48, textWhite);
            IconDoor = GenerateDoorIcon(48, textCyan);
            IconCheck = GenerateCheckIcon(48, textCyan);
            IconSuccess = GenerateCheckIcon(48, NavigationUiPalette.Success);
            IconCompanion = GenerateCompanionIcon(48);
            IconSearch = GenerateSearchIcon(48, textCyan);
            IconArProject = GenerateArProjectIcon(64, brandBlue);
            IconVpsMap = GenerateVpsMapIcon(64, brandBlue);
            IconWalk = GenerateWalkIcon(64, Color.white);
            IconLock = GenerateLockIcon(48, brandBlue);
            SheetHandle = GenerateHandleTexture(64, 10, textMuted);
            LauncherBackground = GenerateLauncherGradientTexture(
                new Color(0.985f, 0.992f, 1.000f, 1.0f),
                new Color(0.910f, 0.944f, 0.990f, 1.0f)
            );
            IconBackDark = GenerateBackIcon(48, new Color(0.12f, 0.16f, 0.23f, 1.0f));
            IconCloseDark = GenerateCloseIcon(48, new Color(0.28f, 0.34f, 0.42f, 1.0f));
            IconNavArrow = GenerateNavArrowIcon(48, Color.white);
            IconRadioSelected = GenerateRadioSelected(48, new Color(0.11f, 0.42f, 0.96f, 1.0f), Color.white);
            IconRadioUnselected = GenerateRadioUnselected(48, new Color(0.79f, 0.83f, 0.88f, 1.0f), 2.5f);
            IconSuccessBadge = GenerateRadioSelected(48, new Color(0.13f, 0.77f, 0.40f, 1.0f), Color.white);
            IconBoxBlue = CreateRoundedTexture(64, 64, 14, new Color(0.88f, 0.93f, 0.99f, 1.0f), new Color(0.78f, 0.86f, 0.97f, 0.6f), 1.0f);
            BottomGradientTexture = GenerateBottomGradientTexture(32, 128, new Color(0.94f, 0.97f, 1.0f, 0.92f));

            Color pureWhite = new Color(1f, 1f, 1f, 0.96f);
            Color solidWhite = Color.white;
            Color cardBorder = new Color(0.88f, 0.91f, 0.95f, 0.9f);
            Color mintFill = new Color(0.92f, 0.98f, 0.94f, 0.96f);
            Color mintBorder = new Color(0.76f, 0.93f, 0.82f, 0.9f);
            Color searchFill = new Color(0.945f, 0.960f, 0.976f, 1.0f);
            Color poiNormFill = Color.white;
            Color poiNormBorder = new Color(0.88f, 0.91f, 0.95f, 1.0f);
            Color poiSelFill = new Color(0.935f, 0.965f, 1.0f, 1.0f);
            Color poiSelBorder = new Color(0.20f, 0.48f, 0.96f, 1.0f);
            Color royalBlue = new Color(0.11f, 0.42f, 0.96f, 1.0f);
            Color royalBlueHover = new Color(0.16f, 0.47f, 1.0f, 1.0f);
            Color royalBluePressed = new Color(0.08f, 0.35f, 0.86f, 1.0f);
            Color coralRed = new Color(0.95f, 0.22f, 0.31f, 1.0f);
            Color coralRedHover = new Color(0.98f, 0.28f, 0.37f, 1.0f);
            Color coralRedPressed = new Color(0.82f, 0.16f, 0.24f, 1.0f);

            Texture2D texWhiteBar = CreateRoundedTexture(64, 64, 22, pureWhite, cardBorder, 1.0f);
            Texture2D texMintCard = CreateRoundedTexture(64, 64, 20, mintFill, mintBorder, 1.2f);
            Texture2D texDockCard = CreateRoundedTexture(64, 64, 26, solidWhite, cardBorder, 1.2f);
            Texture2D texSearchPill = CreateRoundedTexture(64, 64, 24, searchFill, cardBorder, 1.0f);
            Texture2D texPoiNorm = CreateRoundedTexture(64, 64, 18, poiNormFill, poiNormBorder, 1.0f);
            Texture2D texPoiSel = CreateRoundedTexture(64, 64, 18, poiSelFill, poiSelBorder, 1.8f);
            Texture2D texPillBlue = CreateRoundedTexture(64, 64, 28, royalBlue, new Color(0.35f, 0.60f, 1.0f, 0.8f), 1.0f);
            Texture2D texPillBlueHover = CreateRoundedTexture(64, 64, 28, royalBlueHover, new Color(0.45f, 0.70f, 1.0f, 0.9f), 1.0f);
            Texture2D texPillBluePressed = CreateRoundedTexture(64, 64, 28, royalBluePressed, royalBlue, 1.2f);
            Texture2D texPillRed = CreateRoundedTexture(64, 64, 28, coralRed, new Color(1.0f, 0.45f, 0.52f, 0.8f), 1.0f);
            Texture2D texPillRedHover = CreateRoundedTexture(64, 64, 28, coralRedHover, new Color(1.0f, 0.55f, 0.62f, 0.9f), 1.0f);
            Texture2D texPillRedPressed = CreateRoundedTexture(64, 64, 28, coralRedPressed, coralRed, 1.2f);
            Texture2D texCircleClose = CreateRoundedTexture(64, 64, 31, searchFill, cardBorder, 1.0f);

            // 9-slice rounded textures (64x64 with 18-28px radius)
            Texture2D texBanner = CreateRoundedTexture(64, 64, 20, darkGlass, borderDark, 1.5f);
            Texture2D texPill = CreateRoundedTexture(64, 64, 28, pillDark, borderDark, 1.5f);
            Texture2D texModal = CreateRoundedTexture(64, 64, 24, darkCard, borderDark, 1.5f);
            Texture2D texPrimary = CreateRoundedTexture(64, 64, 22, primaryBlue, borderCyan, 1.0f);
            Texture2D texPrimaryHover = CreateRoundedTexture(64, 64, 22, NavigationUiPalette.BrandBlueHover, borderCyan, 1.0f);
            Texture2D texPrimaryPressed = CreateRoundedTexture(64, 64, 22, primaryActive, borderCyan, 1.5f);
            Texture2D texDanger = CreateRoundedTexture(64, 64, 20, dangerRed, NavigationUiPalette.DangerBorder, 1.0f);
            Texture2D texDangerHover = CreateRoundedTexture(64, 64, 20, NavigationUiPalette.DangerHover, NavigationUiPalette.DangerBorder, 1.0f);
            Texture2D texDangerPressed = CreateRoundedTexture(64, 64, 20, dangerActive, NavigationUiPalette.DangerPressedBorder, 1.0f);
            Texture2D texCardNorm = CreateRoundedTexture(64, 64, 16, pillDark, borderDark, 1.2f);
            Texture2D texSecondary = CreateRoundedTexture(64, 64, 16, NavigationUiPalette.DarkSecondary, borderDark, 1.2f);
            Texture2D texSecondaryHover = CreateRoundedTexture(64, 64, 16, NavigationUiPalette.DarkSecondaryHover, borderDark, 1.2f);
            Texture2D texSecondaryPressed = CreateRoundedTexture(64, 64, 16, NavigationUiPalette.DarkSecondaryPressed, borderDark, 1.2f);
            Texture2D texCardSel = CreateRoundedTexture(64, 64, 16, brandBlue, borderCyan, 2.0f);
            Texture2D texCircle = CreateRoundedTexture(64, 64, 31, darkGlass, borderDark, 1.5f);
            Texture2D texInset = CreateRoundedTexture(64, 64, 14, NavigationUiPalette.DarkInset, new Color(0.22f, 0.32f, 0.45f, 0.55f), 1.0f);
            Texture2D texLauncherCard = CreateRoundedTexture(64, 64, 20, NavigationUiPalette.LightSurface, lightBorder, 1.2f);
            Texture2D texLauncherCardActive = CreateRoundedTexture(64, 64, 20, NavigationUiPalette.LightSurfaceHover, brandBlue, 1.8f);
            Texture2D texLauncherPrimary = CreateRoundedTexture(64, 64, 20, brandBlue, new Color(0.39f, 0.55f, 0.98f, 1f), 1.0f);
            Texture2D texLauncherPrimaryActive = CreateRoundedTexture(64, 64, 20, NavigationUiPalette.BrandBluePressed, NavigationUiPalette.BrandBlueHover, 1.0f);
            Texture2D texLightModal = CreateRoundedTexture(64, 64, 24, NavigationUiPalette.LightSurface, lightBorder, 1.0f);
            Texture2D texLightInset = CreateRoundedTexture(64, 64, 14, NavigationUiPalette.LightSurfaceMuted, lightBorder, 0.8f);
            Texture2D texLightCircle = CreateRoundedTexture(64, 64, 31, NavigationUiPalette.LightSurface, lightBorder, 1.0f);
            Texture2D texLightCard = CreateRoundedTexture(64, 64, 16, NavigationUiPalette.LightSurface, lightBorder, 1.0f);
            Texture2D texLightCardPressed = CreateRoundedTexture(64, 64, 16, NavigationUiPalette.LightSurfacePressed, lightBorder, 1.0f);
            Texture2D texLightCardSelected = CreateRoundedTexture(64, 64, 16, NavigationUiPalette.LightSurfaceHover, brandBlue, 1.8f);
            Texture2D texLightSliderTrack = CreateRoundedTexture(64, 16, 8, NavigationUiPalette.LightSurfacePressed, lightBorder, 1.0f);
            Texture2D texLightSliderThumb = CreateRoundedTexture(32, 32, 16, brandBlue, Color.white, 2.0f);

            var border16 = new RectOffset(16, 16, 16, 16);
            var border22 = new RectOffset(22, 22, 22, 22);

            TopBannerStyle = new GUIStyle
            {
                normal = { background = texBanner },
                border = border22,
                padding = new RectOffset(22, 22, 14, 14)
            };

            BottomPillStyle = new GUIStyle
            {
                normal = { background = texPill },
                border = new RectOffset(26, 26, 26, 26),
                padding = new RectOffset(24, 24, 12, 12),
                alignment = TextAnchor.MiddleCenter
            };

            ModalCardStyle = new GUIStyle
            {
                normal = { background = texModal },
                border = border22,
                padding = new RectOffset(28, 28, 24, 24)
            };

            PrimaryButtonStyle = new GUIStyle
            {
                normal = { background = texPrimary, textColor = textWhite },
                hover = { background = texPrimaryHover, textColor = textWhite },
                active = { background = texPrimaryPressed, textColor = textWhite },
                focused = { background = texPrimary, textColor = textWhite },
                border = border22,
                font = uiFont,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            SecondaryButtonStyle = new GUIStyle
            {
                normal = { background = texSecondary, textColor = textWhite },
                hover = { background = texSecondaryHover, textColor = textWhite },
                active = { background = texSecondaryPressed, textColor = textWhite },
                focused = { background = texSecondary, textColor = textWhite },
                border = border16,
                font = uiFont,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            DangerButtonStyle = new GUIStyle
            {
                normal = { background = texDanger, textColor = textWhite },
                hover = { background = texDangerHover, textColor = textWhite },
                active = { background = texDangerPressed, textColor = textWhite },
                focused = { background = texDanger, textColor = textWhite },
                border = new RectOffset(20, 20, 20, 20),
                font = uiFont,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            IconButtonStyle = new GUIStyle
            {
                normal = { background = texCircle, textColor = textWhite },
                active = { background = texPrimaryPressed, textColor = textWhite },
                border = new RectOffset(30, 30, 30, 30),
                alignment = TextAnchor.MiddleCenter
            };

            CardNormalStyle = new GUIStyle
            {
                normal = { background = texCardNorm, textColor = textWhite },
                border = border16,
                padding = new RectOffset(20, 20, 14, 14),
                font = uiFont,
                fontSize = 22,
                alignment = TextAnchor.MiddleLeft
            };

            CardSelectedStyle = new GUIStyle
            {
                normal = { background = texCardSel, textColor = textWhite },
                border = border16,
                padding = new RectOffset(20, 20, 14, 14),
                font = uiFont,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            InsetPanelStyle = new GUIStyle
            {
                normal = { background = texInset },
                border = border16,
                padding = new RectOffset(18, 18, 14, 14)
            };

            SearchFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                normal = { background = texInset, textColor = textWhite },
                focused = { background = texCardNorm, textColor = textWhite },
                active = { background = texCardNorm, textColor = textWhite },
                border = border16,
                padding = new RectOffset(48, 16, 8, 8),
                font = uiFont,
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft
            };

            TitleStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 27,
                fontStyle = FontStyle.Bold,
                normal = { textColor = textWhite },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };

            SubtitleStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 17,
                normal = { textColor = textMuted },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };

            BadgeStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = textCyan },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };

            CompanionTextStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = textWhite },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };

            ButtonTextStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = textWhite },
                alignment = TextAnchor.MiddleCenter
            };

            CardTitleStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = textWhite },
                alignment = TextAnchor.MiddleLeft
            };

            CardSubtitleStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 15,
                normal = { textColor = textMuted },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };

            EyebrowStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = textCyan },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };

            BodyStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 18,
                normal = { textColor = textWhite },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };

            CaptionStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 14,
                normal = { textColor = textQuiet },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };

            MetricStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 29,
                fontStyle = FontStyle.Bold,
                normal = { textColor = textWhite },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };

            LauncherCardStyle = new GUIStyle
            {
                normal = { background = texLauncherCard, textColor = ink },
                hover = { background = texLauncherCardActive, textColor = ink },
                active = { background = texLauncherCardActive, textColor = ink },
                border = border22,
                padding = new RectOffset(18, 18, 14, 14),
                alignment = TextAnchor.MiddleLeft
            };

            LauncherPrimaryCardStyle = new GUIStyle
            {
                normal = { background = texLauncherPrimary, textColor = textWhite },
                hover = { background = texLauncherPrimaryActive, textColor = textWhite },
                active = { background = texLauncherPrimaryActive, textColor = textWhite },
                border = border22,
                padding = new RectOffset(18, 18, 14, 14),
                alignment = TextAnchor.MiddleLeft
            };

            LightModalCardStyle = new GUIStyle
            {
                normal = { background = texLightModal },
                border = border22,
                padding = new RectOffset(24, 24, 22, 22)
            };

            LightInsetPanelStyle = new GUIStyle
            {
                normal = { background = texLightInset },
                border = border16,
                padding = new RectOffset(16, 16, 12, 12)
            };

            LightIconButtonStyle = new GUIStyle
            {
                normal = { background = texLightCircle, textColor = ink },
                hover = { background = texLightCardSelected, textColor = ink },
                active = { background = texLightCardSelected, textColor = ink },
                border = new RectOffset(30, 30, 30, 30),
                alignment = TextAnchor.MiddleCenter
            };

            LightCardNormalStyle = new GUIStyle
            {
                normal = { background = texLightCard, textColor = ink },
                hover = { background = texLightCardSelected, textColor = ink },
                active = { background = texLightCardSelected, textColor = ink },
                border = border16,
                padding = new RectOffset(18, 18, 12, 12),
                alignment = TextAnchor.MiddleLeft
            };

            LightCardSelectedStyle = new GUIStyle
            {
                normal = { background = texLightCardSelected, textColor = ink },
                hover = { background = texLightCardSelected, textColor = ink },
                active = { background = texLightCardSelected, textColor = ink },
                border = border16,
                padding = new RectOffset(18, 18, 12, 12),
                alignment = TextAnchor.MiddleLeft
            };

            LightSecondaryButtonStyle = new GUIStyle(LightCardNormalStyle)
            {
                font = uiFont,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            LightSecondaryButtonStyle.hover.background = texLightCardSelected;
            LightSecondaryButtonStyle.active.background = texLightCardPressed;
            LightSecondaryButtonStyle.focused.background = texLightCardSelected;

            LauncherBrandArStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                normal = { textColor = ink },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            measureContent.text = "Unity";
            LauncherBrandUnityWidth = LauncherBrandArStyle.CalcSize(measureContent).x;

            LauncherBrandClipStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.24f, 0.42f, 0.86f, 1.0f) },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };

            LauncherCardTitleStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = ink },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };

            LauncherCardSubtitleStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 15,
                fontStyle = FontStyle.Normal,
                normal = { textColor = inkMuted },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };

            LauncherPrimaryTitleStyle = new GUIStyle(LauncherCardTitleStyle);
            LauncherPrimaryTitleStyle.normal.textColor = textWhite;
            LauncherPrimarySubtitleStyle = new GUIStyle(LauncherCardSubtitleStyle);
            LauncherPrimarySubtitleStyle.normal.textColor = new Color(0.90f, 0.93f, 1f, 1f);

            LightTitleStyle = new GUIStyle(TitleStyle);
            LightTitleStyle.normal.textColor = ink;
            LightSubtitleStyle = new GUIStyle(SubtitleStyle);
            LightSubtitleStyle.normal.textColor = inkMuted;
            LightBodyStyle = new GUIStyle(BodyStyle);
            LightBodyStyle.normal.textColor = NavigationUiPalette.BodyOnLight;
            LightCaptionStyle = new GUIStyle(CaptionStyle);
            LightCaptionStyle.normal.textColor = inkMuted;
            LightEyebrowStyle = new GUIStyle(EyebrowStyle);
            LightEyebrowStyle.normal.textColor = new Color(0.30f, 0.38f, 0.51f, 1f);
            LightCardTitleStyle = new GUIStyle(CardTitleStyle);
            LightCardTitleStyle.normal.textColor = ink;
            LightCardSubtitleStyle = new GUIStyle(CardSubtitleStyle);
            LightCardSubtitleStyle.normal.textColor = inkMuted;
            LightButtonTextStyle = new GUIStyle(ButtonTextStyle);
            LightButtonTextStyle.normal.textColor = ink;
            LightDangerStyle = new GUIStyle(LightBodyStyle);
            LightDangerStyle.normal.textColor = new Color(0.72f, 0.16f, 0.20f, 1f);

            LightSearchFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                normal = { background = texSearchPill, textColor = ink },
                focused = { background = texSearchPill, textColor = ink },
                active = { background = texSearchPill, textColor = ink },
                border = new RectOffset(24, 24, 24, 24),
                padding = new RectOffset(48, 16, 8, 8),
                font = uiFont,
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft
            };

            LightSliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                normal = { background = texLightSliderTrack },
                hover = { background = texLightSliderTrack },
                active = { background = texLightSliderTrack },
                border = new RectOffset(8, 8, 5, 5),
                fixedHeight = 12f
            };

            LightSliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                normal = { background = texLightSliderThumb },
                hover = { background = texLightSliderThumb },
                active = { background = texLightSliderThumb },
                border = new RectOffset(16, 16, 16, 16),
                fixedWidth = 24f,
                fixedHeight = 24f
            };

            LightIconButtonStyle = new GUIStyle
            {
                normal = { background = texCircleClose, textColor = ink },
                hover = { background = texLightCardSelected, textColor = ink },
                active = { background = texLightCardSelected, textColor = ink },
                border = new RectOffset(30, 30, 30, 30),
                alignment = TextAnchor.MiddleCenter
            };

            WhitePillBarStyle = new GUIStyle
            {
                normal = { background = texWhiteBar },
                border = border22,
                padding = new RectOffset(20, 20, 12, 12)
            };

            ReadinessMintCardStyle = new GUIStyle
            {
                normal = { background = texMintCard },
                border = border22,
                padding = new RectOffset(20, 20, 14, 14)
            };

            LightDockCardStyle = new GUIStyle
            {
                normal = { background = texDockCard },
                border = new RectOffset(26, 26, 26, 26),
                padding = new RectOffset(22, 22, 18, 18)
            };

            PrimaryPillButtonStyle = new GUIStyle
            {
                normal = { background = texPillBlue, textColor = Color.white },
                hover = { background = texPillBlueHover, textColor = Color.white },
                active = { background = texPillBluePressed, textColor = Color.white },
                focused = { background = texPillBlue, textColor = Color.white },
                border = new RectOffset(28, 28, 28, 28),
                font = uiFont,
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            DangerPillButtonStyle = new GUIStyle
            {
                normal = { background = texPillRed, textColor = Color.white },
                hover = { background = texPillRedHover, textColor = Color.white },
                active = { background = texPillRedPressed, textColor = Color.white },
                focused = { background = texPillRed, textColor = Color.white },
                border = new RectOffset(28, 28, 28, 28),
                font = uiFont,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            PoiCardNormalStyle = new GUIStyle
            {
                normal = { background = texPoiNorm },
                hover = { background = texPoiSel },
                active = { background = texPoiSel },
                border = border16,
                padding = new RectOffset(16, 16, 12, 12),
                alignment = TextAnchor.MiddleLeft
            };

            PoiCardSelectedStyle = new GUIStyle
            {
                normal = { background = texPoiSel },
                hover = { background = texPoiSel },
                active = { background = texPoiSel },
                border = border16,
                padding = new RectOffset(16, 16, 12, 12),
                alignment = TextAnchor.MiddleLeft
            };

            HeaderBreadcrumbStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 16,
                fontStyle = FontStyle.Normal,
                richText = true,
                normal = { textColor = new Color(0.12f, 0.16f, 0.23f, 1.0f) },
                alignment = TextAnchor.MiddleLeft
            };

            HeaderSubtitleStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 13,
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(0.39f, 0.45f, 0.55f, 1.0f) },
                alignment = TextAnchor.MiddleLeft
            };

            PillButtonTextStyle = new GUIStyle
            {
                font = uiFont,
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };

            initialized = true;
        }

        public bool DrawLauncherCard(Rect rect, Texture2D icon, string title, string subtitle, bool primary)
        {
            GUIStyle cardStyle = primary ? LauncherPrimaryCardStyle : LauncherCardStyle;
            bool clicked = GUI.Button(rect, GUIContent.none, cardStyle);

            float iconSize = Mathf.Min(48f, rect.height * 0.56f);
            float iconX = rect.x + 20f;
            float iconY = rect.y + (rect.height - iconSize) * 0.5f;

            if (icon != null)
            {
                Color oldColor = GUI.color;
                GUI.color = Color.white;
                Rect iconRect = new Rect(iconX, iconY, iconSize, iconSize);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                GUI.color = oldColor;
            }

            float textX = rect.x + 82f;
            float textWidth = Mathf.Max(80f, rect.width - 132f);
            float textTop = rect.y + (rect.height - 54f) * 0.5f;
            GUI.Label(new Rect(textX, textTop, textWidth, 30f), title,
                primary ? LauncherPrimaryTitleStyle : LauncherCardTitleStyle);
            GUI.Label(new Rect(textX, textTop + 29f, textWidth, 24f), subtitle,
                primary ? LauncherPrimarySubtitleStyle : LauncherCardSubtitleStyle);

            Color previousColor = GUI.color;
            GUI.color = primary ? Color.white : new Color(0.31f, 0.49f, 0.96f, 1f);
            GUI.DrawTexture(new Rect(rect.xMax - 40f, rect.center.y - 11f, 22f, 22f), IconArrow, ScaleMode.ScaleToFit);
            GUI.color = previousColor;

            return clicked;
        }

        // =========================================================================
        // HIGH-LEVEL COMPONENT DRAWING (Anti-aliased, zero emojis, pixel-perfect)
        // =========================================================================

        public bool DrawIconButton(Rect rect, Texture2D icon, GUIStyle style, float iconSize = 26f, Color? iconTint = null)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, style);
            if (icon != null)
            {
                float ix = rect.x + (rect.width - iconSize) * 0.5f;
                float iy = rect.y + (rect.height - iconSize) * 0.5f;
                Color oldColor = GUI.color;
                if (iconTint.HasValue) GUI.color = iconTint.Value;
                GUI.DrawTexture(new Rect(ix, iy, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
                GUI.color = oldColor;
            }
            return clicked;
        }

        public bool DrawButtonWithIcon(Rect rect, Texture2D icon, string text, GUIStyle style, float iconSize = 24f, float spacing = 10f, GUIStyle labelStyle = null, Color? iconTint = null)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, style);

            GUIStyle resolvedLabelStyle = labelStyle ?? ButtonTextStyle;
            measureContent.text = text ?? string.Empty;
            float textWidth = resolvedLabelStyle.CalcSize(measureContent).x;
            float totalWidth = (icon != null ? iconSize + spacing : 0f) + textWidth;
            float startX = rect.x + Mathf.Max(0f, (rect.width - totalWidth) * 0.5f);

            if (icon != null)
            {
                float iy = rect.y + (rect.height - iconSize) * 0.5f;
                Color oldColor = GUI.color;
                if (iconTint.HasValue) GUI.color = iconTint.Value;
                GUI.DrawTexture(new Rect(startX, iy, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
                GUI.color = oldColor;
            }

            float textX = startX + (icon != null ? iconSize + spacing : 0f);
            Rect textRect = new Rect(textX, rect.y, textWidth + 6f, rect.height);
            GUI.Label(textRect, text, resolvedLabelStyle);

            return clicked;
        }

        public void DrawTopNavigationBanner(Rect rect, string destination, float distanceMeters, string guidanceMessage, out bool stopClicked)
        {
            GUI.Box(rect, GUIContent.none, TopBannerStyle);

            // Left icon (Destination Pin)
            float pinSize = 34f;
            float pinX = rect.x + 20f;
            float pinY = rect.y + (rect.height - pinSize) * 0.5f;
            GUI.DrawTexture(new Rect(pinX, pinY, pinSize, pinSize), IconPin, ScaleMode.ScaleToFit);

            // Right Stop Button
            float stopWidth = 116f;
            float stopHeight = 48f;
            float stopX = rect.x + rect.width - stopWidth - 18f;
            float stopY = rect.y + (rect.height - stopHeight) * 0.5f;
            Rect stopRect = new Rect(stopX, stopY, stopWidth, stopHeight);
            stopClicked = DrawButtonWithIcon(stopRect, IconClose, "Dung", DangerButtonStyle, 18f, 6f);

            // Center Text Block
            float textX = pinX + pinSize + 14f;
            float textWidth = stopX - textX - 12f;
            if (textWidth > 50f)
            {
                float titleHeight = 32f;
                float subHeight = 24f;
                float totalTextH = titleHeight + subHeight + 2f;
                float startY = rect.y + (rect.height - totalTextH) * 0.5f;

                GUI.Label(new Rect(textX, startY, textWidth, titleHeight), destination, TitleStyle);

                string distText = $"{distanceMeters:0.0}m - {guidanceMessage}";
                GUI.Label(new Rect(textX, startY + titleHeight + 2f, textWidth, subHeight), distText, SubtitleStyle);
            }
        }

        public void DrawCompanionPill(Rect rect, string message)
        {
            GUI.Box(rect, GUIContent.none, BottomPillStyle);

            // Left Avatar (Doraemon face badge)
            float avatarSize = 42f;
            float ax = rect.x + 18f;
            float ay = rect.y + (rect.height - avatarSize) * 0.5f;
            GUI.DrawTexture(new Rect(ax, ay, avatarSize, avatarSize), IconCompanion, ScaleMode.ScaleToFit);

            // Right Text
            float tx = ax + avatarSize + 14f;
            float tw = rect.width - (tx - rect.x) - 18f;
            if (tw > 20f)
            {
                GUI.Label(new Rect(tx, rect.y, tw, rect.height), message, CompanionTextStyle);
            }
        }

        public void DrawSearchingPill(Rect rect, string message)
        {
            GUI.Box(rect, GUIContent.none, BottomPillStyle);

            float iconSize = 32f;
            float ix = rect.x + 20f;
            float iy = rect.y + (rect.height - iconSize) * 0.5f;
            GUI.DrawTexture(new Rect(ix, iy, iconSize, iconSize), IconSearch, ScaleMode.ScaleToFit);

            float tx = ix + iconSize + 14f;
            float tw = rect.width - (tx - rect.x) - 20f;
            if (tw > 20f)
            {
                GUI.Label(new Rect(tx, rect.y, tw, rect.height), message, CompanionTextStyle);
            }
        }

        public bool DrawCardItem(Rect rect, Texture2D icon, string title, string category, bool isSelected, bool light = false)
        {
            if (light)
            {
                GUIStyle cardStyle = isSelected ? PoiCardSelectedStyle : PoiCardNormalStyle;
                bool clicked = GUI.Button(rect, GUIContent.none, cardStyle);

                // Left soft blue rounded square container
                float boxSize = 44f;
                float bx = rect.x + 14f;
                float by = rect.y + (rect.height - boxSize) * 0.5f;
                if (IconBoxBlue != null)
                {
                    GUI.DrawTexture(new Rect(bx, by, boxSize, boxSize), IconBoxBlue, ScaleMode.StretchToFill);
                }

                // Inside icon (royal blue)
                float iconSize = 22f;
                float ix = bx + (boxSize - iconSize) * 0.5f;
                float iy = by + (boxSize - iconSize) * 0.5f;
                if (icon != null)
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(0.11f, 0.42f, 0.96f, 1f);
                    GUI.DrawTexture(new Rect(ix, iy, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
                    GUI.color = oldColor;
                }

                // Right Radio Button (Selected vs Unselected)
                float radioSize = 26f;
                float rx = rect.xMax - radioSize - 16f;
                float ry = rect.y + (rect.height - radioSize) * 0.5f;
                Texture2D radioTex = isSelected ? IconRadioSelected : IconRadioUnselected;
                if (radioTex != null)
                {
                    GUI.DrawTexture(new Rect(rx, ry, radioSize, radioSize), radioTex, ScaleMode.ScaleToFit);
                }

                // Middle Title and Category Label
                float tx = bx + boxSize + 14f;
                float tw = rx - tx - 10f;
                if (tw > 20f)
                {
                    float titleHeight = 24f;
                    float subHeight = 18f;
                    float startY = rect.y + (rect.height - (titleHeight + subHeight)) * 0.5f;

                    GUI.Label(new Rect(tx, startY, tw, titleHeight), title ?? string.Empty, LightCardTitleStyle);
                    GUI.Label(new Rect(tx, startY + titleHeight + 2f, tw, subHeight), category ?? string.Empty, LightCardSubtitleStyle);
                }

                return clicked;
            }
            else
            {
                GUIStyle cardStyle = isSelected ? CardSelectedStyle : CardNormalStyle;
                bool clicked = GUI.Button(rect, GUIContent.none, cardStyle);

                // Left Icon
                float iconSize = 30f;
                float ix = rect.x + 18f;
                float iy = rect.y + (rect.height - iconSize) * 0.5f;
                if (icon != null)
                {
                    GUI.DrawTexture(new Rect(ix, iy, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
                }

                // Right checkmark if selected
                float checkSize = 24f;
                float cx = rect.x + rect.width - checkSize - 18f;
                float cy = rect.y + (rect.height - checkSize) * 0.5f;
                if (isSelected)
                {
                    GUI.DrawTexture(new Rect(cx, cy, checkSize, checkSize), IconCheck, ScaleMode.ScaleToFit);
                }

                // Title and Category Label
                float tx = ix + iconSize + 14f;
                float tw = (isSelected ? cx : rect.x + rect.width) - tx - 12f;
                if (tw > 20f)
                {
                    float titleHeight = 26f;
                    float subHeight = 20f;
                    float startY = rect.y + (rect.height - (titleHeight + subHeight)) * 0.5f;

                    GUI.Label(new Rect(tx, startY, tw, titleHeight), title, CardTitleStyle);
                    GUI.Label(new Rect(tx, startY + titleHeight, tw, subHeight), category, CardSubtitleStyle);
                }

                return clicked;
            }
        }

        public bool DrawPillButton(Rect rect, Texture2D icon, string text, GUIStyle style, float iconSize = 22f, float spacing = 10f)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, style);

            GUIStyle labelStyle = PillButtonTextStyle;
            measureContent.text = text ?? string.Empty;
            float textWidth = labelStyle.CalcSize(measureContent).x;
            float totalWidth = (icon != null ? iconSize + spacing : 0f) + textWidth;
            float startX = rect.x + Mathf.Max(0f, (rect.width - totalWidth) * 0.5f);

            if (icon != null)
            {
                float iy = rect.y + (rect.height - iconSize) * 0.5f;
                Color oldColor = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(startX, iy, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
                GUI.color = oldColor;
            }

            float textX = startX + (icon != null ? iconSize + spacing : 0f);
            Rect textRect = new Rect(textX, rect.y, textWidth + 6f, rect.height);
            GUI.Label(textRect, text, labelStyle);

            return clicked;
        }

        // =========================================================================
        // PROCEDURAL VECTOR ICON GENERATORS (Crisp, Anti-aliased, Zero Missing Emojis)
        // =========================================================================

        private static Texture2D GeneratePinIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            Vector2 headCenter = new Vector2(size * 0.5f, size * 0.65f);
            float headRadius = size * 0.28f;
            float innerRadius = size * 0.12f;
            Vector2 tip = new Vector2(size * 0.5f, size * 0.12f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float distHead = Vector2.Distance(pt, headCenter);

                    // Circle head
                    float headAlpha = Mathf.Clamp01(headRadius - distHead + 0.5f);

                    // Triangle stem
                    float stemAlpha = 0f;
                    if (y <= headCenter.y && y >= tip.y)
                    {
                        float t = (y - tip.y) / (headCenter.y - tip.y);
                        float halfWidth = headRadius * Mathf.Sqrt(Mathf.Clamp01(1f - (1f - t) * (1f - t)));
                        float dx = Mathf.Abs(x - headCenter.x);
                        stemAlpha = Mathf.Clamp01(halfWidth - dx + 0.5f);
                    }

                    float outerAlpha = Mathf.Max(headAlpha, stemAlpha);

                    // Hollow center
                    float innerAlpha = Mathf.Clamp01(distHead - innerRadius + 0.5f);
                    float finalAlpha = outerAlpha * innerAlpha;

                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * finalAlpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateCloseIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float thickness = size * 0.08f;
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float span = size * 0.26f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float d1 = DistanceToLineSegment(pt, c - new Vector2(span, span), c + new Vector2(span, span));
                    float d2 = DistanceToLineSegment(pt, c - new Vector2(span, -span), c + new Vector2(span, -span));
                    float d = Mathf.Min(d1, d2);
                    float alpha = Mathf.Clamp01(thickness - d + 0.5f);

                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateGearIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float rOuter = size * 0.36f;
            float rInner = size * 0.24f;
            float rHole = size * 0.12f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float d = Vector2.Distance(pt, c);
                    float angle = Mathf.Atan2(pt.y - c.y, pt.x - c.x);
                    float tooth = Mathf.Cos(angle * 6f); // 6 gear teeth
                    float rTarget = rInner + (rOuter - rInner) * Mathf.Clamp01(tooth * 2.5f);

                    float gearAlpha = Mathf.Clamp01(rTarget - d + 0.5f);
                    float holeAlpha = Mathf.Clamp01(d - rHole + 0.5f);
                    float alpha = gearAlpha * holeAlpha;

                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateArrowIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float thickness = size * 0.08f;
            Vector2 start = new Vector2(size * 0.22f, size * 0.5f);
            Vector2 end = new Vector2(size * 0.72f, size * 0.5f);
            Vector2 arrowTip = new Vector2(size * 0.76f, size * 0.5f);
            Vector2 arrowTop = new Vector2(size * 0.54f, size * 0.72f);
            Vector2 arrowBot = new Vector2(size * 0.54f, size * 0.28f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float dShaft = DistanceToLineSegment(pt, start, end);
                    float dWing1 = DistanceToLineSegment(pt, arrowTop, arrowTip);
                    float dWing2 = DistanceToLineSegment(pt, arrowBot, arrowTip);
                    float d = Mathf.Min(dShaft, Mathf.Min(dWing1, dWing2));
                    float alpha = Mathf.Clamp01(thickness - d + 0.5f);

                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateDoorIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float thickness = size * 0.07f;
            Vector2 bl = new Vector2(size * 0.25f, size * 0.18f);
            Vector2 tl = new Vector2(size * 0.25f, size * 0.82f);
            Vector2 tr = new Vector2(size * 0.75f, size * 0.82f);
            Vector2 br = new Vector2(size * 0.75f, size * 0.18f);
            Vector2 knob = new Vector2(size * 0.65f, size * 0.48f);
            float knobR = size * 0.05f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float d1 = DistanceToLineSegment(pt, bl, tl);
                    float d2 = DistanceToLineSegment(pt, tl, tr);
                    float d3 = DistanceToLineSegment(pt, tr, br);
                    float d4 = DistanceToLineSegment(pt, bl, br);
                    float dFrame = Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4));
                    float frameAlpha = Mathf.Clamp01(thickness - dFrame + 0.5f);

                    float dKnob = Vector2.Distance(pt, knob);
                    float knobAlpha = Mathf.Clamp01(knobR - dKnob + 0.5f);

                    float alpha = Mathf.Max(frameAlpha, knobAlpha);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateCheckIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float thickness = size * 0.08f;
            Vector2 p1 = new Vector2(size * 0.20f, size * 0.50f);
            Vector2 p2 = new Vector2(size * 0.42f, size * 0.28f);
            Vector2 p3 = new Vector2(size * 0.82f, size * 0.74f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float d1 = DistanceToLineSegment(pt, p1, p2);
                    float d2 = DistanceToLineSegment(pt, p2, p3);
                    float d = Mathf.Min(d1, d2);
                    float alpha = Mathf.Clamp01(thickness - d + 0.5f);

                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateSearchIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            Vector2 lensCenter = new Vector2(size * 0.42f, size * 0.58f);
            float rOuter = size * 0.26f;
            float rInner = size * 0.16f;
            Vector2 handleStart = lensCenter + new Vector2(rOuter * 0.65f, -rOuter * 0.65f);
            Vector2 handleEnd = new Vector2(size * 0.82f, size * 0.18f);
            float handleThickness = size * 0.08f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float dLens = Vector2.Distance(pt, lensCenter);
                    float ringAlpha = Mathf.Clamp01(rOuter - dLens + 0.5f) * Mathf.Clamp01(dLens - rInner + 0.5f);

                    float dHandle = DistanceToLineSegment(pt, handleStart, handleEnd);
                    float handleAlpha = Mathf.Clamp01(handleThickness - dHandle + 0.5f);

                    float alpha = Mathf.Max(ringAlpha, handleAlpha);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateCompanionIcon(int size)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);

            // White rounded robot head with glowing cyan-blue eyes and dark accents (matching robot.glb)
            float rHead = size * 0.38f;
            float rEar = size * 0.09f;
            Vector2 earLeft = new Vector2(size * 0.12f, size * 0.52f);
            Vector2 earRight = new Vector2(size * 0.88f, size * 0.52f);

            Color whiteBody = new Color(0.96f, 0.97f, 1.0f, 1f);
            Color darkAccent = new Color(0.12f, 0.16f, 0.23f, 1f);
            Color glowingBlue = new Color(0.11f, 0.42f, 0.96f, 1f);
            Color eyeCyan = new Color(0.35f, 0.75f, 1.0f, 1f);

            float visorXMin = size * 0.28f;
            float visorXMax = size * 0.72f;
            float visorYMin = size * 0.44f;
            float visorYMax = size * 0.62f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);

                    // Ears (black matte accents on robot side)
                    float dEarL = Vector2.Distance(pt, earLeft);
                    float dEarR = Vector2.Distance(pt, earRight);
                    float earAlpha = Mathf.Max(Mathf.Clamp01(rEar - dEarL + 0.5f), Mathf.Clamp01(rEar - dEarR + 0.5f));

                    // Head (white glossy sphere)
                    float dHead = Vector2.Distance(pt, c);
                    float headAlpha = Mathf.Clamp01(rHead - dHead + 0.5f);

                    if (headAlpha <= 0f && earAlpha <= 0f) continue;

                    Color pixelColor = Color.clear;
                    if (earAlpha > 0f)
                    {
                        pixelColor = darkAccent;
                        pixelColor.a = earAlpha;
                    }

                    if (headAlpha > 0f)
                    {
                        Color hColor = whiteBody;

                        // Dark visor area
                        if (x >= visorXMin && x <= visorXMax && y >= visorYMin && y <= visorYMax)
                        {
                            hColor = darkAccent;

                            // Glowing cyan eyes inside visor
                            bool isLeftEye = Mathf.Abs(x - size * 0.39f) <= 2.5f && Mathf.Abs(y - size * 0.53f) <= 2.5f;
                            bool isRightEye = Mathf.Abs(x - size * 0.61f) <= 2.5f && Mathf.Abs(y - size * 0.53f) <= 2.5f;
                            if (isLeftEye || isRightEye)
                            {
                                hColor = eyeCyan;
                            }
                        }
                        // Glowing cyan-blue chest/mouth wave
                        else if (x >= size * 0.40f && x <= size * 0.60f && Mathf.Abs(y - size * 0.33f) <= 1.2f)
                        {
                            hColor = glowingBlue;
                        }

                        pixelColor = Color.Lerp(pixelColor, hColor, headAlpha);
                    }

                    tex.SetPixel(x, y, pixelColor);
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateHandleTexture(int width, int height, Color color)
        {
            Texture2D tex = CreateRoundedTexture(width, height, height / 2, color, color, 0f);
            return tex;
        }

        private static Texture2D GenerateLauncherGradientTexture(Color topColor, Color bottomColor)
        {
            const int width = 4;
            const int height = 128;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < height; y++)
            {
                float t = 1f - (y / (float)(height - 1));
                Color rowColor = Color.Lerp(topColor, bottomColor, t);
                for (int x = 0; x < width; x++)
                    tex.SetPixel(x, y, rowColor);
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateBackIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float thickness = size * 0.08f;
            Vector2 tip = new Vector2(size * 0.24f, size * 0.50f);
            Vector2 end = new Vector2(size * 0.76f, size * 0.50f);
            Vector2 top = new Vector2(size * 0.48f, size * 0.74f);
            Vector2 bot = new Vector2(size * 0.48f, size * 0.26f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float dShaft = DistanceToLineSegment(pt, tip, end);
                    float dWing1 = DistanceToLineSegment(pt, top, tip);
                    float dWing2 = DistanceToLineSegment(pt, bot, tip);
                    float d = Mathf.Min(dShaft, Mathf.Min(dWing1, dWing2));
                    float alpha = Mathf.Clamp01(thickness - d + 0.5f);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateWalkIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float stroke = size * 0.075f;
            Vector2 head = new Vector2(size * 0.56f, size * 0.20f);
            float headRadius = size * 0.085f;
            Vector2 shoulder = new Vector2(size * 0.48f, size * 0.37f);
            Vector2 hip = new Vector2(size * 0.43f, size * 0.61f);
            Vector2 armLeft = new Vector2(size * 0.27f, size * 0.50f);
            Vector2 armRight = new Vector2(size * 0.70f, size * 0.45f);
            Vector2 handLeft = new Vector2(size * 0.19f, size * 0.64f);
            Vector2 handRight = new Vector2(size * 0.82f, size * 0.31f);
            Vector2 kneeLeft = new Vector2(size * 0.30f, size * 0.78f);
            Vector2 footLeft = new Vector2(size * 0.19f, size * 0.91f);
            Vector2 kneeRight = new Vector2(size * 0.66f, size * 0.77f);
            Vector2 footRight = new Vector2(size * 0.78f, size * 0.91f);
            Vector2[] strokes =
            {
                shoulder, hip,
                shoulder, armLeft,
                armLeft, handLeft,
                shoulder, armRight,
                armRight, handRight,
                hip, kneeLeft,
                kneeLeft, footLeft,
                hip, kneeRight,
                kneeRight, footRight
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(x, y);
                    float distance = Vector2.Distance(point, head) - headRadius;
                    for (int i = 0; i < strokes.Length; i += 2)
                        distance = Mathf.Min(distance, DistanceToLineSegment(point, strokes[i], strokes[i + 1]));
                    float alpha = Mathf.Clamp01(stroke - distance + 0.5f);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateArProjectIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float stroke = size * 0.055f;

            // Document outline with folded top-right corner and bottom-right cloud cutout
            Vector2 bl = new Vector2(size * 0.18f, size * 0.14f);
            Vector2 tl = new Vector2(size * 0.18f, size * 0.86f);
            Vector2 topCut = new Vector2(size * 0.56f, size * 0.86f);
            Vector2 rightCut = new Vector2(size * 0.76f, size * 0.66f);
            Vector2 foldCorner = new Vector2(size * 0.56f, size * 0.66f);
            Vector2 rightMid = new Vector2(size * 0.76f, size * 0.46f);
            Vector2 botMid = new Vector2(size * 0.42f, size * 0.14f);

            // Cloud at bottom-right
            Vector2 cloudLeft = new Vector2(size * 0.48f, size * 0.18f);
            Vector2 cloudRight = new Vector2(size * 0.82f, size * 0.18f);
            Vector2 cloudArchTop = new Vector2(size * 0.65f, size * 0.34f);
            Vector2 cloudLeftTop = new Vector2(size * 0.50f, size * 0.27f);
            Vector2 cloudRightTop = new Vector2(size * 0.80f, size * 0.27f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float dDoc = Mathf.Min(
                        Mathf.Min(DistanceToLineSegment(pt, bl, tl), DistanceToLineSegment(pt, tl, topCut)),
                        Mathf.Min(DistanceToLineSegment(pt, topCut, rightCut), DistanceToLineSegment(pt, rightCut, rightMid)));
                    dDoc = Mathf.Min(dDoc, DistanceToLineSegment(pt, bl, botMid));
                    dDoc = Mathf.Min(dDoc, Mathf.Min(DistanceToLineSegment(pt, topCut, foldCorner), DistanceToLineSegment(pt, foldCorner, rightCut)));

                    float dCloud = Mathf.Min(
                        Mathf.Min(DistanceToLineSegment(pt, cloudLeft, cloudRight), DistanceToLineSegment(pt, cloudLeft, cloudLeftTop)),
                        Mathf.Min(DistanceToLineSegment(pt, cloudLeftTop, cloudArchTop),
                                  Mathf.Min(DistanceToLineSegment(pt, cloudArchTop, cloudRightTop), DistanceToLineSegment(pt, cloudRightTop, cloudRight))));

                    float d = Mathf.Min(dDoc, dCloud);
                    float alpha = Mathf.Clamp01(stroke - d + 0.5f);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateVpsMapIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float stroke = size * 0.055f;

            // Trifold map vertices
            Vector2 b0 = new Vector2(size * 0.14f, size * 0.22f);
            Vector2 t0 = new Vector2(size * 0.14f, size * 0.74f);
            Vector2 b1 = new Vector2(size * 0.38f, size * 0.14f);
            Vector2 t1 = new Vector2(size * 0.38f, size * 0.66f);
            Vector2 b2 = new Vector2(size * 0.62f, size * 0.26f);
            Vector2 t2 = new Vector2(size * 0.62f, size * 0.78f);
            Vector2 b3 = new Vector2(size * 0.86f, size * 0.18f);
            Vector2 t3 = new Vector2(size * 0.86f, size * 0.70f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float dVert = Mathf.Min(
                        Mathf.Min(DistanceToLineSegment(pt, b0, t0), DistanceToLineSegment(pt, b1, t1)),
                        Mathf.Min(DistanceToLineSegment(pt, b2, t2), DistanceToLineSegment(pt, b3, t3)));
                    float dTop = Mathf.Min(
                        DistanceToLineSegment(pt, t0, t1),
                        Mathf.Min(DistanceToLineSegment(pt, t1, t2), DistanceToLineSegment(pt, t2, t3)));
                    float dBot = Mathf.Min(
                        DistanceToLineSegment(pt, b0, b1),
                        Mathf.Min(DistanceToLineSegment(pt, b1, b2), DistanceToLineSegment(pt, b2, b3)));

                    float d = Mathf.Min(dVert, Mathf.Min(dTop, dBot));
                    float alpha = Mathf.Clamp01(stroke - d + 0.5f);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateLockIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            float stroke = size * 0.065f;
            Vector2 bl = new Vector2(size * 0.24f, size * 0.16f);
            Vector2 tl = new Vector2(size * 0.24f, size * 0.52f);
            Vector2 tr = new Vector2(size * 0.76f, size * 0.52f);
            Vector2 br = new Vector2(size * 0.76f, size * 0.16f);
            Vector2 shackleCenter = new Vector2(size * 0.50f, size * 0.56f);
            float shackleRadius = size * 0.18f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float dBody = Mathf.Min(
                        Mathf.Min(DistanceToLineSegment(pt, bl, tl), DistanceToLineSegment(pt, tl, tr)),
                        Mathf.Min(DistanceToLineSegment(pt, tr, br), DistanceToLineSegment(pt, bl, br)));
                    float dShackle = y >= shackleCenter.y
                        ? Mathf.Abs(Vector2.Distance(pt, shackleCenter) - shackleRadius)
                        : float.MaxValue;
                    float d = Mathf.Min(dBody, dShackle);
                    float alpha = Mathf.Clamp01(stroke - d + 0.5f);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private static float DistanceToLineSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        private static Texture2D CreateEmptyTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, clear);
            return tex;
        }

        private static Texture2D CreateRoundedTexture(int width, int height, int radius, Color fill, Color border, float borderWidth)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color transparent = new Color(0, 0, 0, 0);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int cx = x < radius ? radius : (x > width - radius - 1 ? width - radius - 1 : x);
                    int cy = y < radius ? radius : (y > height - radius - 1 ? height - radius - 1 : y);
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));

                    if (dist > radius)
                    {
                        tex.SetPixel(x, y, transparent);
                    }
                    else
                    {
                        float edgeDist = radius - dist;
                        float alpha = Mathf.Clamp01(edgeDist);

                        Color c;
                        if (borderWidth > 0.01f && dist >= radius - borderWidth)
                        {
                            float borderFactor = Mathf.Clamp01((dist - (radius - borderWidth)) / 1.0f);
                            c = Color.Lerp(fill, border, borderFactor);
                        }
                        else
                        {
                            c = fill;
                        }

                        tex.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * alpha));
                    }
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateNavArrowIcon(int size, Color color)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            Vector2 tip = new Vector2(size * 0.84f, size * 0.84f);
            Vector2 wing1 = new Vector2(size * 0.16f, size * 0.64f);
            Vector2 wing2 = new Vector2(size * 0.64f, size * 0.16f);
            Vector2 notch = new Vector2(size * 0.44f, size * 0.44f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    bool inTri1 = PointInTriangle(pt, tip, wing1, notch);
                    bool inTri2 = PointInTriangle(pt, tip, wing2, notch);

                    if (inTri1 || inTri2)
                    {
                        float d1 = DistanceToLineSegment(pt, tip, wing1);
                        float d2 = DistanceToLineSegment(pt, wing1, notch);
                        float d3 = DistanceToLineSegment(pt, notch, wing2);
                        float d4 = DistanceToLineSegment(pt, wing2, tip);
                        float d = Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4));
                        float alpha = Mathf.Clamp01(d + 0.5f);
                        tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                    }
                }
            }
            tex.Apply();
            return tex;
        }

        private static bool PointInTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            float d1 = Sign(pt, v1, v2);
            float d2 = Sign(pt, v2, v3);
            float d3 = Sign(pt, v3, v1);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        private static Texture2D GenerateRadioSelected(int size, Color circleColor, Color checkColor)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.44f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float d = Vector2.Distance(pt, c);
                    float circleAlpha = Mathf.Clamp01(radius - d + 0.5f);
                    if (circleAlpha > 0f)
                    {
                        tex.SetPixel(x, y, new Color(circleColor.r, circleColor.g, circleColor.b, circleColor.a * circleAlpha));
                    }
                }
            }

            float thickness = size * 0.08f;
            Vector2 p1 = new Vector2(size * 0.28f, size * 0.50f);
            Vector2 p2 = new Vector2(size * 0.44f, size * 0.34f);
            Vector2 p3 = new Vector2(size * 0.72f, size * 0.66f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float d1 = DistanceToLineSegment(pt, p1, p2);
                    float d2 = DistanceToLineSegment(pt, p2, p3);
                    float d = Mathf.Min(d1, d2);
                    float checkAlpha = Mathf.Clamp01(thickness - d + 0.5f);
                    if (checkAlpha > 0f)
                    {
                        Color prev = tex.GetPixel(x, y);
                        Color blended = Color.Lerp(prev, checkColor, checkAlpha);
                        tex.SetPixel(x, y, blended);
                    }
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateRadioUnselected(int size, Color outlineColor, float borderWidth)
        {
            Texture2D tex = CreateEmptyTexture(size, size);
            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.44f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    float d = Vector2.Distance(pt, c);
                    float outerAlpha = Mathf.Clamp01(radius - d + 0.5f);
                    float innerAlpha = Mathf.Clamp01(d - (radius - borderWidth) + 0.5f);
                    float alpha = outerAlpha * innerAlpha;
                    if (alpha > 0f)
                    {
                        tex.SetPixel(x, y, new Color(outlineColor.r, outlineColor.g, outlineColor.b, outlineColor.a * alpha));
                    }
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateBottomGradientTexture(int width, int height, Color bottomColor)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < height; y++)
            {
                float t = Mathf.Clamp01((float)y / (height - 1));
                float alpha = (1f - t) * bottomColor.a;
                Color c = new Color(bottomColor.r, bottomColor.g, bottomColor.b, alpha);
                for (int x = 0; x < width; x++)
                {
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
