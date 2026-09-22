using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Rightpad.Receiver;

// A lost feedback is preferable to holding up input. No I/O or continuations run in TryEnqueue.
internal sealed class HapticFeedbackSender : IAsyncDisposable
{
    private readonly record struct Pending(IPAddress Address, ClickFeedback Click, long At, byte[]? Config = null);
    private readonly Channel<Pending> queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(32)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly TextWriter output;
    private readonly Func<byte[], IPEndPoint, CancellationToken, ValueTask>? sendForTest;
    private readonly int port;
    private long dropped;
    private Func<StatusDelivery?>? statusSource;

    public void SetStatusSource(Func<StatusDelivery?> source) => Volatile.Write(ref statusSource, source);

    public HapticFeedbackSender(TextWriter output, int port = HapticFeedbackCodec.Port,
        Func<byte[], IPEndPoint, CancellationToken, ValueTask>? sendForTest = null)
    {
        this.output = output;
        this.port = port;
        this.sendForTest = sendForTest;
        worker = Task.Run(RunAsync);
    }

    public void TryEnqueue(IPAddress address, ClickFeedback click)
    {
        if (!queue.Writer.TryWrite(new(address, click, Stopwatch.GetTimestamp()))) Interlocked.Increment(ref dropped);
    }
    public void TryEnqueueConfig(IPAddress address, byte[] snapshot)
    {
        if (!queue.Writer.TryWrite(new(address, default, Stopwatch.GetTimestamp(), snapshot))) Interlocked.Increment(ref dropped);
    }

    private async Task RunAsync()
    {
        long sent = 0, errors = 0;
        try
        {
            using var socket = sendForTest is null ? new UdpClient(AddressFamily.InterNetwork) : null;
            Task<bool>? ready = null;
            Task refresh = Task.Delay(StatusProtocol.RefreshInterval, stop.Token);
            while (!stop.IsCancellationRequested)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (refresh.IsCompleted)
                {
                    // Capture at send time, never queue a route to a historical sender.
                    var status = Volatile.Read(ref statusSource)?.Invoke();
                    if (status is not null)
                    {
                        try
                        {
                            byte[] bytes = StatusProtocol.Encode(status.Snapshot);
                            var target = new IPEndPoint(status.Address, port);
                            if (sendForTest is not null) await sendForTest(bytes, target, stop.Token).ConfigureAwait(false);
                            else await socket!.SendAsync(bytes, target, stop.Token).ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is SocketException or IOException)
                        {
                            if (++errors == 1 || errors % 1024 == 0) output.WriteLine($"status_send_error: count={errors} error={e.Message}");
                        }
                    }
                    refresh = Task.Delay(StatusProtocol.RefreshInterval, stop.Token);
                }
                if (!queue.Reader.TryRead(out var pending))
                {
                    ready ??= queue.Reader.WaitToReadAsync(stop.Token).AsTask();
                    await Task.WhenAny(ready, refresh).ConfigureAwait(false);
                    if (ready.IsCompleted)
                    {
                        if (!await ready.ConfigureAwait(false)) break;
                        ready = null;
                    }
                    continue;
                }
                if (Stopwatch.GetElapsedTime(pending.At) >= TimeSpan.FromSeconds(1))
                { Interlocked.Increment(ref dropped); continue; }
                try
                {
                    byte[] bytes = pending.Config ?? HapticFeedbackCodec.Encode(pending.Click);
                    var target = new IPEndPoint(pending.Address, port);
                    if (sendForTest is not null) await sendForTest(bytes, target, stop.Token).ConfigureAwait(false);
                    else await socket!.SendAsync(bytes, target, stop.Token).ConfigureAwait(false);
                    sent++;
                    if (pending.Config is null)
                        output.WriteLine($"haptic_sent: senderRunId={pending.Click.SenderRunId:X16} sessionId={pending.Click.SessionId} upSequence={pending.Click.UpSequence} target={target} count={sent}");
                    else output.WriteLine($"controls_config_sent: target={target} bytes={bytes.Length}");
                }
                catch (Exception e) when (e is SocketException or IOException)
                {
                    if (++errors == 1 || errors % 1024 == 0) output.WriteLine($"haptic_send_error: count={errors} error={e.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception e)
        {
            // Feedback startup/worker failure never becomes a Receiver or mouse failure.
            output.WriteLine($"haptic_worker_error: {e.Message}");
        }
        finally
        {
            queue.Writer.TryComplete();
            output.WriteLine($"haptic_stats: sent={sent} errors={errors} dropped={Interlocked.Read(ref dropped)}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        stop.Cancel();
        await worker.ConfigureAwait(false);
        stop.Dispose();
    }
}
