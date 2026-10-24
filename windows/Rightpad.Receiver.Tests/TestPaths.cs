using System.Reflection;

namespace Rightpad.Receiver.Tests;

internal static class TestPaths
{
    private static string Metadata(string key) => typeof(TestPaths).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value
        ?? throw new InvalidOperationException($"Missing build path metadata: {key}");

    public static string SourceRoot
    {
        get
        {
            string path = Metadata("RightpadSourceRoot");
            if (!File.Exists(Path.Combine(path, "AGENTS.md")))
                throw new DirectoryNotFoundException($"Test source root is unavailable: {path}");
            return path;
        }
    }

    public static string EvidenceDirectory => Directory.CreateDirectory(Path.Combine(
        Metadata("RightpadArtifactsRoot"), "test-results", Metadata("RightpadArtifactScope"), "evidence")).FullName;
}
