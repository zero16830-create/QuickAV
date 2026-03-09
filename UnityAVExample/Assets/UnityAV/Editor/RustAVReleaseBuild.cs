using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace UnityAV.Editor
{
    /// <summary>
    /// 提供给 CI 的统一发布构建入口。
    /// </summary>
    public static class RustAVReleaseBuild
    {
        private const string ScenePath = "Assets/UnityAV/Scenes/SimpleExample.unity";
        private const string DefaultProductName = "RustAVExample";
        private const string DefaultCompanyName = "zero16832";
        private const string DefaultApplicationIdentifier = "com.zero16832.rustavexample";

        public static void BuildFromCi()
        {
            var args = new BuildArguments(Environment.GetCommandLineArgs());
            var target = ParseBuildTarget(args.Require("rustavTarget"));
            var outputPath = args.Require("rustavOutput");
            var version = args.Get("rustavVersion", "0.1.0");
            var applicationIdentifier = args.Get(
                "rustavApplicationIdentifier",
                DefaultApplicationIdentifier);
            var androidVersionCode = args.GetInt("rustavAndroidVersionCode", 1);
            var androidAppBundle = args.GetBool("rustavAndroidAppBundle", false);

            ApplyPlayerSettings(target, version, applicationIdentifier, androidVersionCode, androidAppBundle);

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception("CI 构建失败: " + report.summary.result);
            }

            UnityEngine.Debug.Log("[RustAVReleaseBuild] build_succeeded=" + outputPath);
        }

        private static void ApplyPlayerSettings(
            BuildTarget target,
            string version,
            string applicationIdentifier,
            int androidVersionCode,
            bool androidAppBundle)
        {
            PlayerSettings.productName = DefaultProductName;
            PlayerSettings.companyName = DefaultCompanyName;
            PlayerSettings.bundleVersion = version;

            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            if (targetGroup != BuildTargetGroup.Unknown)
            {
                PlayerSettings.SetApplicationIdentifier(targetGroup, applicationIdentifier);
            }

            if (target == BuildTarget.Android)
            {
                PlayerSettings.Android.bundleVersionCode = Math.Max(1, androidVersionCode);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                EditorUserBuildSettings.buildAppBundle = androidAppBundle;
            }
        }

        private static BuildTarget ParseBuildTarget(string value)
        {
            switch (value)
            {
                case "StandaloneWindows64":
                    return BuildTarget.StandaloneWindows64;
                case "Android":
                    return BuildTarget.Android;
                case "iOS":
                    return BuildTarget.iOS;
                default:
                    throw new ArgumentOutOfRangeException("value", value, "不支持的构建目标");
            }
        }

        private sealed class BuildArguments
        {
            private readonly string[] _args;

            public BuildArguments(string[] args)
            {
                _args = args ?? Array.Empty<string>();
            }

            public string Require(string key)
            {
                var value = Get(key, string.Empty);
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("缺少命令行参数: -" + key);
                }

                return value;
            }

            public string Get(string key, string fallback)
            {
                var prefix = "-" + key + "=";
                foreach (var arg in _args)
                {
                    if (!string.IsNullOrEmpty(arg)
                        && arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return arg.Substring(prefix.Length);
                    }
                }

                return fallback;
            }

            public int GetInt(string key, int fallback)
            {
                int parsed;
                return int.TryParse(Get(key, string.Empty), out parsed)
                    ? parsed
                    : fallback;
            }

            public bool GetBool(string key, bool fallback)
            {
                bool parsed;
                return bool.TryParse(Get(key, string.Empty), out parsed)
                    ? parsed
                    : fallback;
            }
        }
    }
}
