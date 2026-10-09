namespace Buddy.Server;

// Per-request model selection never changes the saved default or downloads a model.
public static class LocalModelSelection
{
    public static void RequireLocalMetadata(System.Text.Json.JsonElement metadata)
    {
        bool Remote(string property) => metadata.TryGetProperty(property, out var value) &&
            value.ValueKind != System.Text.Json.JsonValueKind.Null &&
            (value.ValueKind != System.Text.Json.JsonValueKind.String || !string.IsNullOrEmpty(value.GetString()));
        if (metadata.ValueKind != System.Text.Json.JsonValueKind.Object || Remote("remote_host") || Remote("remote_model") ||
            !metadata.TryGetProperty("details", out var details) || details.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !details.TryGetProperty("format", out var format) || format.ValueKind != System.Text.Json.JsonValueKind.String || format.GetString() != "gguf" ||
            !metadata.TryGetProperty("model_info", out var info) || info.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !info.TryGetProperty("general.architecture", out var architecture) || architecture.ValueKind != System.Text.Json.JsonValueKind.String || string.IsNullOrWhiteSpace(architecture.GetString()))
            throw new BuddyException("LOCAL_MODEL_UNVERIFIED", "Buddy could not verify local model weights. No prompt was sent; cloud-backed or unknown models cannot use this local selection.");
    }
    public static void ValidateRequest(ChatRequest request)
    {
        if (request.LocalModel is null) return;
        if (string.IsNullOrWhiteSpace(request.LocalModel) || request.LocalModel.Length > 200 ||
            request.LocalModel.Any(char.IsControl) || request.LocalModel != request.LocalModel.Trim())
            throw new BuddyException("INVALID_LOCAL_MODEL", "Choose an installed local model.");
        if (request.ImageBase64 is not null || request.BrainId is not (null or "local") ||
            BrainRouter.ExplicitPhrase(request.Text) is not (null or "local"))
            throw new BuddyException("LOCAL_MODEL_SCOPE", "This model choice applies only to local text chat.");
    }
    public static string RequireInstalled(string selected, IReadOnlyCollection<string> installed)
    {
        if (!installed.Contains(selected, StringComparer.Ordinal))
            throw new BuddyException("LOCAL_MODEL_UNAVAILABLE", "The selected model is no longer installed. Choose another local model; nothing was downloaded.");
        return selected;
    }
}
