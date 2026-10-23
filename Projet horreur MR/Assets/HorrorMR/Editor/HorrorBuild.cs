using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HorrorMR.Editor
{
    /// <summary>Compile l'APK du Quest dans Builds/PretOuPas.apk (menu ou ligne de commande).</summary>
    public static class HorrorBuild
    {
        [MenuItem("Horreur MR/Compiler l'APK Quest", priority = 40)]
        public static void BuildApk()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Builds", "PretOuPas.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            var summary = report.summary;
            Debug.Log($"[Horreur] Build Android : {summary.result}, {summary.totalSize / 1000000f:F0} Mo, {summary.totalErrors} erreur(s) → {path}");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
