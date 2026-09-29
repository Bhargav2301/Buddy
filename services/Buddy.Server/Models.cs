namespace Buddy.Server;

public record ChatMessage(string Id, string Role, string Text, DateTimeOffset At, string Mode = "type", string? RequestId = null);
public record Conversation(string Id, string Title, DateTimeOffset UpdatedAt, List<ChatMessage> Messages, bool Pinned = false, bool Archived = false);
public record Note(string Id, string Title, string Text);
public record PairedDevice(string Id, string Name, string TokenHash, DateTimeOffset AddedAt);
public record ChatRequest(string ConversationId, string Text, string RequestId, string Mode = "type", string? Context = null, string? ImageBase64 = null, bool UseWeb = false);
public record PairRequest(string Code, string Name);
public record RefineRequest(string Prompt, string Mode = "quick", string Technique = "auto", string Domain = "general", bool Important = false);
public record ConversationUpdate(string? Title = null, bool? Pinned = null, bool? Archived = null);
public record NoteRequest(string Title, string Text);
public record StreamEvent(string Type, string? Text = null, string? Code = null, string? ConversationId = null);
public record EngineStatus(bool Reachable, bool Ready, string Model, string VisionModel, string[] Installed, string Message);
public sealed class BuddyState
{
    public int SchemaVersion { get; set; } = 2;
    public string EmbeddingModel { get; set; } = "all-minilm:22m";
    public string Model { get; set; } = "qwen3:4b-instruct-2507-q4_K_M";
    public string VisionModel { get; set; } = "gemma3:4b";
    public string DesktopToken { get; set; } = Security.NewToken();
    public List<Conversation> Conversations { get; set; } = [];
    public List<PairedDevice> Devices { get; set; } = [];
    public List<Note> Memories { get; set; } = [];
    public List<Note> Prompts { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
    public List<SavedGuide> Guides { get; set; } = [];
}
public class BuddyException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
