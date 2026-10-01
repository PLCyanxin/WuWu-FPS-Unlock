// Inert fixture records completion handoff without opening UI or a game.
if (args.SequenceEqual(new[] { "--update-completed" }))
{
    // The updater starts this child asynchronously; keep the test's handoff barrier exercised.
    Thread.Sleep(250);
    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "completion-fixture.txt"), "completed\n");
    return;
}
Thread.Sleep(2500);
