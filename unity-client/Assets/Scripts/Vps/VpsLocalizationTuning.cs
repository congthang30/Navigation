using UnityEngine;

namespace Vps.UnityClient.Vps
{
    [CreateAssetMenu(menuName = "VPS/Localization Tuning", fileName = "VpsLocalizationTuning")]
    public sealed class VpsLocalizationTuning : ScriptableObject
    {
        [SerializeField, Min(0.1f)] private float trackedStableSeconds = 0.5f;
        [SerializeField, Min(1f)] private float localizationTimeoutSeconds = 20f;

        public double TrackedStableSeconds => trackedStableSeconds;
        public double LocalizationTimeoutSeconds => localizationTimeoutSeconds;
        public bool IsValid => IsFinite(trackedStableSeconds) && trackedStableSeconds >= 0.1f &&
            IsFinite(localizationTimeoutSeconds) && localizationTimeoutSeconds >= trackedStableSeconds + 1f;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
