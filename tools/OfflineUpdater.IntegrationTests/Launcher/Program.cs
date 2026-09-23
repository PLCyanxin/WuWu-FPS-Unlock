// Inert fixture records completion handoff without opening UI or a game.
if (args.SequenceEqual(new[] { "--update-completed" }))
{
    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "completion-fixture.txt"), "completed\n");
    return;
}
Thread.Sleep(2500);
