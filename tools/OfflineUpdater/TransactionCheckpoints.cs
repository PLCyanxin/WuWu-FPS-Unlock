internal static partial class Program
{
    // Unimplemented in production; erased by the compiler. Integration fixtures
    // terminate only their own inert updater to exercise OS-level interruption.
    static partial void UpdateCommitCheckpoint(int count);
    static partial void SnapshotStateCheckpoint(bool complete);
}
