using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace AlterOS.Editor
{
    static class GteSemanticToolbar
    {
        const string ToolbarElementPath = "AlterOS/Semantics/GTE";
        const string VectorAssetPath = "Assets/Resources/AlterOS/Semantics/Models/gte-anchors-vectors.bytes";
        const string MetadataAssetPath = "Assets/Resources/AlterOS/Semantics/Models/gte-anchors-metadata.json";

        static Process s_BakeProcess;
        static string s_BakeDevice;

        [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Right, defaultDockIndex = 200)]
        static MainToolbarElement CreateElement()
        {
            return new MainToolbarDropdown(
                new MainToolbarContent("GTE", "AlterOS semantic anchor tools"),
                ShowMenu);
        }

        static void ShowMenu(Rect rect)
        {
            var menu = new GenericMenu();
            if (s_BakeProcess != null)
            {
                menu.AddDisabledItem(new GUIContent("Rebake already running"));
            }
            else
            {
                menu.AddItem(new GUIContent("Rebake anchors (auto device)"), false, () => StartBake("auto"));
                menu.AddItem(new GUIContent("Rebake anchors (CPU)"), false, () => StartBake("cpu"));
                menu.AddItem(new GUIContent("Rebake anchors (CUDA)"), false, () => StartBake("cuda"));
            }

            menu.AddSeparator("");
            if (s_BakeProcess != null)
                menu.AddDisabledItem(new GUIContent("Reimport unavailable while rebaking"));
            else
                menu.AddItem(new GUIContent("Reimport baked anchor assets"), false, ReimportAnchors);
            menu.DropDown(rect);
        }

        static void StartBake(string device)
        {
            if (s_BakeProcess != null)
                return;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var vectorPath = Path.Combine(projectRoot, VectorAssetPath.Replace('/', Path.DirectorySeparatorChar));
            var metadataPath = Path.Combine(projectRoot, MetadataAssetPath.Replace('/', Path.DirectorySeparatorChar));
            var arguments =
                $"run --project Tools/SemanticLab python Tools/SemanticLab/bake_gte_anchors.py " +
                $"--output \"{vectorPath}\" --metadata \"{metadataPath}\" --device {device}";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "uv",
                    Arguments = arguments,
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("uv did not start.");
            }
            catch (Exception exception)
            {
                process.Dispose();
                EditorUtility.DisplayDialog(
                    "AlterOS GTE rebake",
                    $"Could not start the Semantic Lab baker. Ensure uv is installed and available on PATH.\n\n{exception.Message}",
                    "OK");
                return;
            }

            s_BakeProcess = process;
            s_BakeDevice = device;
            EditorApplication.update += PollBake;
            MainToolbar.Refresh(ToolbarElementPath);
            UnityEngine.Debug.Log($"AlterOS GTE anchor rebake started ({device} device). Unity will refresh the baked assets when it finishes.");
        }

        static void PollBake()
        {
            if (s_BakeProcess == null || !s_BakeProcess.HasExited)
                return;

            var process = s_BakeProcess;
            var device = s_BakeDevice;
            s_BakeProcess = null;
            s_BakeDevice = null;
            EditorApplication.update -= PollBake;

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            var exitCode = process.ExitCode;
            process.Dispose();

            if (exitCode == 0)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                UnityEngine.Debug.Log($"AlterOS GTE anchor rebake completed ({device} device).\n{output.Trim()}");
            }
            else
            {
                UnityEngine.Debug.LogError($"AlterOS GTE anchor rebake failed with exit code {exitCode}.\n{error.Trim()}\n{output.Trim()}");
                EditorUtility.DisplayDialog(
                    "AlterOS GTE rebake failed",
                    "The Semantic Lab baker failed. See the Console for the full command output.",
                    "OK");
            }

            MainToolbar.Refresh(ToolbarElementPath);
        }

        static void ReimportAnchors()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(VectorAssetPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(MetadataAssetPath, ImportAssetOptions.ForceSynchronousImport);
            UnityEngine.Debug.Log("AlterOS GTE baked anchor assets reimported.");
        }
    }
}
