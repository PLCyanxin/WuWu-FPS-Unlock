using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace WuWaFpsUnlock.Core;

public static class EnvironmentStatus
{
    public static string WindowsName(int major, int minor, int build, int revision, string? product, string? installationType, string? displayVersion)
    {
        bool client = string.Equals(installationType, "Client", StringComparison.OrdinalIgnoreCase);
        string name = string.IsNullOrWhiteSpace(product) ? "Windows" : product.Replace("Microsoft ", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (client && major == 10 && build >= 22000)
            name = name.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) ? name.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase) : name.Contains("Windows 11", StringComparison.OrdinalIgnoreCase) ? name : "Windows 11";
        if (string.IsNullOrWhiteSpace(installationType)) name = "Windows"; // Do not classify a server as Windows 11 from build alone.
        string release = string.IsNullOrWhiteSpace(displayVersion) ? "" : " " + displayVersion.Trim();
        return $"{name}{release}（版本 {build}.{revision}）";
    }
    public static bool? ParseDxDiagHags(string xml, string selectedGpu)
    {
        if (string.IsNullOrWhiteSpace(selectedGpu)) return null;
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        static string Normalize(string text) => Regex.Replace(text.Replace("NVIDIA ", "", StringComparison.OrdinalIgnoreCase).Trim(), @"\s+", " ");
        var selected = Normalize(selectedGpu);
        if (document.Root?.Name != "DxDiag") return null;
        var matches = (document.Root.Element("DisplayDevices")?.Elements("DisplayDevice") ?? Enumerable.Empty<XElement>())
            .Where(d => string.Equals(Normalize((string?)d.Element("CardName") ?? ""), selected, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) return null;
        bool? result = null;
        foreach (var device in matches)
        {
            string value = (string?)device.Element("HardwareSchedulingAttributes") ?? (string?)device.Element("HardwareScheduling") ?? "";
            var tokens = Regex.Matches(value, @"(?:^|\s)Enabled\s*:\s*(True|False)(?=\s|$)", RegexOptions.IgnoreCase);
            if (tokens.Count != 1) return null;
            bool enabled = tokens[0].Groups[1].Value.Equals("True", StringComparison.OrdinalIgnoreCase);
            if (result.HasValue && result.Value != enabled) return null;
            result = enabled;
        }
        return result;
    }
    public static string HagsText(bool? runtime, bool? configured) => runtime switch
    {
        true => "已开启（DxDiag 运行态）" + (configured == false ? "；配置为关闭，可能待重启" : ""),
        false => "未开启（DxDiag 运行态）" + (configured == true ? "；配置为开启，可能待重启" : ""),
        _ => configured switch { true => "已配置开启；运行态未确认", false => "已配置关闭；运行态未确认", _ => "系统默认配置；运行态未确认" }
    };
}
