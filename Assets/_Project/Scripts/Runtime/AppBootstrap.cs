using UnityEngine;

namespace PuppetMaster
{
    /// <summary>
    /// Phase 0 infrastructure only — contains NO gameplay.
    /// Applies baseline application-level settings once at startup so the
    /// runtime frame pacing is consistent across the Editor, iOS and Android.
    /// </summary>
    internal static class AppBootstrap
    {
        /// <summary>Frame-rate target for the mobile build (see Docs/PROJECT_MASTER.md).</summary>
        private const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            // Drive frame pacing explicitly instead of relying on display V-Sync,
            // so the 60 FPS target behaves the same on every platform.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
