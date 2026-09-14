using WuWaFpsUnlock.Core;

public static class GameFileBaselineTests
{
    public static async Task RunAsync(Func<string,Func<Task>,Task> test)
    {
        static void Check(bool value){if(!value)throw new Exception("baseline assertion failed");}
        static (string root,string exe,string dll) Fixture()
        {
            string root=Path.GetFullPath(Path.Combine("artifacts","baseline-test",Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Path.Combine(root,"engine"));
            string exe=Path.Combine(root,"Client-Win64-Shipping.exe"),dll=Path.Combine(root,"engine","nvngx_dlss.dll");
            File.WriteAllText(exe,"inert fake shipping");File.WriteAllText(dll,"inert initial dll");
            return(root,exe,dll);
        }
        await test("baseline captures only existing mapped files; absent names are reference only",async()=>{var x=Fixture();var b=await GameFileBaselineStore.CaptureAsync(x.root,x.exe);Check(b.Files.Count==1&&b.AbsentNamesForReferenceOnly.Count==17&&b.Files[0].RelativePath==Path.GetRelativePath(x.root,x.dll));Check(GameFileBaselineStore.Check(b,x.root,x.exe).IsComplete);});
        await test("baseline missing known path blocks with exact repair instruction",async()=>{var x=Fixture();var b=await GameFileBaselineStore.CaptureAsync(x.root,x.exe);File.Delete(x.dll);var result=GameFileBaselineStore.Check(b,x.root,x.exe);Check(!result.IsComplete&&result.MissingPaths.SequenceEqual(new[]{x.dll})&&result.Message.StartsWith("检测到文件缺失，请在鸣潮官方启动器启动一次游戏完成游戏文件修复",StringComparison.Ordinal));try{GameFileBaselineStore.RequirePresent(b,x.root,x.exe);throw new Exception("missing baseline not blocked");}catch(IOException){}Check(!File.Exists(x.dll));});
        await test("baseline later incomplete scan cannot shrink persisted requirements",async()=>{var x=Fixture();string state=Path.Combine(x.root,"state","baseline.json");var first=await GameFileBaselineStore.CaptureAsync(x.root,x.exe);GameFileBaselineStore.MergeAndSave(state,first);File.Delete(x.dll);var observed=await GameFileBaselineStore.CaptureAsync(x.root,x.exe);Check(observed.Files.Count==0);var merged=GameFileBaselineStore.MergeAndSave(state,observed);Check(merged.Files.Count==1&&!GameFileBaselineStore.Check(merged,x.root,x.exe).IsComplete);});
        await test("baseline adds newly observed paths but retains original hash evidence",async()=>{var x=Fixture();string state=Path.Combine(x.root,"state","baseline.json");var first=await GameFileBaselineStore.CaptureAsync(x.root,x.exe);var initialHash=first.Files[0].Sha256;GameFileBaselineStore.MergeAndSave(state,first);File.WriteAllText(x.dll,"official repair may change hash");File.WriteAllText(Path.Combine(x.root,"engine","sl.common.dll"),"additional");var merged=GameFileBaselineStore.MergeAndSave(state,await GameFileBaselineStore.CaptureAsync(x.root,x.exe));Check(merged.Files.Count==2&&merged.Files.Single(f=>f.RelativePath==first.Files[0].RelativePath).Sha256==initialHash&&GameFileBaselineStore.Check(merged,x.root,x.exe).IsComplete);});
        await test("baseline cannot be reused for another installation",async()=>{var x=Fixture();var y=Fixture();var b=await GameFileBaselineStore.CaptureAsync(x.root,x.exe);try{GameFileBaselineStore.Check(b,y.root,y.exe);throw new Exception("identity accepted");}catch(InvalidDataException){}});
    }
}
