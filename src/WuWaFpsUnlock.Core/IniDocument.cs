using System.Text;

namespace WuWaFpsUnlock.Core;

// Line-preserving INI editor. Never serializes an existing ReShade configuration from scratch.
public sealed class IniDocument
{
    private readonly List<string> _lines;
    private readonly Encoding _encoding;
    private readonly string _newline;
    private IniDocument(List<string> lines, Encoding encoding, string newline) { _lines = lines; _encoding = encoding; _newline = newline; }
    public static IniDocument Load(string path)
    {
        if (!File.Exists(path)) return new([], new UTF8Encoding(false), "\r\n");
        var bytes = File.ReadAllBytes(path); Encoding enc;
        if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) enc = Encoding.Unicode;
        else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) enc = Encoding.BigEndianUnicode;
        else enc = new UTF8Encoding(bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191, true);
        string text;
        try { text = enc.GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new InvalidDataException("现有 INI 编码未知；为避免损坏，未修改：" + path); }
        return new(text.Replace("\r\n", "\n").Split('\n').ToList(), enc, text.Contains("\r\n") ? "\r\n" : "\n");
    }
    public string? Get(string section, string key)
    {
        var indexes = Find(section, key);
        if (indexes.Count > 1) throw new InvalidDataException($"INI 有重复键 [{section}] {key}，请先处理歧义。");
        return indexes.Count == 0 ? null : _lines[indexes[0]][(_lines[indexes[0]].IndexOf('=') + 1)..].Trim();
    }
    private List<int> Find(string section, string key)
    {
        string active = ""; var result = new List<int>();
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { active = line[1..^1].Trim(); continue; }
            if (line.StartsWith(';') || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && active.Equals(section, StringComparison.OrdinalIgnoreCase) && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) result.Add(i);
        }
        return result;
    }
    public void RemoveKey(string section, string key)
    {
        foreach (int index in Find(section, key).OrderDescending()) _lines.RemoveAt(index);
    }
    public void Set(string section, string key, string? value)
    {
        var found = Find(section, key);
        if (found.Count > 1) throw new InvalidDataException($"INI 重复键：[{section}]{key}");
        if (found.Count == 1)
        {
            if (value is null) _lines.RemoveAt(found[0]);
            else _lines[found[0]] = key + "=" + value;
            return;
        }
        if (value is null) return;
        int sectionIndex = _lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0) { if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add(""); _lines.Add("[" + section + "]"); _lines.Add(key + "=" + value); }
        else _lines.Insert(sectionIndex + 1, key + "=" + value);
    }
    public string MergeCsv(string section, string key, string item)
    {
        var existing = (Get(section, key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!existing.Contains(item, StringComparer.OrdinalIgnoreCase)) existing.Add(item);
        return string.Join(',', existing);
    }
    public void ApplyOwned(string section, string key, string value, DeploymentReceipt receipt)
    {
        string? previous = Get(section, key);
        var edit = receipt.IniEdits.FirstOrDefault(x => x.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (edit is null) receipt.IniEdits.Add(new() { Section = section, Key = key, Previous = previous, Written = value });
        else edit.Written = value;
        Set(section, key, value);
    }
    public void RemoveOwnedEdits(DeploymentReceipt receipt, Action<string> log)
    {
        foreach (var edit in receipt.IniEdits)
        {
            if (edit.Section != "RenoDX.MFGUnlock" && !(edit.Section == "ADDON" && edit.Key == "LoadFromDllMain"))
                throw new InvalidDataException("拒绝清除不属于本工具范围的 INI 项。");
            var current = Get(edit.Section, edit.Key);
            if (current == edit.Written) Set(edit.Section, edit.Key, edit.Previous);
            else if (edit.Section == "ADDON" && edit.Key == "LoadFromDllMain")
            {
                var prior = (edit.Previous ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!prior.Contains("renodx-mfgunlock.addon64", StringComparer.OrdinalIgnoreCase))
                {
                    var values = (current ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(v => !v.Equals("renodx-mfgunlock.addon64", StringComparison.OrdinalIgnoreCase));
                    Set(edit.Section, edit.Key, string.Join(',', values));
                }
            }
            else log($"保留后来被修改的配置：[{edit.Section}] {edit.Key}");
        }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(tmp, string.Join(_newline, _lines), _encoding); File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
