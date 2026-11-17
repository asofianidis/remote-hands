using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RH.EditorTools
{
    public static class BuildTools
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Remote Hands/Setup Main Scene")]
        public static void SetupScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Game").AddComponent<Game>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Remote Hands";
            PlayerSettings.companyName = "Remote Hands";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[Remote Hands] Scene set up at " + ScenePath);
        }

        [MenuItem("Remote Hands/Build Windows Player")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) SetupScene();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/RemoteHands.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Remote Hands] Build result: {report.summary.result}, size {report.summary.totalSize / (1024 * 1024)} MB, errors {report.summary.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
