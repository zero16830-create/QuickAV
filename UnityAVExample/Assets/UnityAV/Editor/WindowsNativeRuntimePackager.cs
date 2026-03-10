using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityAV.Editor
{
    internal static class WindowsNativeRuntimePackager
    {
        private const string BuildSupportRuntimeRoot = "BuildSupport/WindowsGStreamer";
        private const string LegacyProjectPluginRuntimeRoot = "Assets/Plugins/x86_64/GStreamer";

        public static void EnsureProjectRuntimeAvailable()
        {
            List<string> missingPaths;
            ResolveProjectRuntimeRootOrThrow(out missingPaths);
        }

        public static void PackageGstreamerRuntimeOrThrow(string playerPath)
        {
            if (string.IsNullOrWhiteSpace(playerPath)
                || !playerPath.EndsWith(".exe"))
            {
                return;
            }

            string runtimeRoot;
            List<string> missingPaths;
            runtimeRoot = ResolveProjectRuntimeRootOrThrow(out missingPaths);

            var playerDirectory = Path.GetDirectoryName(playerPath) ?? string.Empty;
            var playerName = Path.GetFileNameWithoutExtension(playerPath);
            var pluginDirectory = Path.Combine(
                playerDirectory,
                playerName + "_Data",
                "Plugins",
                "x86_64");
            var packagedRuntimeRoot = Path.Combine(pluginDirectory, "GStreamer");
            var previousFlattenedRuntimeDlls = CollectFlattenedRuntimeDllNames(packagedRuntimeRoot);

            Directory.CreateDirectory(pluginDirectory);
            if (Directory.Exists(packagedRuntimeRoot))
            {
                Directory.Delete(packagedRuntimeRoot, true);
            }
            RemoveFlattenedRuntimeDlls(pluginDirectory, previousFlattenedRuntimeDlls);
            CopyDirectory(runtimeRoot, packagedRuntimeRoot);
            CopyBinDllsToPluginDirectory(packagedRuntimeRoot, pluginDirectory);
            ValidatePackagedRuntime(packagedRuntimeRoot, pluginDirectory);

            Debug.Log(
                "[WindowsNativeRuntimePackager] packaged_gstreamer_runtime="
                + packagedRuntimeRoot
                + " source="
                + runtimeRoot);
        }

        public static void ConfigureProjectRuntimeImportSettings()
        {
            if (!Directory.Exists(LegacyProjectPluginRuntimeRoot))
            {
                return;
            }

            var changedCount = 0;
            var assetGuids = AssetDatabase.FindAssets(
                string.Empty,
                new[] { LegacyProjectPluginRuntimeRoot });
            foreach (var assetGuid in assetGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(assetPath)
                    || !assetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null)
                {
                    continue;
                }

                var changed = false;
                if (importer.GetCompatibleWithAnyPlatform())
                {
                    importer.SetCompatibleWithAnyPlatform(false);
                    changed = true;
                }

                if (importer.GetCompatibleWithEditor())
                {
                    importer.SetCompatibleWithEditor(false);
                    changed = true;
                }

                if (importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64))
                {
                    importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, false);
                    changed = true;
                }

                if (!changed)
                {
                    continue;
                }

                importer.SaveAndReimport();
                changedCount++;
            }

            Debug.Log(
                "[WindowsNativeRuntimePackager] configured_gstreamer_import_settings files="
                + changedCount
                + " root="
                + LegacyProjectPluginRuntimeRoot);
        }

        private static string ResolveProjectRuntimeRootOrThrow(
            out List<string> missingPaths)
        {
            string runtimeRoot;
            if (TryResolveProjectRuntimeRoot(out runtimeRoot, out missingPaths))
            {
                return runtimeRoot;
            }

            throw new BuildFailedException(
                "[WindowsNativeRuntimePackager] gstreamer_runtime_not_found="
                + BuildSupportRuntimeRoot
                + " missing="
                + string.Join(", ", missingPaths.ToArray()));
        }

        private static bool TryResolveProjectRuntimeRoot(
            out string runtimeRoot,
            out List<string> missingPaths)
        {
            missingPaths = new List<string>();
            var candidates = new[]
            {
                BuildSupportRuntimeRoot,
                LegacyProjectPluginRuntimeRoot,
            };
            foreach (var candidate in candidates)
            {
                var candidateMissingPaths = new List<string>();
                if (!Directory.Exists(candidate))
                {
                    candidateMissingPaths.Add(candidate);
                }
                else
                {
                    var requiredDirectories = new[]
                    {
                        Path.Combine(candidate, "bin"),
                        Path.Combine(candidate, "plugins", "gstreamer"),
                        Path.Combine(candidate, "tools", "gstreamer"),
                    };

                    foreach (var requiredDirectory in requiredDirectories)
                    {
                        if (!Directory.Exists(requiredDirectory))
                        {
                            candidateMissingPaths.Add(requiredDirectory);
                        }
                    }
                }

                if (candidateMissingPaths.Count == 0)
                {
                    runtimeRoot = candidate;
                    return true;
                }

                missingPaths.AddRange(candidateMissingPaths);
            }

            runtimeRoot = string.Empty;
            return false;
        }

        private static List<string> CollectFlattenedRuntimeDllNames(string packagedRuntimeRoot)
        {
            var previousBinDirectory = Path.Combine(packagedRuntimeRoot, "bin");
            var fileNames = new List<string>();
            if (!Directory.Exists(previousBinDirectory))
            {
                return fileNames;
            }

            foreach (var dllPath in Directory.GetFiles(previousBinDirectory, "*.dll"))
            {
                var fileName = Path.GetFileName(dllPath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    fileNames.Add(fileName);
                }
            }

            return fileNames;
        }

        private static void RemoveFlattenedRuntimeDlls(
            string pluginDirectory,
            IEnumerable<string> fileNames)
        {
            foreach (var fileName in fileNames)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                var existingPath = Path.Combine(pluginDirectory, fileName);
                if (File.Exists(existingPath))
                {
                    File.Delete(existingPath);
                }
            }
        }

        private static void CopyBinDllsToPluginDirectory(
            string packagedRuntimeRoot,
            string pluginDirectory)
        {
            var binDirectory = Path.Combine(packagedRuntimeRoot, "bin");
            if (!Directory.Exists(binDirectory))
            {
                return;
            }

            foreach (var dllPath in Directory.GetFiles(binDirectory, "*.dll"))
            {
                var fileName = Path.GetFileName(dllPath);
                if (string.IsNullOrEmpty(fileName))
                {
                    continue;
                }

                File.Copy(dllPath, Path.Combine(pluginDirectory, fileName), true);
            }
        }

        private static void CopyDirectory(string sourceRoot, string destinationRoot)
        {
            Directory.CreateDirectory(destinationRoot);

            foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = directory.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
            }

            foreach (var filePath in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if (filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = filePath.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var targetPath = Path.Combine(destinationRoot, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(filePath, targetPath, true);
            }
        }

        private static void ValidatePackagedRuntime(
            string packagedRuntimeRoot,
            string pluginDirectory)
        {
            if (!Directory.Exists(packagedRuntimeRoot))
            {
                throw new BuildFailedException(
                    "[WindowsNativeRuntimePackager] packaged_runtime_missing="
                    + packagedRuntimeRoot);
            }

            var requiredDirectories = new[]
            {
                Path.Combine(packagedRuntimeRoot, "bin"),
                Path.Combine(packagedRuntimeRoot, "plugins", "gstreamer"),
                Path.Combine(packagedRuntimeRoot, "tools", "gstreamer"),
            };
            foreach (var requiredDirectory in requiredDirectories)
            {
                if (!Directory.Exists(requiredDirectory))
                {
                    throw new BuildFailedException(
                        "[WindowsNativeRuntimePackager] packaged_runtime_directory_missing="
                        + requiredDirectory);
                }
            }

            if (Directory.GetFiles(Path.Combine(packagedRuntimeRoot, "bin"), "*.dll").Length == 0)
            {
                throw new BuildFailedException(
                    "[WindowsNativeRuntimePackager] packaged_runtime_bin_empty="
                    + packagedRuntimeRoot);
            }

            if (Directory.GetFiles(pluginDirectory, "*.dll").Length == 0)
            {
                throw new BuildFailedException(
                    "[WindowsNativeRuntimePackager] packaged_plugin_directory_empty="
                    + pluginDirectory);
            }
        }
    }

    internal sealed class WindowsNativeRuntimeBuildPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder
        {
            get { return 0; }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report == null
                || report.summary.platform != BuildTarget.StandaloneWindows64)
            {
                return;
            }

            WindowsNativeRuntimePackager.PackageGstreamerRuntimeOrThrow(
                report.summary.outputPath);
        }
    }
}
