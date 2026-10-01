using System.Globalization;

namespace WuWaFpsUnlock.Core;

// ReShade INPUT/KeyOverlay is a Windows virtual key followed by Ctrl, Shift, Alt.
public sealed record MenuShortcut(int Key, bool Control = false, bool Shift = false, bool Alt = false)
{
    public static MenuShortcut Home { get; } = new(0x24);
    public bool IsValid => IsKeyboardKey(Key) && !(Alt && Key is 9 or 27 or 32 or 0x73)
        && !(Control && Key == 27) && !(Control && Alt && Key == 46);
    private static bool IsKeyboardKey(int key) => key is 8 or 9 or 12 or 13 or 19 or 20 or 27
        or >= 32 and <= 40 or 45 or 46 or >= 48 and <= 57 or >= 65 and <= 90
        or >= 96 and <= 135 or 144 or 145 or >= 186 and <= 192 or >= 219 and <= 222 or 226;

    public string ToIniValue()
    {
        if (!IsValid) throw new InvalidDataException("请选择有效的键盘按键；不能使用鼠标、单独的修饰键或系统保留的快捷键。");
        return string.Join(',', Key.ToString(CultureInfo.InvariantCulture), Control ? "1" : "0", Shift ? "1" : "0", Alt ? "1" : "0");
    }
    public string DisplayName
    {
        get
        {
            if(Key==0)return "未设置";
            string name=Key switch
            {
                >=65 and <=90 => ((char)Key).ToString(), >=48 and <=57 => ((char)Key).ToString(),
                >=112 and <=135 => "F"+(Key-111), >=96 and <=105 => "Num "+(Key-96),
                8=>"Backspace",9=>"Tab",12=>"Clear",13=>"Enter",19=>"Pause",20=>"Caps Lock",27=>"Esc",
                32=>"Space",33=>"Page Up",34=>"Page Down",35=>"End",36=>"Home",37=>"Left",38=>"Up",39=>"Right",40=>"Down",
                45=>"Insert",46=>"Delete",106=>"Num *",107=>"Num +",108=>"Num Enter",109=>"Num -",110=>"Num .",111=>"Num /",
                144=>"Num Lock",145=>"Scroll Lock",186=>";",187=>"=",188=>",",189=>"-",190=>".",191=>"/",192=>"`",219=>"[",220=>"\\",221=>"]",222=>"'",226=>"OEM 102",
                _=>"未识别"
            };
            return (Control?"Ctrl + ":"")+(Shift?"Shift + ":"")+(Alt?"Alt + ":"")+name;
        }
    }
    public static MenuShortcut Read(IniDocument ini)
    {
        string? raw = ini.Get("INPUT", "KeyOverlay");
        if (raw is null) return Home;
        var fields = ReShadeValues.Decode(raw);
        if (fields.Length != 4 || !int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int key)
            || fields.Skip(1).Any(value => value.Trim() is not ("0" or "1")))
            throw new InvalidDataException("ReShade 菜单按键配置无法识别，请在设置中重新选择按键。");
        var shortcut = new MenuShortcut(key, fields[1].Trim() == "1", fields[2].Trim() == "1", fields[3].Trim() == "1");
        // An existing disabled binding is displayed faithfully, never replaced by Home.
        if (key != 0 && !shortcut.IsValid) throw new InvalidDataException("ReShade 菜单按键配置无法识别，请在设置中重新选择按键。");
        return shortcut;
    }
    public void Apply(IniDocument ini) => ini.Set("INPUT", "KeyOverlay", ToIniValue());
}
