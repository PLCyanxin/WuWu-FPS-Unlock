internal static partial class Program
{
    static void GameGuardTests(Action<string,Action> test)
    {
        void Reject(Action check){try{check();}catch(IOException){return;}throw new Exception("Expected game guard refusal");}
        test("update game guard accepts absent and exited candidates",()=>{
            CheckGameCandidates([1],_=>null);
            CheckGameCandidates([1],_=>new FakeLauncherProcess{HasExited=true});
        });
        test("update game guard rejects live Shipping without waiting or killing",()=>{
            var process=new FakeLauncherProcess{ExecutablePath=Path.Combine(Path.GetTempPath(),"Client-Win64-Shipping.exe")};
            Reject(()=>CheckGameCandidates([1],_=>process));
            if(!process.Disposed || process.WaitMilliseconds!=0)throw new Exception("Guard must query only");
        });
        test("update game guard rejects inaccessible identity",()=>{
            Reject(()=>CheckGameCandidates([1],_=>throw new System.ComponentModel.Win32Exception(5)));
            Reject(()=>CheckGameCandidates([1],_=>new FakeLauncherProcess{ExecutablePath="relative.exe"}));
        });
        test("update game guard ignores confirmed unrelated reused PID",()=>
            CheckGameCandidates([1],_=>new FakeLauncherProcess{ExecutablePath=Path.Combine(Path.GetTempPath(),"other.exe")}));
    }
}
