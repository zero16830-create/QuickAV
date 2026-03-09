using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityAV.Editor
{
    /// <summary>
    /// 为 batchmode 提供最小场景级验证入口。
    /// </summary>
    public static class CodexValidationBuild
    {
        private const string ScenePath = "Assets/UnityAV/Validation/CodexPullValidation.generated.unity";
        private const string MaterialPath = "Assets/UnityAV/Materials/VideoMaterial.mat";
        private const string BuildPath = "Build/CodexPullValidation/CodexPullValidation.exe";
        private const string SampleUri = "SampleVideo_1280x720_10mb.mp4";
        private const int DefaultVideoWidth = 1280;
        private const int DefaultVideoHeight = 720;

        public static void CreatePullValidationScene()
        {
            DeleteGeneratedValidationScene();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = CreateCamera();
            var surface = CreateVideoSurface();
            var player = CreateValidationPlayer();
            CreateDriver(player, surface.transform, camera);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/UnityAV/Validation");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[CodexValidationBuild] scene_created=" + ScenePath);
        }

        public static void BuildWindowsValidationPlayer()
        {
            CreatePullValidationScene();

            Directory.CreateDirectory(Path.GetDirectoryName(BuildPath) ?? "Build/CodexPullValidation");
            var previousFullScreenMode = PlayerSettings.fullScreenMode;
            var previousDefaultScreenWidth = PlayerSettings.defaultScreenWidth;
            var previousDefaultScreenHeight = PlayerSettings.defaultScreenHeight;
            var previousResizableWindow = PlayerSettings.resizableWindow;
            var previousDefaultIsNativeResolution = PlayerSettings.defaultIsNativeResolution;

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = DefaultVideoWidth;
            PlayerSettings.defaultScreenHeight = DefaultVideoHeight;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultIsNativeResolution = false;

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = BuildPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                PlayerSettings.fullScreenMode = previousFullScreenMode;
                PlayerSettings.defaultScreenWidth = previousDefaultScreenWidth;
                PlayerSettings.defaultScreenHeight = previousDefaultScreenHeight;
                PlayerSettings.resizableWindow = previousResizableWindow;
                PlayerSettings.defaultIsNativeResolution = previousDefaultIsNativeResolution;
                DeleteGeneratedValidationScene();
            }

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new System.Exception(
                    "Windows 验证包构建失败: " + report.summary.result);
            }

            Debug.Log("[CodexValidationBuild] build_succeeded=" + BuildPath);
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.transform.rotation = Quaternion.identity;

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 0.5f;

            cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private static GameObject CreateVideoSurface()
        {
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Video Surface";
            surface.transform.position = Vector3.zero;
            surface.transform.localScale = new Vector3(
                (float)DefaultVideoWidth / DefaultVideoHeight,
                1f,
                1f);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
            {
                var renderer = surface.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
            }

            return surface;
        }

        private static MediaPlayerPull CreateValidationPlayer()
        {
            var playerObject = new GameObject("Validation Player");
            var player = playerObject.AddComponent<MediaPlayerPull>();
            player.Uri = SampleUri;
            player.Loop = false;
            player.AutoPlay = true;
            player.Width = DefaultVideoWidth;
            player.Height = DefaultVideoHeight;
            player.EnableAudio = true;
            player.AutoStartAudio = true;
            player.TargetMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            return player;
        }

        private static void CreateDriver(MediaPlayerPull player, Transform surface, Camera camera)
        {
            var driver = player.gameObject.AddComponent<CodexValidationDriver>();
            driver.Player = player;
            driver.ValidationSeconds = 6f;
            driver.LogIntervalSeconds = 1f;
            driver.VideoSurface = surface;
            driver.ValidationCamera = camera;
        }

        private static void DeleteGeneratedValidationScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.DeleteAsset(ScenePath))
            {
                AssetDatabase.Refresh();
            }
        }
    }
}
