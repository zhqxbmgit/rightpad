namespace Rightpad.Receiver;

internal sealed class LeftButtonController : IDisposable
{
    private readonly object gate = new();
    private readonly Action down, up;
    private readonly Action<string> logError;
    private readonly Action onFailure;
    private readonly int holdMs;
    private readonly Timer timer;
    private bool held, disposed;
    private long pendingClicks;
    private Exception? failure;

    public Exception? Failure { get { lock (gate) return failure; } }

    public LeftButtonController(Action down, Action up, Action<string> logError,
        Action onFailure, int clickHoldMs = 25)
    {
        if (clickHoldMs <= 0) throw new ArgumentOutOfRangeException(nameof(clickHoldMs));
        this.down = down;
        this.up = up;
        this.logError = logError;
        this.onFailure = onFailure;
        holdMs = clickHoldMs;
        timer = new Timer(_ => ReleaseDue(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Click()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is not null) throw new IOException("Left button output has failed.", failure);
            // Serialize overlapping clicks so an earlier UP cannot shorten a later hold.
            if (held) { pendingClicks++; return; }
            try { Press(); }
            catch (Exception exception) { Fail(exception); throw; }
        }
    }

    private void Press()
    {
        held = true; // Even an ambiguous DOWN failure gets a best-effort UP.
        down();
        timer.Change(holdMs, Timeout.Infinite);
    }

    private void ReleaseDue()
    {
        lock (gate)
        {
            if (disposed || failure is not null || !held) return;
            try
            {
                up();
                held = false;
                if (pendingClicks > 0)
                {
                    pendingClicks--;
                    Press();
                }
            }
            catch (Exception exception) { Fail(exception); }
        }
    }

    private void Fail(Exception exception)
    {
        failure ??= exception;
        pendingClicks = 0;
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        logError($"left_button_error: {exception.Message}");
        ReleaseBestEffort();
        onFailure(); // Wake the receiving loop even when no new packet arrives.
    }

    private void ReleaseBestEffort()
    {
        if (!held) return;
        try { up(); held = false; }
        catch (Exception exception)
        {
            failure ??= exception;
            logError($"left_button_cleanup_error: {exception.Message}");
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            pendingClicks = 0;
            timer.Dispose();
            ReleaseBestEffort();
        }
    }
}
