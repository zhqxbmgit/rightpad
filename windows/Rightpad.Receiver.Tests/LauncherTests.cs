using System.Diagnostics;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class LauncherTests
{
    public static async Task Arguments()
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(AppContext.BaseDirectory, "LauncherTests.ps1") }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not run launcher argument tests.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch { if (!process.HasExited) process.Kill(); throw; }
        Equal(0, process.ExitCode, $"launcher argument tests: {await output} {await error}");
    }
}
