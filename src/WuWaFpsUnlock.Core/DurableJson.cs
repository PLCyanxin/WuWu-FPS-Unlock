using System.Security.Cryptography;
using System.Text.Json;

namespace WuWaFpsUnlock.Core;

// A single atomic file carries both content and checksum. Legacy metadata can
// still be emitted for older consumers; it is never the new writer's journal.
public static class DurableJson
{
    private sealed record Envelope(byte[] Payload, string Sha256);
    public static void Save<T>(string path, T value)
    {
        byte[] payload=JsonSerializer.SerializeToUtf8Bytes(value);
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new Envelope(payload,Convert.ToHexString(SHA256.HashData(payload))));
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path,false);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static T Read<T>(string path)
    {
        try
        {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>64*1024*1024)throw new InvalidDataException("事务记录过大。");
        var envelope=JsonSerializer.Deserialize<Envelope>(stream)??throw new InvalidDataException("事务记录为空。");
        if(envelope.Payload is null||envelope.Sha256!=Convert.ToHexString(SHA256.HashData(envelope.Payload)))throw new InvalidDataException("事务记录校验失败。");
        return JsonSerializer.Deserialize<T>(envelope.Payload)??throw new InvalidDataException("事务内容为空。");
        }
        catch(JsonException error){throw new InvalidDataException("事务记录格式损坏。",error);}
    }
}
