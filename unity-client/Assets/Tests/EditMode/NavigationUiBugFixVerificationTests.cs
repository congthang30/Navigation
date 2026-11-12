using NUnit.Framework;
using UnityEngine;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    [TestFixture]
    public sealed class NavigationUiBugFixVerificationTests
    {
        private NavigationUiTheme theme;

        [SetUp]
        public void SetUp()
        {
            theme = new NavigationUiTheme();
            theme.Initialize();
        }

        [Test]
        public void SliderTrackAndThumbHaveIdenticalHeightAndZeroMarginPadding()
        {
            Assert.That(theme.LightSliderStyle.fixedHeight, Is.EqualTo(24f), "Track style height should be 24f");
            Assert.That(theme.LightSliderThumbStyle.fixedHeight, Is.EqualTo(24f), "Thumb style height should be 24f");
            Assert.That(theme.LightSliderThumbStyle.fixedWidth, Is.EqualTo(24f), "Thumb style width should be 24f");

            Assert.That(theme.LightSliderStyle.padding.top, Is.EqualTo(0));
            Assert.That(theme.LightSliderStyle.padding.bottom, Is.EqualTo(0));
            Assert.That(theme.LightSliderThumbStyle.margin.top, Is.EqualTo(0));
            Assert.That(theme.LightSliderThumbStyle.margin.bottom, Is.EqualTo(0));

            // Verify vertical center alignment in IMGUI SliderHandler coordinate space
            Rect sliderRect = new Rect(8f, 50f, 300f, 24f);
            Rect contentRect = theme.LightSliderThumbStyle.margin.Remove(theme.LightSliderStyle.padding.Remove(sliderRect));
            float thumbCenterY = contentRect.y + theme.LightSliderThumbStyle.fixedHeight * 0.5f;
            float trackCenterY = sliderRect.y + sliderRect.height * 0.5f;

            Assert.That(thumbCenterY, Is.EqualTo(trackCenterY).Within(0.001f),
                "Slider thumb vertical center must perfectly match the track vertical center");
        }

        [Test]
        public void NineSliceBordersAreSafeForSmallButtons()
        {
            // LightIconButtonStyle used for 40x40 and 44x44 buttons
            Assert.That(theme.LightIconButtonStyle.border.left, Is.LessThanOrEqualTo(16));
            Assert.That(theme.LightIconButtonStyle.border.right, Is.LessThanOrEqualTo(16));
            Assert.That(theme.LightIconButtonStyle.border.top, Is.LessThanOrEqualTo(16));
            Assert.That(theme.LightIconButtonStyle.border.bottom, Is.LessThanOrEqualTo(16));

            // Pill buttons used for height=40 (Done), height=44 (Stop), height=50 (Finish)
            Assert.That(theme.PrimaryPillButtonStyle.border.top, Is.LessThanOrEqualTo(16));
            Assert.That(theme.PrimaryPillButtonStyle.border.bottom, Is.LessThanOrEqualTo(16));
            Assert.That(theme.DangerPillButtonStyle.border.top, Is.LessThanOrEqualTo(16));
            Assert.That(theme.DangerPillButtonStyle.border.bottom, Is.LessThanOrEqualTo(16));

            // Thumb style border must be smaller than 24x24 fixed size
            Assert.That(theme.LightSliderThumbStyle.border.left, Is.LessThanOrEqualTo(12));
            Assert.That(theme.LightSliderThumbStyle.border.right, Is.LessThanOrEqualTo(12));
            Assert.That(theme.LightSliderThumbStyle.border.top, Is.LessThanOrEqualTo(12));
            Assert.That(theme.LightSliderThumbStyle.border.bottom, Is.LessThanOrEqualTo(12));
        }

        [Test]
        public void CompanionGuideDefaultsAndLeadDistanceAreBalanced()
        {
            var go = new GameObject("TestCompanionBalance");
            try
            {
                var guide = go.AddComponent<DoraemonCompanionGuide>();
                Assert.That(DoraemonCompanionGuide.DefaultLeadDistance, Is.GreaterThanOrEqualTo(1.8f),
                    "Default lead distance must be at least 1.8m to prevent obstructing user's camera view");
                Assert.That(guide.LeadDistance, Is.GreaterThanOrEqualTo(1.2f),
                    "Lead distance must not be allowed to drop under 1.2m");

                // Test lead distance clamping
                guide.LeadDistance = 0.5f;
                Assert.That(guide.LeadDistance, Is.EqualTo(1.2f), "LeadDistance should clamp to minimum 1.2m");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CompanionModelHasDedicatedLightAndDoesNotRemainBlack()
        {
            var go = new GameObject("TestCompanionLighting");
            try
            {
                var guide = go.AddComponent<DoraemonCompanionGuide>();
                guide.Initialize(null, null);

                // Set route and visibility so model is created
                Vector3[] dummyRoute = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 5) };
                guide.SetRoute(dummyRoute);
                guide.SetVisible(true);

                // Check that companion has an active light component
                var light = guide.GetComponentInChildren<Light>(true);
                Assert.That(light, Is.Not.Null, "Companion must possess a dedicated light to prevent pitch black rendering in AR");
                Assert.That(light.intensity, Is.GreaterThan(0.5f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
