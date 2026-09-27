using WuWaFpsUnlock.Services;
// The old external-unlocker worker is retired. Keep this project as a regression
// check that no compiled external launch/UAC route returns to the launcher.
var assembly=typeof(BuiltinFpsService).Assembly;
foreach(string name in new[]{"ExternalUnlockerService","ExternalLaunchWorker"})
{
    if(assembly.GetType("WuWaFpsUnlock.Services."+name) is not null)
        throw new Exception("Retired launch route is compiled: "+name);
    Console.WriteLine("PASS retired type absent: "+name);
}
if(!string.Equals(GameProcesses.ImagePath(Environment.ProcessId),Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))
    throw new Exception("Read-only current-process identity mismatch");
Console.WriteLine("PASS current process identity query; no UAC/game/unlocker executed");
