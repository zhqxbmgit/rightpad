using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Rightpad.Receiver;

// A lost feedback is preferable to holding up input. No I/O or continuations run in TryEnqueue.
internal sealed class HapticFeedbackSender : IAsyncDisposable
{
    private readonly record struct Pending(IPAddress Address, ClickFeedback Click, long At);
    private readonly Channel<Pending> queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(32)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly TextWriter output;
    private readonly Func<byte[], IPEndPoint, CancellationToken, ValueTask>? sendForTest;
    private readonly int port;
    private long dropped;

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

    private async Task RunAsync()
    {
        long sent = 0, errors = 0;
        try
        {
            using var socket = sendForTest is null ? new UdpClient(AddressFamily.InterNetwork) : null;
            await foreach (var pending in queue.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                stop.Token.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(pending.At) >= TimeSpan.FromSeconds(1))
                { Interlocked.Increment(ref dropped); continue; }
                try
                {
                    byte[] bytes = HapticFeedbackCodec.Encode(pending.Click);
                    var target = new IPEndPoint(pending.Address, port);
                    if (sendForTest is not null) await sendForTest(bytes, target, stop.Token).ConfigureAwait(false);
                    else await socket!.SendAsync(bytes, target, stop.Token).ConfigureAwait(false);
                    sent++;
                    output.WriteLine($"haptic_sent: senderRunId={pending.Click.SenderRunId:X16} sessionId={pending.Click.SessionId} upSequence={pending.Click.UpSequence} target={target} count={sent}");
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
