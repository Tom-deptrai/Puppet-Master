using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuppetMaster.Editor
{
    /// <summary>
    /// Phase 0 developer tool (no gameplay). Reports the technical baseline of the
    /// project so a human or AI can confirm the foundation is intact after changes.
    /// Run from the menu, or headless via
    /// <c>-executeMethod PuppetMaster.Editor.ProjectHealthCheck.RunFromCommandLine</c>.
    /// </summary>
    public static class ProjectHealthCheck
    {
        private const string BootstrapScenePath = "Assets/_Project/Scenes/Bootstrap.unity";

        [MenuItem("Puppet Master/Phase 0/Health Check")]
        public static void RunFromMenu()
        {
            Debug.Log(BuildReport(out _));
        }

        // Entry point for batch mode (-executeMethod).
        public static void RunFromCommandLine()
        {
            string report = BuildReport(out bool ok);
            Debug.Log(report);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        private static string BuildReport(out bool ok)
        {
            ok = true;
            var sb = new StringBuilder();
            sb.AppendLine("=== PUPPET MASTER - PROJECT HEALTH CHECK ===");
            sb.AppendLine($"Unity            : {Application.unityVersion}");
            sb.AppendLine($"Active target    : {EditorUserBuildSettings.activeBuildTarget}");

            // Render pipeline is URP
            var rp = GraphicsSettings.defaultRenderPipeline;
            bool urp = rp != null && rp.GetType().FullName.Contains("Universal");
            Check(sb, ref ok, "URP active", urp, rp != null ? rp.name : "none");

            // Landscape-only orientation
            bool landscape = PlayerSettings.defaultInterfaceOrientation == UIOrientation.AutoRotation
                             && !PlayerSettings.allowedAutorotateToPortrait
                             && !PlayerSettings.allowedAutorotateToPortraitUpsideDown
                             && (PlayerSettings.allowedAutorotateToLandscapeLeft
                                 || PlayerSettings.allowedAutorotateToLandscapeRight);
            Check(sb, ref ok, "Landscape-only orientation", landscape,
                PlayerSettings.defaultInterfaceOrientation.ToString());

            // Bootstrap scene present + enabled in the build list
            bool sceneExists = File.Exists(BootstrapScenePath);
            Check(sb, ref ok, "Bootstrap scene exists", sceneExists, BootstrapScenePath);

            bool inBuild = false;
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path == BootstrapScenePath && s.enabled) inBuild = true;
            }
            Check(sb, ref ok, "Bootstrap scene in Build Settings", inBuild, string.Empty);

            if (sceneExists)
            {
                var scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
                Check(sb, ref ok, "Bootstrap scene opens", scene.IsValid() && scene.isLoaded,
                    $"{scene.rootCount} root object(s)");
            }

            // Physics present (not tuned - Phase 0 leaves defaults in place)
            Check(sb, ref ok, "Physics gravity set", Physics.gravity.y < 0f, Physics.gravity.ToString());

            sb.AppendLine($"Company / Product: {PlayerSettings.companyName} / {PlayerSettings.productName}");
            sb.AppendLine($"Bundle id (iOS)  : {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)}");
            sb.AppendLine($"Bundle id (Droid): {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}");
            sb.AppendLine($"Android min SDK  : {PlayerSettings.Android.minSdkVersion}");
            sb.AppendLine($"Android arch     : {PlayerSettings.Android.targetArchitectures}");
            sb.AppendLine($"Android backend  : {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}");
            sb.AppendLine($"iOS min version  : {PlayerSettings.iOS.targetOSVersionString}");
            sb.AppendLine(ok ? ">>> HEALTH CHECK PASSED" : ">>> HEALTH CHECK FAILED");
            return sb.ToString();
        }

        private static void Check(StringBuilder sb, ref bool ok, string label, bool pass, string detail)
        {
            if (!pass) ok = false;
            sb.AppendLine($"[{(pass ? "PASS" : "FAIL")}] {label}{(string.IsNullOrEmpty(detail) ? "" : $"  ({detail})")}");
        }
    }
}
