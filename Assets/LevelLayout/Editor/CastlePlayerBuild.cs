using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Explicit Windows delivery entry point; never invoked during import or play.
public static class CastlePlayerBuild
{
    public static void BuildForBatch()
    {
        string output = Environment.GetEnvironmentVariable("SHADOWS_PLAYER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output) || !Path.IsPathRooted(output))
            throw new InvalidOperationException("Set SHADOWS_PLAYER_OUTPUT to an absolute .exe path outside Assets.");
        output = Path.GetFullPath(output);
        string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
        if (!output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Windows player must be an .exe outside Assets.");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { CastleLayoutBuilder.ScenePath },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        var summary = report.summary;
        Debug.Log($"Castle Windows build: {summary.result}; errors={summary.totalErrors}; warnings={summary.totalWarnings}; bytes={summary.totalSize}; duration={summary.totalTime}");
        if (summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Castle player build failed: " + summary.result);
    }
}
