using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Buddy.Server;

public record ProviderPacket(Uri Endpoint,string Json);

// Side-effect-free text protocol helpers. Explicit reviewed sessions own credentials and network routing.
public static class ProviderProtocols
{
    public const string TextPolicy = ConversationalReply.Policy + " No tools or actions are available.";
    public static readonly string[] Providers=["openai","anthropic","gemini","openrouter"];
    // These helpers never own a connection. Read ProviderRoutingSession.Status for a configured session.
    public static bool Connected=>false;
    public static ProviderPacket Build(string provider,string model,string question)
    {
        if(!Providers.Contains(provider)||!Regex.IsMatch(model,@"^[a-zA-Z0-9][a-zA-Z0-9_./:-]{0,119}$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new InvalidOperationException("Unsupported provider or model identifier.");
        question=Security.Text(question,8000,"Provider question");string policy=TextPolicy;
        object body;string endpoint;
        if(provider=="anthropic"){
            endpoint="https://api.anthropic.com/v1/messages";body=new{model,max_tokens=1024,system=policy,messages=new[]{new{role="user",content=question}},stream=false};
        }else if(provider=="gemini"){
            if(model.Contains('/')||model.Contains(':'))throw new InvalidOperationException("Use a Gemini model name without a path.");
            endpoint="https://generativelanguage.googleapis.com/v1beta/models/"+Uri.EscapeDataString(model)+":generateContent";
            body=new{systemInstruction=new{parts=new[]{new{text=policy}}},contents=new[]{new{role="user",parts=new[]{new{text=question}}}},generationConfig=new{maxOutputTokens=1024}};
        }else{
            endpoint=provider=="openai"?"https://api.openai.com/v1/chat/completions":"https://openrouter.ai/api/v1/chat/completions";
            body=new{model,messages=new[]{new{role="system",content=policy},new{role="user",content=question}},max_completion_tokens=1024,stream=false};
        }
        return new(new Uri(endpoint),JsonSerializer.Serialize(body));
    }
    public static string Parse(string provider,ReadOnlyMemory<byte> json,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();if(!Providers.Contains(provider)||json.Length>65536)throw new InvalidOperationException("Invalid or oversized provider response.");
        try{
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=24});var root=doc.RootElement;string text;
            if(root.TryGetProperty("error",out _))throw new InvalidOperationException("Provider returned an error; no answer or action was accepted.");
            if(provider=="anthropic"){
                if(root.GetProperty("stop_reason").GetString()!="end_turn")throw new InvalidOperationException("The provider answer is incomplete or requests unsupported tools.");
                var blocks=root.GetProperty("content").EnumerateArray().ToArray();if(blocks.Any(b=>b.GetProperty("type").GetString()!="text"))throw new InvalidOperationException("Only text responses are supported.");
                text=string.Join(" ",blocks.Select(b=>b.GetProperty("text").GetString()));
            }else if(provider=="gemini"){
                if(root.TryGetProperty("promptFeedback",out var feedback)&&feedback.TryGetProperty("blockReason",out _))throw new InvalidOperationException("The provider blocked the request.");
                var candidates=root.GetProperty("candidates");if(candidates.GetArrayLength()!=1)throw new InvalidOperationException("Expected one provider answer.");var candidate=candidates[0];
                if(candidate.GetProperty("finishReason").GetString()!="STOP")throw new InvalidOperationException("The provider answer is incomplete or blocked.");
                var parts=candidate.GetProperty("content").GetProperty("parts").EnumerateArray().ToArray();
                if(parts.Any(p=>!p.TryGetProperty("text",out _)||p.TryGetProperty("functionCall",out _)||p.TryGetProperty("inlineData",out _)))throw new InvalidOperationException("Only text responses are supported.");
                text=string.Join(" ",parts.Select(p=>p.GetProperty("text").GetString()));
            }else{
                var choices=root.GetProperty("choices");if(choices.GetArrayLength()!=1)throw new InvalidOperationException("Expected one provider answer.");var choice=choices[0];var message=choice.GetProperty("message");
                if(choice.GetProperty("finish_reason").GetString()!="stop"||message.TryGetProperty("tool_calls",out _)||message.TryGetProperty("function_call",out _))throw new InvalidOperationException("Incomplete answers and tool requests are not accepted.");
                text=message.GetProperty("content").GetString()??"";
            }
            ct.ThrowIfCancellationRequested();return Validate(text);
        }catch(Exception ex)when(ex is JsonException or KeyNotFoundException or InvalidOperationException){throw new InvalidOperationException("No complete supported provider answer was accepted.",ex);}
    }
    internal static string Validate(string text){text=ConversationalReply.PlainText(text);if(text.Length==0||!ConversationalReply.IsConcise(text))throw new InvalidOperationException("The answer needs complete concise recomposition; do not truncate it.");return text;}
}

// Text-only offline Realtime event reducer. It never connects, records or plays audio.
public sealed class RealtimeTextProtocol
{
    private string? response;
    private readonly StringBuilder text=new();
    private readonly HashSet<string> events=[];
    public void Begin(string responseId){response=Security.Text(responseId,100,"Response ID");text.Clear();events.Clear();}
    public string? Receive(ReadOnlyMemory<byte> json,CancellationToken ct=default)
    {
        if(ct.IsCancellationRequested){Cancel();ct.ThrowIfCancellationRequested();}if(response is null)return null;
        if(json.Length>65536){Cancel();throw new InvalidOperationException("Realtime event is too large.");}
        try{
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=24});var root=doc.RootElement;string type=root.GetProperty("type").GetString()??"";
            if(type=="error"){Cancel();throw new InvalidOperationException("Realtime reported an error.");}
            var id=type=="response.done"?root.GetProperty("response").GetProperty("id").GetString():root.TryGetProperty("response_id",out var rid)?rid.GetString():null;
            if(id!=response)return null;
            if(root.TryGetProperty("event_id",out var eid)&&eid.GetString() is{} eventId){if(eventId.Length>100||events.Count>=256)throw new InvalidOperationException("Too many realtime events.");if(!events.Add(eventId))return null;}
            if(type=="response.output_text.delta"){
                var delta=root.GetProperty("delta").GetString()??"";if(text.Length+delta.Length>1600)throw new InvalidOperationException("Realtime answer exceeds the review bound.");text.Append(delta);return null;
            }
            if(type=="response.done"){
                if(root.GetProperty("response").GetProperty("status").GetString()!="completed")throw new InvalidOperationException("Realtime response did not complete.");
                var answer=ProviderProtocols.Validate(text.ToString());Cancel();return answer;
            }
            if(type.Contains("audio",StringComparison.Ordinal)||type.Contains("function_call",StringComparison.Ordinal))throw new InvalidOperationException("Audio and function events are disabled.");
            return null;
        }catch(Exception ex)when(ex is JsonException or KeyNotFoundException or InvalidOperationException){Cancel();throw new InvalidOperationException("Realtime event was not accepted.",ex);}
    }
    public string? Cancel(){var old=response;response=null;text.Clear();events.Clear();return old is null?null:JsonSerializer.Serialize(new{type="response.cancel",response_id=old});}
}
