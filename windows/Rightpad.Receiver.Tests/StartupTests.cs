using System.Net;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class StartupTests
{
    private const string Current = @"C:\Program Files\rightpad 测试\Rightpad.Receiver.exe";

    private sealed class MemoryStore : IStartupValueStore
    {
        internal readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
        internal int Writes;
        internal bool FailWrite;

        public string? Read(string name) => Values.GetValueOrDefault(name);

        public void Write(string name, string command)
        {
            if (FailWrite) throw new UnauthorizedAccessException("test failure");
            Writes++;
            Values[name] = command;
        }

        public void Delete(string name) => Values.Remove(name);
    }

    private static StartupRegistration Registration(MemoryStore store) => new(store, Current);

    public static void AbsentIsOff()
    {
        var registration = Registration(new());
        Check(!registration.Read().Enabled, "absent entry is off");
    }

    public static void CurrentExecutableIsOn()
    {
        var store = new MemoryStore();
        store.Values[StartupRegistration.ValueName] = $"\"{Current}\" --ignored-argument";
        Check(Registration(store).Read().Enabled, "quoted current executable is on");
    }

    public static void StaleOrWrongIsOff()
    {
        var store = new MemoryStore();
        var registration = Registration(store);
        foreach (string value in new[] { "\"C:\\old\\Rightpad.Receiver.exe\"", "\"C:\\other.exe\"", "\"unterminated" })
        {
            store.Values[StartupRegistration.ValueName] = value;
            Check(!registration.Read().Enabled, $"wrong command is off: {value}");
        }
    }

    public static void EnableWritesCurrentCommand()
    {
        var store = new MemoryStore();
        var state = Registration(store).SetEnabled(true);
        Check(state.Enabled, "enabled after write");
        Equal($"\"{Current}\"", store.Values[StartupRegistration.ValueName], "current command");
    }

    public static void EnableIsIdempotent()
    {
        var store = new MemoryStore();
        var registration = Registration(store);
        registration.SetEnabled(true);
        registration.SetEnabled(true);
        Equal($"\"{Current}\"", store.Values[StartupRegistration.ValueName], "same command after repeated enable");
        Equal(2, store.Writes, "safe repeated registry set");
    }

    public static void EnableReplacesStalePath()
    {
        var store = new MemoryStore();
        store.Values[StartupRegistration.ValueName] = "\"C:\\old\\Rightpad.Receiver.exe\"";
        Check(Registration(store).SetEnabled(true).Enabled, "stale entry repaired");
        Equal($"\"{Current}\"", store.Values[StartupRegistration.ValueName], "stale command replaced");
    }

    public static void DisableDeletesOnlyRightpadValue()
    {
        var store = new MemoryStore();
        store.Values[StartupRegistration.ValueName] = $"\"{Current}\"";
        store.Values["Other App"] = @"C:\Other.exe";
        Check(!Registration(store).SetEnabled(false).Enabled, "disabled after delete");
        Check(!store.Values.ContainsKey(StartupRegistration.ValueName), "rightpad value deleted");
        Equal(@"C:\Other.exe", store.Values["Other App"], "other startup value preserved");
    }

    public static void DisableAbsentIsSafe()
    {
        Check(!Registration(new()).SetEnabled(false).Enabled, "absent delete remains off");
    }

    public static async Task WriteFailureDoesNotAffectRuntime()
    {
        var store = new MemoryStore { FailWrite = true };
        var startup = new StartupViewModel(Registration(store));
        var runtime = new ReceiverRuntime(new RuntimeSettingsStore(), TextWriter.Null, MouseBackend.SendInput,
            new IPEndPoint(IPAddress.Loopback, 0), mouseFactory: () => new WindowsMouseOutput((uint count, ref WindowsMouseOutput.NativeInput input, int size) => 1, () => 0));
        await runtime.StartAsync();
        try
        {
            startup.SetEnabled(true);
            Equal(ReceiverState.Running, runtime.CaptureSnapshot().RuntimeState, "runtime remains running");
            Check(!startup.Enabled, "toggle returns to registry truth");
            Equal("Could not update Windows startup.", startup.Notice, "nonblocking error notice");
        }
        finally { await runtime.StopAsync(); }
    }

    public static void CommandQuotingHandlesSpacesAndUnicode()
    {
        string command = StartupRegistration.QuoteExecutable(Current);
        Equal($"\"{Current}\"", command, "quoted command");
        Equal(Current, StartupRegistration.ParseExecutable(command), "quoted path parsed");
        Equal(Current, StartupRegistration.ParseExecutable(command + " --argument"), "path parsed with argument");
        Check(Registration(new MemoryStore
        {
            Values = { [StartupRegistration.ValueName] = command }
        }).Read().Enabled, "unicode path matches");
    }
}
