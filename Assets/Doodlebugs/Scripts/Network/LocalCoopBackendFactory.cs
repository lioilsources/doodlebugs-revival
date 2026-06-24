namespace Doodlebugs.Network
{
    /// <summary>
    /// Picks the right native local-coop backend for the current build target.
    /// Editor and desktop fall back to a harmless no-op so the project always
    /// compiles and runs (the mobile-data path is never selected there anyway).
    /// </summary>
    public static class LocalCoopBackendFactory
    {
        public static ILocalCoopBackend Create()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new MultipeerLocalCoopBackend();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new NearbyLocalCoopBackend();
#else
            return new UnsupportedLocalCoopBackend();
#endif
        }
    }
}
