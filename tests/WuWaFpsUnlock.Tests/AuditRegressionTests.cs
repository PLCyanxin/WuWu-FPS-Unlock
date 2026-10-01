using System.Buffers.Binary;
using WuWaFpsUnlock.Core;

public static class AuditRegressionTests
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
    [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name,string existing,IntPtr reserved);
    public static async Task RunAsync(Func<string,Func<Task>,Task> test)
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","tests","test-work","audit-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        void Check(bool condition){if(!condition)throw new Exception("audit regression failed");}
        async Task Reject<T>(Func<Task> action) where T:Exception {try{await action();}catch(T){return;}throw new Exception("expected "+typeof(T).Name);}
        byte[] Pe()
        {
            var bytes=new byte[1024];bytes[0]=(byte)'M';bytes[1]=(byte)'Z';BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(60),128);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(128),0x4550);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(132),0x8664);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(134),1);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148),240);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(150),0x2022);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(152),0x20b);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(208),8192);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(212),512);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(400),256);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(404),4096);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(408),512);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(412),512);return bytes;
        }
        try
        {
            await test("deployment revalidates target after intent journal and staging",async()=>
            {
                string game=Path.Combine(root,"game"),source=Path.Combine(root,"vendor.dll"),target=Path.Combine(game,"nvngx_dlss.dll");Directory.CreateDirectory(game);
                File.WriteAllText(source,"new payload");File.WriteAllText(target,"approved old file");
                var plan=new PlannedFile(source,target,await SafePaths.HashAsync(source),new FileInfo(source).Length,PayloadKind.Vendor,await SafePaths.HashAsync(target));
                var receipt=new DeploymentReceipt{GameRoot=game};bool changed=false;
                await Reject<IOException>(()=>DeploymentFiles.ApplyAsync([plan],receipt,()=>{if(!changed&&receipt.Files.Any(f=>!f.Completed)){changed=true;File.WriteAllText(target,"concurrent user change");}},_=>{}));
                Check(File.ReadAllText(target)=="concurrent user change"&&!receipt.Files.Single().Completed&&!receipt.Files.Single().ReplacedByTool);
            });
            await test("materials inside game cannot become deployment or cleanup targets",async()=>
            {
                string game=Path.Combine(root,"nested-game"),package=Path.Combine(game,"launcher","payload");Directory.CreateDirectory(package);
                string source=Path.Combine(package,"nvngx_dlss.dll");File.WriteAllText(source,"source stays intact");
                var manifest=new PayloadManifest{PackageId="fixture",Files=[new(){Source="nvngx_dlss.dll",Target="nvngx_dlss.dll",Kind=PayloadKind.Vendor},new(){Source="addon.bin",Target="renodx-mfgunlock.addon64",Kind=PayloadKind.Addon,Anchor=TargetAnchor.AddonDir}]};
                string path=Path.Combine(package,"manifest.json");JsonFiles.Save(path,manifest);
                await Reject<InvalidDataException>(()=>PackageReader.PlanAsync(manifest,path,game,game,game));
                await Reject<InvalidDataException>(()=>UserMaterialRemoval.PlanAsync(path,game));Check(File.ReadAllText(source)=="source stays intact");
            });
            await test("runtime materials accept unsigned x64 DLL and reject invalid PE images",async()=>
            {
                string file=Path.Combine(root,"validated.dll");File.WriteAllBytes(file,Pe());MaterialSafety.RequireX64Dll(file);
                var inputs=new List<byte[]>{Array.Empty<byte>(),System.Text.Encoding.UTF8.GetBytes("not a DLL"),Pe()[..300]};
                var x86=Pe();BinaryPrimitives.WriteUInt16LittleEndian(x86.AsSpan(132),0x14c);inputs.Add(x86);
                var exe=Pe();BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(150),0x22);inputs.Add(exe);
                var truncatedSection=Pe();BinaryPrimitives.WriteUInt32LittleEndian(truncatedSection.AsSpan(408),4096);inputs.Add(truncatedSection);
                foreach(var bytes in inputs){File.WriteAllBytes(file,bytes);await Reject<InvalidDataException>(()=>{MaterialSafety.RequireX64Dll(file);return Task.CompletedTask;});}
            });
            await test("hardlinked source and game target are rejected without deleting source",()=>
            {
                if(!OperatingSystem.IsWindows())return Task.CompletedTask;
                string source=Path.Combine(root,"hardlink-source.dll"),target=Path.Combine(root,"hardlink-target.dll");File.WriteAllText(source,"same physical file");
                Check(CreateHardLinkW(target,source,IntPtr.Zero));
                try{MaterialSafety.RequireDifferentFiles(source,target);throw new Exception("accepted identical hardlink");}catch(InvalidDataException){}
                Check(File.Exists(source)&&File.Exists(target));return Task.CompletedTask;
            });
            await test("current packaged DLLs and addon pass structural validation without fixed old hashes",()=>
            {
                string manifest=Path.GetFullPath("payload/manifest.json");PackageManifestLocation.RequireSources(PackageReader.Load(manifest),manifest);
                MaterialSafety.RequireX64Dll(Path.GetFullPath("release-assets/dynamicmax/wuwa-dynamicmax.addon64"));return Task.CompletedTask;
            });
            await test("atomic checksummed state survives torn legacy metadata",()=>
            {
                string state=Path.Combine(root,"state.json");DurableJson.Save(state,new[]{"old"});DurableJson.Save(state,new[]{"new"});Check(DurableJson.Read<string[]>(state).Single()=="new");
                File.AppendAllText(state,"corrupt");try{DurableJson.Read<string[]>(state);throw new Exception("accepted corruption");}catch(InvalidDataException){}return Task.CompletedTask;
            });
            async Task<BaselineRebuildPlan> RebuildFixture()
            {
                string directory=Path.Combine(root,Guid.NewGuid().ToString("N")),game=Path.Combine(directory,"game");Directory.CreateDirectory(game);
                string exe=Path.Combine(game,"Client-Win64-Shipping.exe"),oldFile=Path.Combine(game,"old","nvngx_dlss.dll"),newFile=Path.Combine(game,"new","nvngx_dlss.dll");
                File.WriteAllText(exe,"inert Shipping identity");Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);File.WriteAllText(oldFile,"library fixture");
                string baseline=Path.Combine(directory,"baseline.json"),receipt=Path.Combine(directory,"receipt.json");
                GameFileBaselineStore.MergeAndSave(baseline,await GameFileBaselineStore.CaptureAsync(game,exe));
                JsonFiles.Save(receipt,new DeploymentReceipt{GameRoot=game,GameExe=exe,Status="Deployed",Files=[new(){Path=oldFile,Kind="Vendor",Completed=true,ReplacedByTool=true,InstalledHash=await SafePaths.HashAsync(oldFile)}]});
                Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);File.Move(oldFile,newFile);
                return await BaselineRebuild.PrepareAsync(baseline,receipt,game,exe);
            }
            await test("explicit baseline rebuild archives moved paths and obsolete receipt without changing game bytes",async()=>
            {
                var plan=await RebuildFixture();Check(!GameFileBaselineStore.Check(plan.Previous,plan.Current.GameRoot,plan.Current.GameExe).IsComplete);
                string file=SafePaths.Under(plan.Current.GameRoot,plan.Current.Files.Single().RelativePath),hash=await SafePaths.HashAsync(file);
                string archive=await BaselineRebuild.CommitAsync(plan,()=>{});
                Check(File.Exists(archive)&&await SafePaths.HashAsync(file)==hash&&JsonFiles.Read<DeploymentReceipt>(plan.ReceiptPath).Files.Count==0);
                GameFileBaselineStore.RequirePresent(JsonFiles.Read<GameFileBaseline>(plan.BaselinePath),plan.Current.GameRoot,plan.Current.GameExe);
            });
            await test("baseline rebuild rejects wrong installation and post-preview changes",async()=>
            {
                var plan=await RebuildFixture();string baseline=await SafePaths.HashAsync(plan.BaselinePath),receipt=await SafePaths.HashAsync(plan.ReceiptPath);
                await Reject<InvalidDataException>(()=>BaselineRebuild.PrepareAsync(plan.BaselinePath,plan.ReceiptPath,root,Path.Combine(root,"different.exe")));
                File.AppendAllText(SafePaths.Under(plan.Current.GameRoot,plan.Current.Files.Single().RelativePath),"changed during confirmation");
                await Reject<IOException>(()=>BaselineRebuild.CommitAsync(plan,()=>{}));
                Check(await SafePaths.HashAsync(plan.BaselinePath)==baseline&&await SafePaths.HashAsync(plan.ReceiptPath)==receipt);
            });
            await test("failed baseline commit restores the preceding receipt and keeps original baseline",async()=>
            {
                var plan=await RebuildFixture();string baseline=await SafePaths.HashAsync(plan.BaselinePath),receipt=await SafePaths.HashAsync(plan.ReceiptPath);
                using(var held=new FileStream(plan.BaselinePath,FileMode.Open,FileAccess.Read,FileShare.Read))await Reject<UnauthorizedAccessException>(()=>BaselineRebuild.CommitAsync(plan,()=>{}));
                Check(await SafePaths.HashAsync(plan.BaselinePath)==baseline&&await SafePaths.HashAsync(plan.ReceiptPath)==receipt);
            });
        }
        finally{Directory.Delete(root,true);}
    }
}
