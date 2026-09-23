namespace WuWaFpsUnlock.Core;

/// <summary>UI-thread owned, session-only reservation for a specific immutable release.</summary>
public sealed class DeferredUpdateSession
{
    public UpdateRelease? Pending { get; private set; }
    public bool AttemptInProgress { get; private set; }
    private bool _awaitingGameExit;
    public void Queue(UpdateRelease release, bool gameRunning)
    {
        if (AttemptInProgress) throw new InvalidOperationException("更新预约正在处理。");
        Pending = release; _awaitingGameExit = gameRunning;
    }
    public void GameStarted() { if (Pending is not null) _awaitingGameExit = true; }
    public bool TryBeginAfterGameExit(out UpdateRelease? release)
    {
        release = null;
        if (Pending is null || !_awaitingGameExit || AttemptInProgress) return false;
        _awaitingGameExit = false; AttemptInProgress = true; release = Pending; return true;
    }
    public void CompleteAttempt(bool handedOff)
    {
        AttemptInProgress = false;
        if (handedOff) { Pending = null; _awaitingGameExit = false; }
    }
    public void Cancel() { Pending = null; _awaitingGameExit = false; }
}
