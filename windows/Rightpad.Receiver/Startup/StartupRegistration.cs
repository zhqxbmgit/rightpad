using Microsoft.Win32;

namespace Rightpad.Receiver;

internal interface IStartupValueStore
{
    string? Read(string name);
    void Write(string name, string command);
    void Delete(string name);
}

internal sealed class WindowsStartupValueStore : IStartupValueStore
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup key.");
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

internal sealed record StartupState(bool Enabled, string Notice = "");

internal sealed class StartupRegistration(IStartupValueStore store, string executablePath)
{
    internal const string ValueName = "rightpad Receiver";
    internal string Command { get; } = QuoteExecutable(executablePath);

    public StartupState Read()
    {
        try { return new(IsCurrentExecutable(store.Read(ValueName))); }
        catch { return new(false, "Could not read Windows startup."); }
    }

    public StartupState SetEnabled(bool enabled)
    {
        string notice = "";
        try
        {
            if (enabled) store.Write(ValueName, Command);
            else store.Delete(ValueName);
        }
        catch { notice = "Could not update Windows startup."; }

        var actual = Read();
        return actual with { Notice = notice.Length == 0 ? actual.Notice : notice };
    }

    private bool IsCurrentExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        string trimmed = command.Trim();
        string? parsed = ParseExecutable(trimmed);
        if (parsed is null && !trimmed.Contains('"')) parsed = trimmed;
        try
        {
            return parsed is not null && string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(parsed)),
                Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static string QuoteExecutable(string path) => $"\"{path}\"";

    internal static string? ParseExecutable(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return null;
        if (command[0] == '"')
        {
            int closing = command.IndexOf('"', 1);
            return closing > 1 ? command[1..closing] : null;
        }
        int separator = command.IndexOfAny([' ', '\t']);
        return separator < 0 ? command : command[..separator];
    }
}
