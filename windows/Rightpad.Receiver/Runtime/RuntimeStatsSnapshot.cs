namespace Rightpad.Receiver;

internal enum ReceiverState { Stopped, Starting, Running, Stopping, Error }

// Independent atomic observations for display, not a transaction or an input decision.
internal sealed record RuntimeStatsSnapshot(long RunId, ReceiverState RuntimeState,
    long LastAcceptedAtTicks = 0, long ReceivedPackets = 0, long AcceptedPackets = 0,
    long AcceptedSamples = 0, long GapCount = 0, long OldCount = 0, long DuplicateCount = 0,
    long InvalidCount = 0, long InputTimeoutCount = 0, long ActiveTouchSessionId = -1,
    string? LastRemoteIp = null, string MouseBackend = "SendInput", string? LastError = null,
    SenderPresence? Presence = null, long HeartbeatPackets = 0, long OutdatedRunPackets = 0,
    long PresenceTimeouts = 0, long LastHeartbeatAtTicks = 0, long LastTouchDatagramAtTicks = 0,
    long LastAcceptedSampleAtTicks = 0, long MotionOutputEvents = 0, long LastMotionOutputAtTicks = 0,
    long SendInputSuccesses = 0, long SendInputFailures = 0,
    long LastSuccessfulSendInputAtTicks = 0, long LastFailedSendInputAtTicks = 0);
