namespace Vps.UnityClient.Vps
{
    /// <summary>Provider-independent tracking state. SDK-specific states must be normalized to this enum.</summary>
    public enum NormalizedVpsTrackingState
    {
        UNTRACKED,
        LIMITED,
        TRACKED
    }
}
