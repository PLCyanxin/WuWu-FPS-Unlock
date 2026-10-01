using System.Text;
namespace WuWaFpsUnlock.Core;

// ReShade 6.8: a double comma is one literal comma; a single comma separates values.
public static class ReShadeValues
{
    public static string[] Decode(string? value)
    {
        if(string.IsNullOrEmpty(value))return [];
        var items=new List<string>();var current=new StringBuilder();
        for(int i=0;i<value.Length;i++)
        {
            if(value[i]!=','){current.Append(value[i]);continue;}
            if(i+1<value.Length&&value[i+1]==','){current.Append(',');i++;}
            else{items.Add(current.ToString());current.Clear();}
        }
        items.Add(current.ToString());return items.ToArray();
    }
    public static string Encode(IEnumerable<string> items)=>string.Join(',',items.Where(v=>v.Length>0).Select(v=>v.Replace(",",",,")));
    public static string? Scalar(string? value)
    {
        var items=Decode(value);if(items.Length>1)throw new InvalidDataException("ReShade 路径配置包含多个值，无法安全确定目标位置。");
        return items.Length==0?null:items[0];
    }
}
