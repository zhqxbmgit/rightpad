using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Rightpad.Receiver;

internal interface IFlightRecordStorage : IDisposable { void WriteLine(string line); }

internal sealed class RollingFlightRecordStorage(string directory, long fileSizeLimit = 8 * 1024 * 1024) : IFlightRecordStorage
{
    public const string CurrentFileName = "flight-recorder.log";
    public const string PreviousFileName = "flight-recorder.previous.log";
    private StreamWriter? writer;
    public void WriteLine(string line)
    {
        string current = Path.Combine(directory, CurrentFileName), previous = Path.Combine(directory, PreviousFileName);
        int bytes = Encoding.UTF8.GetByteCount(line) + 1;
        if (writer is not null && writer.BaseStream.Length + bytes > fileSizeLimit)
        {
            writer.Dispose(); writer = null;
            if (File.Exists(previous)) File.Delete(previous);
            if (File.Exists(current)) File.Move(current, previous);
        }
        if (writer is null)
        {
            Directory.CreateDirectory(directory);
            writer = new StreamWriter(new FileStream(current, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false)) { AutoFlush = true };
        }
        writer.WriteLine(line);
    }
    public void Dispose() => writer?.Dispose();
}

internal sealed class FlightRecorder : IDisposable
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "rightpad", "diagnostics");
    public static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(1);
    private readonly BlockingCollection<string> queue;
    private readonly IFlightRecordStorage storage;
    private readonly Func<long> monotonicNow;
    private readonly Func<DateTimeOffset> wallNow;
    private readonly Action<string>? reportError;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task writerTask;
    private Task? snapshotTask;
    private int disabled;
    private long droppedRecords;
    public long DroppedRecords => Interlocked.Read(ref droppedRecords);
    public bool Disabled => Volatile.Read(ref disabled) != 0;

    public FlightRecorder(IFlightRecordStorage storage, int queueCapacity = 256,
        Func<long>? monotonicNow = null, Func<DateTimeOffset>? wallNow = null, Action<string>? reportError = null)
    {
        this.storage = storage; this.monotonicNow = monotonicNow ?? Stopwatch.GetTimestamp;
        this.wallNow = wallNow ?? (() => DateTimeOffset.Now); this.reportError = reportError;
        queue = new(queueCapacity); writerTask = Task.Run(WriteLoop);
    }
    public static FlightRecorder CreateDefault(Action<string>? reportError = null) =>
        new(new RollingFlightRecordStorage(DefaultDirectory), reportError: reportError);
    public void StartSnapshots(Func<RuntimeStatsSnapshot> capture, TimeSpan? interval = null)
    {
        if (snapshotTask is not null) throw new InvalidOperationException("Snapshots already started.");
        snapshotTask = Task.Run(() => SnapshotLoop(capture, interval ?? SnapshotInterval));
    }
    public void Event(string name, params (string Name, object? Value)[] fields)
    {
        var values = BaseRecord("event"); values["event"] = name;
        foreach (var field in fields) values[field.Name] = field.Value;
        Enqueue(JsonSerializer.Serialize(values));
    }
    internal void Snapshot(RuntimeStatsSnapshot s)
    {
        long now = monotonicNow(); var p = s.Presence; var v = BaseRecord("snapshot", now);
        v["runtimeRunId"] = s.RunId; v["runtimeState"] = s.RuntimeState.ToString();
        v["connectionState"] = ConnectionState(s); v["senderRunId"] = p?.RunId is ulong run ? $"{run:X16}" : null;
        v["lastSeenAgeMs"] = Age(now, p?.LastSeenAtTicks ?? 0); v["lastHeartbeatAgeMs"] = Age(now, s.LastHeartbeatAtTicks);
        v["lastTouchDatagramAgeMs"] = Age(now, s.LastTouchDatagramAtTicks); v["lastAcceptedTouchAgeMs"] = Age(now, s.LastAcceptedAtTicks);
        v["lastAcceptedSampleAgeMs"] = Age(now, s.LastAcceptedSampleAtTicks); v["lastMotionOutputAgeMs"] = Age(now, s.LastMotionOutputAtTicks);
        v["lastSuccessfulSendInputAgeMs"] = Age(now, s.LastSuccessfulSendInputAtTicks); v["lastFailedSendInputAgeMs"] = Age(now, s.LastFailedSendInputAtTicks);
        v["receivedPackets"] = s.ReceivedPackets; v["acceptedInputPackets"] = s.AcceptedPackets; v["acceptedSamples"] = s.AcceptedSamples;
        v["heartbeatPackets"] = s.HeartbeatPackets; v["gap"] = s.GapCount; v["old"] = s.OldCount; v["duplicate"] = s.DuplicateCount;
        v["invalid"] = s.InvalidCount; v["outdatedRunPackets"] = s.OutdatedRunPackets; v["presenceTimeouts"] = s.PresenceTimeouts;
        v["inputTimeouts"] = s.InputTimeoutCount; v["activeTouchSessionId"] = s.ActiveTouchSessionId < 0 ? null : s.ActiveTouchSessionId;
        v["motionOutputEvents"] = s.MotionOutputEvents; v["sendInputSuccesses"] = s.SendInputSuccesses;
        v["sendInputFailures"] = s.SendInputFailures; v["lastError"] = s.LastError;
        Enqueue(JsonSerializer.Serialize(v));
    }
    private Dictionary<string, object?> BaseRecord(string type, long? ticks = null) => new()
    { ["type"] = type, ["wallTime"] = wallNow().ToString("O"), ["monotonicTicks"] = ticks ?? monotonicNow(), ["receiverPid"] = Environment.ProcessId };
    private static double? Age(long now, long then) => then == 0 ? null : Math.Max(0, (now - then) * 1000.0 / Stopwatch.Frequency);
    private static string ConnectionState(RuntimeStatsSnapshot s) => s.RuntimeState switch
    { ReceiverState.Error => "Error", not ReceiverState.Running => s.RuntimeState.ToString(), _ when s.Presence?.RunId is null => "Waiting", _ when s.Presence?.Connected == true => "Connected", _ => "Disconnected" };
    private void Enqueue(string line)
    { if (Disabled || queue.IsAddingCompleted || !queue.TryAdd(line)) Interlocked.Increment(ref droppedRecords); }
    private async Task SnapshotLoop(Func<RuntimeStatsSnapshot> capture, TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        try { while (await timer.WaitForNextTickAsync(cancellation.Token).ConfigureAwait(false)) Snapshot(capture()); }
        catch (OperationCanceledException) { } catch (Exception e) { Disable(e); }
    }
    private void WriteLoop()
    {
        try { foreach (string line in queue.GetConsumingEnumerable()) storage.WriteLine(line); }
        catch (Exception e) { Disable(e); } finally { storage.Dispose(); }
    }
    private void Disable(Exception e)
    { if (Interlocked.Exchange(ref disabled, 1) == 0) try { reportError?.Invoke($"flight_recorder_error: {e.Message}"); } catch { } }
    public void Dispose()
    {
        cancellation.Cancel(); try { snapshotTask?.GetAwaiter().GetResult(); } catch { }
        queue.CompleteAdding(); try { writerTask.GetAwaiter().GetResult(); } catch { }
        cancellation.Dispose(); queue.Dispose();
    }
}
