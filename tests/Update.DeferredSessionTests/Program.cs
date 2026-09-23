using WuWaFpsUnlock.Core;

var first = Release("1.2.2RC");
var second = Release("1.2.3RC");
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    ++passed; Console.WriteLine("PASS " + name);
}
void Refuses(Action action, string name)
{
    bool refused = false;
    try { action(); } catch (InvalidOperationException) { refused = true; }
    Check(refused, name);
}

var empty = new DeferredUpdateSession();
Check(empty.Pending is null && !empty.AttemptInProgress, "new launcher session has no persisted reservation");
Check(!empty.TryBeginAfterGameExit(out var none) && none is null, "unreserved game exit does not request an update");

var queued = new DeferredUpdateSession();
queued.Queue(first, true);
Check(ReferenceEquals(queued.Pending, first) && !queued.AttemptInProgress, "queue retains exact accepted release without starting work");
Check(queued.TryBeginAfterGameExit(out var selected) && ReferenceEquals(selected, first), "first game exit consumes one queued attempt");
Check(queued.AttemptInProgress && queued.Pending is not null, "pending remains visible while attempt runs to suppress normal auto-exit");
Check(!queued.TryBeginAfterGameExit(out none) && none is null, "duplicate game exit cannot begin a concurrent attempt");
Refuses(() => queued.Queue(second, true), "active attempt cannot be replaced by another release");
queued.CompleteAttempt(false);
Check(!queued.AttemptInProgress && ReferenceEquals(queued.Pending, first), "cancel or failure retains reservation for manual retry");
Check(!queued.TryBeginAfterGameExit(out none), "duplicate exit after failure cannot reopen download dialog");
queued.GameStarted();
Check(queued.TryBeginAfterGameExit(out selected) && ReferenceEquals(selected, first), "a later game session rearms one attempt");
queued.CompleteAttempt(true);
Check(queued.Pending is null && !queued.AttemptInProgress, "successful worker handoff clears session reservation");
Check(!queued.TryBeginAfterGameExit(out none), "success does not trigger a second handoff");

var later = new DeferredUpdateSession();
later.Queue(first, false);
Check(!later.TryBeginAfterGameExit(out none), "reservation without current game waits for a future game session");
later.GameStarted();
Check(later.TryBeginAfterGameExit(out selected), "future started game enables its exit attempt");
later.CompleteAttempt(false);
later.Queue(second, false);
Check(ReferenceEquals(later.Pending, second), "idle pending release can be deliberately replaced");
Check(!later.TryBeginAfterGameExit(out none), "replaced no-game reservation does not reuse an earlier exit");
later.Cancel();
later.GameStarted();
Check(later.Pending is null && !later.TryBeginAfterGameExit(out none), "cancelled reservation does not revive with a game session");

var manual = new DeferredUpdateSession();
manual.Queue(first, true);
manual.TryBeginAfterGameExit(out _);
manual.CompleteAttempt(false);
// The UI handles explicit retry; it must clear reservation only on a real worker handoff.
manual.CompleteAttempt(true);
Check(manual.Pending is null && !manual.AttemptInProgress, "successful explicit retry clears retained reservation");

var cancelledDuringAttempt = new DeferredUpdateSession();
cancelledDuringAttempt.Queue(first, true);
cancelledDuringAttempt.TryBeginAfterGameExit(out _);
cancelledDuringAttempt.Cancel();
cancelledDuringAttempt.CompleteAttempt(false);
Check(cancelledDuringAttempt.Pending is null && !cancelledDuringAttempt.AttemptInProgress,
    "completion after reservation cancellation cannot resurrect pending work");
Check(!new DeferredUpdateSession().TryBeginAfterGameExit(out _), "separate launcher sessions never inherit another session reservation");

Console.WriteLine($"{passed}/{passed} deferred-session fixture checks passed. No game/process/UI/network/updater execution; these are state-transition checks only.");
static UpdateRelease Release(string version) => new("v" + version, version, "fixture notes",
    new Uri("https://example.invalid/package.zip"), new Uri("https://example.invalid/SHA256SUMS.txt"), "package.zip");
