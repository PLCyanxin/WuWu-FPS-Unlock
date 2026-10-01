using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

string root=Path.Combine(Path.GetTempPath(),"ww-menu-key-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Fixture.Root=root;
int count=0;
void Check(bool value,string reason){if(!value)throw new Exception(reason);count++;Console.WriteLine("PASS "+reason);}
async Task Reject<T>(Func<Task> run)where T:Exception{try{await run();}catch(T){count++;return;}throw new Exception("Expected "+typeof(T).Name);}
string path=Path.Combine(root,"ReShade.ini");
var settings=new UserSettings{GameRoot=root,GameExe=Path.Combine(root,"game.exe")};
try
{
    Check(MenuShortcut.Read(IniDocument.Load(path))==MenuShortcut.Home,"absent key uses native Home default");
    var selected=new MenuShortcut(112,true,false,true);
    Check(selected.ToIniValue()=="112,1,0,1"&&selected.DisplayName=="Ctrl + Alt + F1","native modifier order and displayed combination agree");
    foreach(int key in new[]{1,16,17,18,91,92,255})Check(!new MenuShortcut(key).IsValid,"invalid key rejected "+key);
    Check(MenuShortcut.None.IsValid&&MenuShortcut.None.ToIniValue()=="0,0,0,0"&&MenuShortcut.None.DisplayName=="未设置","clear uses ReShade's disabled binding instead of unspecified preference");
    Check(!new MenuShortcut(0,Control:true).IsValid,"cleared binding cannot contain modifiers");
    Check(!new MenuShortcut(115,Alt:true).IsValid&&!new MenuShortcut(46,Control:true,Alt:true).IsValid,"system shortcuts cannot be bound");
    File.WriteAllText(path,"; keep\n[INPUT]\nKeyOverlay=36,0,0,0\nKeyEffects=118,0,0,0\n[USER]\nKeep=abc\n");
    byte[] original=File.ReadAllBytes(path);
    await MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default);
    Check(File.ReadAllBytes(path).SequenceEqual(original)&&Fixture.Writes==0,"unmodified preference preserves existing config byte for byte");
    settings.MenuShortcut=selected;
    JsonFiles.Save(Path.Combine(root,"settings.json"),settings);
    Check(JsonFiles.Read<UserSettings>(Path.Combine(root,"settings.json")).MenuShortcut==selected&&settings.Clone().MenuShortcut==selected,"custom key persists through save and launch snapshot");
    await MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default);
    var ini=IniDocument.Load(path);
    Check(MenuShortcut.Read(ini)==selected&&ini.Get("INPUT","KeyEffects")=="118,0,0,0"&&ini.Get("USER","Keep")=="abc","launch applies only menu shortcut and preserves other settings");
    int writes=Fixture.Writes;byte[] applied=File.ReadAllBytes(path);
    await MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default);
    Check(Fixture.Writes==writes&&File.ReadAllBytes(path).SequenceEqual(applied),"unchanged shortcut has no additional config write");
    settings.MenuShortcut=MenuShortcut.None;
    JsonFiles.Save(Path.Combine(root,"settings.json"),settings);
    Check(JsonFiles.Read<UserSettings>(Path.Combine(root,"settings.json")).MenuShortcut==MenuShortcut.None,"clear persists through saved launcher settings");
    await MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default);
    Check(MenuShortcut.Read(IniDocument.Load(path))==MenuShortcut.None&&IniDocument.Load(path).Get("INPUT","KeyEffects")=="118,0,0,0","clear disables only the menu key and keeps other keys");
    settings.MenuShortcut=MenuShortcut.Home;
    await MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default);
    Check(MenuShortcut.Read(IniDocument.Load(path))==MenuShortcut.Home,"default restoration takes effect in actual INI");
    Fixture.Running=true;
    await Reject<IOException>(()=>MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default));Fixture.Running=false;
    Check(MenuShortcut.Read(IniDocument.Load(path))==MenuShortcut.Home,"running game prevents writes");
    settings.MenuShortcut=selected;Fixture.BeforeWrite=()=>Fixture.Running=true;
    await Reject<IOException>(()=>MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default));Fixture.Running=false;Fixture.BeforeWrite=null;
    Check(MenuShortcut.Read(IniDocument.Load(path))==MenuShortcut.Home,"game starting after preflight still prevents writes");
    Fixture.BeforeWrite=()=>File.AppendAllText(path,"External=changed\n");
    await Reject<IOException>(()=>MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default));Fixture.BeforeWrite=null;
    Check(File.ReadAllText(path).Contains("External=changed")&&MenuShortcut.Read(IniDocument.Load(path))==MenuShortcut.Home,"concurrent edit is kept and stale write rejected");
    File.WriteAllText(path,"[INPUT]\nKeyOverlay=36,0,0,0\nKeyOverlay=113,0,0,0\n");applied=File.ReadAllBytes(path);
    await Reject<InvalidDataException>(()=>MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default));
    Check(File.ReadAllBytes(path).SequenceEqual(applied),"duplicate binding is preserved on rejection");
    File.WriteAllText(path,"[INPUT]\nKeyOverlay=0,0,0,0\n");
    Check(MenuShortcut.Read(IniDocument.Load(path)).DisplayName=="未设置","disabled existing native key is displayed faithfully");
    Fixture.State="Missing";applied=File.ReadAllBytes(path);
    await MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default);
    Check(File.ReadAllBytes(path).SequenceEqual(applied),"no ReShade does not create or change a config");
    Fixture.State="Conflict";
    await Reject<IOException>(()=>MenuShortcutService.ApplyBeforeLaunchAsync(settings,_=>{},default));
    Check(File.ReadAllBytes(path).SequenceEqual(applied),"ambiguous runtime is not modified");
    Console.WriteLine($"RESULT: {count} menu-shortcut checks passed; isolated files, no game or runtime loaded.");return 0;
}
finally{Directory.Delete(root,true);}
