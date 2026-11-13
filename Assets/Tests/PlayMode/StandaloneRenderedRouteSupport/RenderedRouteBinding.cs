#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace ForsakenStandaloneRenderedRouteSupport
{
    public static class RenderedRouteBinding
    {
        public const string Profile = "rendered-routes", UnityVersion = "6000.6.3f1";
        public const string MainCase = "ShadowsOfTheForsaken.Tests.PlayMode.FullCastleRouteTests.CastleMainRouteCompletesThroughControlsWithoutSecret";
        public const string SecretCase = "ShadowsOfTheForsaken.Tests.PlayMode.FullCastleRouteTests.CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight";
        public const string Filter = @"^ShadowsOfTheForsaken\.Tests\.PlayMode\.FullCastleRouteTests\.(CastleMainRouteCompletesThroughControlsWithoutSecret|CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight)$";
        public static readonly string[] ExpectedCases = { MainCase, SecretCase };
        public static readonly string[] Conflicts = { "SHADOWS_ROUTE_PLAYER_BUILD", "SHADOWS_ROUTE_PLAYER_OUTPUT",
            "SHADOWS_AUDIO_PLAYER_BUILD", "SHADOWS_AUDIO_PLAYER_OUTPUT", "SHADOWS_AUDIO_PROBE_OUTPUT",
            "SHADOWS_PRESENTATION_PLAYER_BUILD", "SHADOWS_PRESENTATION_PLAYER_OUTPUT",
            "SHADOWS_PLAYER_OUTPUT", "SHADOWS_PLAYER_EVIDENCE_DIR" };
        private static readonly Dictionary<string, RenderedCaseFile> Closed = new Dictionary<string, RenderedCaseFile>(StringComparer.Ordinal);
        public static bool Active => !Application.isEditor && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHADOWS_RENDERED_ROUTE_OUTPUT"));
        public static string Utc() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        public static int Pid() { using (var process = System.Diagnostics.Process.GetCurrentProcess()) return process.Id; }
        public static string CaseKey(string name) => name == MainCase ? "main" : name == SecretCase ? "secret" :
            throw new InvalidOperationException("Unexpected rendered-route test case: " + name);

        public static string Output()
        {
            if (!Active || Application.platform != RuntimePlatform.WindowsPlayer || Application.unityVersion != UnityVersion ||
                !Debug.isDebugBuild || Application.isBatchMode)
                throw new InvalidOperationException("Rendered routes require the pinned graphical Windows Development test player.");
            foreach (string variable in Conflicts)
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
                    throw new InvalidOperationException("Conflicting rendered-route environment: " + variable);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHADOWS_RENDERED_ROUTE_BUILD")))
                throw new InvalidOperationException("The runtime cannot retain its build override.");
            string output = LocalPath(Environment.GetEnvironmentVariable("SHADOWS_RENDERED_ROUTE_OUTPUT"));
            string captures = LocalPath(Environment.GetEnvironmentVariable("SHADOWS_CAPTURE_DIR"));
            if (!string.Equals(captures, Path.Combine(output, "captures"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Rendered-route captures must belong to this fresh output.");
            Directory.CreateDirectory(output); return output;
        }

        public static string LocalPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || !Path.IsPathRooted(raw) || raw.Length < 3 || raw[1] != ':' ||
                (raw[2] != '\\' && raw[2] != '/') || raw.StartsWith(@"\\", StringComparison.Ordinal))
                throw new InvalidOperationException("Rendered evidence requires an absolute local drive path.");
            string path = Path.GetFullPath(raw);
            for (var parent = new DirectoryInfo(path); parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Rendered evidence cannot traverse a linked directory.");
            return path;
        }

        public static void SaveNew(string output, string name, object value)
        {
            if (Path.GetFileName(name) != name) throw new InvalidOperationException("Invalid evidence file name.");
            string json = JsonUtility.ToJson(value, true);
            if (Encoding.UTF8.GetByteCount(json) > 32 * 1024 * 1024) throw new InvalidOperationException("Rendered evidence exceeds 32 MiB.");
            using (var stream = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(json);
        }
        public static string Sha(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path)) return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void RegisterClosed(string key, bool valid, string output)
        {
            CaseKey(key == "main" ? MainCase : key == "secret" ? SecretCase : key);
            if (Closed.ContainsKey(key) || Closed.Count >= 2) throw new InvalidOperationException("Duplicate rendered case closure.");
            string ready = "case-" + key + "-ready.json", result = "case-" + key + ".json";
            Closed.Add(key, new RenderedCaseFile { case_key = key, ready_file = ready, ready_sha256 = Sha(Path.Combine(output, ready)),
                evidence_file = result, evidence_sha256 = Sha(Path.Combine(output, result)), valid = valid });
        }
        public static RenderedCaseFile[] CaseFiles()
        {
            var result = new List<RenderedCaseFile>();
            foreach (string key in new[] { "main", "secret" }) if (Closed.TryGetValue(key, out var value)) result.Add(value);
            return result.ToArray();
        }
    }
    [Serializable] public sealed class RenderedCaseFile
    { public string case_key, ready_file, ready_sha256, evidence_file, evidence_sha256; public bool valid; }
}
#endif
