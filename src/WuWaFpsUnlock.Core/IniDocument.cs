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
        if (bytes.Length >= 2 && ((bytes[0] == 255 && bytes[1] == 254)||(bytes[0] == 254 && bytes[1] == 255)))
            throw new InvalidDataException("ReShade 配置不是 UTF-8；请先备份并转换编码。为避免损坏，未修改："+path);
        enc = new UTF8Encoding(bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191, true);
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
            if (SectionName(line) is string name) { active = name; continue; }
            if (line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('/')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && active.Equals(section, StringComparison.Ordinal) && line[..eq].Trim().Equals(key, StringComparison.Ordinal)) result.Add(i);
        }
        return result;
    }
    private static string? SectionName(string line)
    {
        line=line.Trim();if(!line.StartsWith('['))return null;
        int end=line.IndexOf(']');return (end<0?line:line[..end]).Trim(' ','\t','[',']');
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
            else if(Get(section,key)!=value) _lines[found[0]] = key + "=" + value;
            return;
        }
        if (value is null) return;
        int sectionIndex = _lines.FindIndex(l => SectionName(l)==section);
        if (sectionIndex < 0) { if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add(""); _lines.Add("[" + section + "]"); _lines.Add(key + "=" + value); }
        else _lines.Insert(sectionIndex + 1, key + "=" + value);
    }
    public string MergeCsv(string section, string key, string item)
    {
        var existing = Csv(Get(section, key)).ToList();
        if (!existing.Contains(item, StringComparer.OrdinalIgnoreCase)) existing.Add(item);
        return ReShadeValues.Encode(existing);
    }
    public void ApplyOwned(string section, string key, string value, DeploymentReceipt receipt)
    {
        string? previous = Get(section, key);
        var edit = receipt.IniEdits.FirstOrDefault(x => x.Section.Equals(section, StringComparison.Ordinal) && x.Key.Equals(key, StringComparison.Ordinal));
        if (edit is null) receipt.IniEdits.Add(new() { Section = section, Key = key, Previous = previous, Written = value });
        else edit.Written = value;
        Set(section, key, value);
    }
    private static string[] Csv(string? value) => ReShadeValues.Decode(value).Where(v=>v.Length>0).ToArray();
    public void ApplyManagedAddonLoading(DeploymentReceipt receipt)
    {
        const string section = "ADDON", key = "LoadFromDllMain";
        var current = Csv(Get(section, key)).ToList();
        var edit = receipt.IniEdits.FirstOrDefault(e => e.Section.Equals(section, StringComparison.Ordinal) && e.Key.Equals(key, StringComparison.Ordinal));
        if (edit is not null)
        {
            // A newly observed entry on redeployment belongs to the user, not this tool.
            var prior = Csv(edit.Previous).ToList();
            foreach (string name in current.Where(ManagedAddons.Contains))
                if (!Csv(edit.Written).Contains(name, StringComparer.OrdinalIgnoreCase) && !prior.Contains(name, StringComparer.OrdinalIgnoreCase)) prior.Add(name);
            if (prior.Count > 0) edit.Previous = ReShadeValues.Encode(prior);
        }
        foreach (string name in new[] { ManagedAddons.Main })
            if (!current.Contains(name, StringComparer.OrdinalIgnoreCase)) current.Add(name);
        ApplyOwned(section, key, ReShadeValues.Encode(current), receipt);
    }
    public List<IniReceipt> RemoveOwnedEdits(DeploymentReceipt receipt, Action<string> log)
    {
        var preserved = new List<IniReceipt>();
        foreach (var edit in receipt.IniEdits)
        {
            if (edit.Section != "RenoDX.MFGUnlock" && !(edit.Section == "ADDON" && edit.Key == "LoadFromDllMain"))
                throw new InvalidDataException("拒绝清除不属于本工具范围的 INI 项。");
            var current = Get(edit.Section, edit.Key);
            if (edit.Section == "ADDON" && edit.Key == "LoadFromDllMain")
            {
                var prior = Csv(edit.Previous);
                var added = Csv(edit.Written).Where(name => ManagedAddons.Contains(name) && !prior.Contains(name, StringComparer.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var values = Csv(current).Where(name => !added.Contains(name)).ToArray();
                Set(edit.Section, edit.Key, values.Length == 0 && edit.Previous is null ? null : ReShadeValues.Encode(values));
            }
            else if (current == edit.Written) Set(edit.Section, edit.Key, edit.Previous);
            else if (current is not null)
            { preserved.Add(edit); log($"保留后来被修改的配置：[{edit.Section}] {edit.Key}"); }
        }
        return preserved;
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(tmp, string.Join(_newline, _lines), _encoding); File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
