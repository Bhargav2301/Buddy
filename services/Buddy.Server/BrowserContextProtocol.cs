using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Buddy.Server;

public sealed class BrowserContextProtocolException : InvalidOperationException
{
    public string Code {get;}
    internal BrowserContextProtocolException(string code):base("The browser context request was refused: "+code){Code=code;}
}

public static class BrowserContextProtocol
{
    private static readonly UTF8Encoding Utf8=new(false,true);
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=24};
    public static BrowserContextRegistration ReadRegistration(ReadOnlyMemory<byte> frame)=>Read<BrowserContextRegistration>(frame);
    public static BrowserContextEnvelope ReadEnvelope(ReadOnlyMemory<byte> frame)=>Read<BrowserContextEnvelope>(frame);
    public static byte[] Serialize<T>(T value)
    {
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(value,Json);
        if(data.Length>BrowserContextLimits.MaximumFrameBytes)throw Error("frame_too_large");
        return data;
    }
    public static string Sha256(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string TextSha256(string text)=>Sha256(TextBytes(text));
    internal static byte[] TextBytes(string text)
    {try{return Utf8.GetBytes(text);}catch(EncoderFallbackException){throw Error("invalid_unicode");}}
    internal static T Payload<T>(JsonElement payload)
    {
        try{if(payload.ValueKind!=JsonValueKind.Object)throw Error("invalid_payload");ValidateJson(payload);RequireShape(typeof(T),payload);return payload.Deserialize<T>(Json)??throw Error("invalid_payload");}
        catch(JsonException){throw Error("invalid_payload");}
        catch(InvalidOperationException e)when(e is not BrowserContextProtocolException){throw Error("invalid_payload");}
    }
    internal static T Read<T>(ReadOnlyMemory<byte> bytes,int maximum=BrowserContextLimits.MaximumFrameBytes)
    {
        if(bytes.Length==0||bytes.Length>maximum)throw Error("invalid_frame_size");
        try{
            _=Utf8.GetString(bytes.Span);
            using var document=JsonDocument.Parse(bytes,new(){MaxDepth=24});ValidateJson(document.RootElement);RequireShape(typeof(T),document.RootElement);
            return document.RootElement.Deserialize<T>(Json)??throw Error("invalid_payload");
        }catch(JsonException){throw Error("invalid_json");}
        catch(DecoderFallbackException){throw Error("invalid_unicode");}
        catch(InvalidOperationException e)when(e is not BrowserContextProtocolException){throw Error("invalid_json");}
    }
    private static void RequireShape(Type type,JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Null)return;
        if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(IReadOnlyList<>)){
            if(value.ValueKind!=JsonValueKind.Array)throw Error("invalid_payload");
            foreach(var child in value.EnumerateArray())RequireShape(type.GenericTypeArguments[0],child);return;
        }
        if(type.Namespace!=typeof(BrowserContextProtocol).Namespace||!type.Name.StartsWith("BrowserContext",StringComparison.Ordinal))return;
        if(value.ValueKind!=JsonValueKind.Object)throw Error("invalid_payload");
        foreach(var property in type.GetProperties(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public)){
            if(!value.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(property.Name),out var child))throw Error("missing_property");
            RequireShape(property.PropertyType,child);
        }
    }
    private static void ValidateJson(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object){
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var member in value.EnumerateObject()){
                TextBytes(member.Name);if(!names.Add(member.Name))throw Error("duplicate_property");ValidateJson(member.Value);
            }
        }else if(value.ValueKind==JsonValueKind.Array){foreach(var item in value.EnumerateArray())ValidateJson(item);}
        else if(value.ValueKind==JsonValueKind.String)TextBytes(value.GetString()!);
    }
    internal static void Id(string? value,int maximum=160)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>maximum||value.Any(char.IsControl))throw Error("invalid_identifier");
        TextBytes(value);
    }
    internal static void Digest(string? value)
    {if(value is null||value.Length!=64||value.Any(c=>c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))throw Error("invalid_digest");}
    internal static bool EqualSecret(string expected,string? supplied)
    {if(supplied is null||expected.Length!=supplied.Length)return false;return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),Encoding.UTF8.GetBytes(supplied));}
    internal static BrowserContextProtocolException Error(string code)=>new(code);
}
