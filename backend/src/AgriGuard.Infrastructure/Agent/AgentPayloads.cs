using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// JSON handling for the agent tables' jsonb columns.
///
/// Event payloads come from the agent service, so they are bounded and scrubbed before they are
/// stored: the timeline keeps plans, tool names, timings and verdicts, never raw prompts, model
/// reasoning or the farmer's note (§6). The agent does not send those today; this is what keeps
/// it that way if a future change starts to.
/// </summary>
internal static class AgentPayloads
{
    /// <summary>Largest payload stored per event. A full verdict with eleven rules is about 4 KB.</summary>
    public const int MaxStoredChars = 32_000;

    private static readonly HashSet<string> RedactedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "prompt", "system", "systemPrompt", "messages", "rawResponse", "reasoning_trace",
        "farmerNote", "farmer_note", "apiKey", "api_key", "authorization"
    };

    public static string? ToStorable(JsonElement? payload)
    {
        if (payload is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element)
            return null;

        var node = JsonNode.Parse(element.GetRawText());
        Redact(node);
        var json = node?.ToJsonString();

        if (json is null || json.Length <= MaxStoredChars)
            return json;

        // Still valid jsonb, and says what happened, rather than a truncated fragment.
        return new JsonObject { ["truncated"] = true, ["originalLength"] = json.Length }.ToJsonString();
    }

    public static string? ToStorable(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, StorageOptions);

    /// <summary>Enums as names, matching the API's responses and what the agent and the console compare against.</summary>
    private static readonly JsonSerializerOptions StorageOptions = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Reads a jsonb column back as JSON for a response. A column holding bad JSON is shown as absent, not as a 500.</summary>
    public static JsonElement? ToElement(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(RedactedKeys.Contains).ToList())
                    obj[key] = "[redacted]";
                foreach (var (_, child) in obj)
                    Redact(child);
                break;

            case JsonArray array:
                foreach (var child in array)
                    Redact(child);
                break;
        }
    }
}
