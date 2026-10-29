using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.ChatGPT;

public sealed class Endpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新端点";
    public string Url { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string ModelsUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public List<string> Models { get; set; } = [];
    public DateTimeOffset? ModelsFetchedAt { get; set; }
    public override string ToString() => Name;
}

public sealed class Configuration
{
    public List<Endpoint> Endpoints { get; set; } = [new() { Name = "OpenAI" }];
    public string ActiveEndpointId { get; set; } = "";
    public string PromptStop { get; set; } = "||";
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
    public string DefaultPromptKeyword { get; set; } = "";
    public bool SaveConversation { get; set; }
    [JsonIgnore]
    public Endpoint? ActiveEndpoint => Endpoints.FirstOrDefault(e => e.Id == ActiveEndpointId) ?? Endpoints.FirstOrDefault();
}

public sealed class ConfigurationStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public Configuration Load()
    {
        if (!File.Exists(path)) return new();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("设置文件必须为 JSON 对象。");
        if (document.RootElement.TryGetProperty("Endpoints", out _))
        {
            var config = JsonSerializer.Deserialize<Configuration>(document, Options) ?? new();
            if (config.Endpoints is null) throw new InvalidDataException("端点配置格式无效。");
            return config;
        }
        // Preserve the original Python plugin's single endpoint settings.
        var root = document.RootElement;
        string Read(string key, string fallback = "") => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
        var endpoint = new Endpoint
        {
            Name = "原有端点", Url = Read("api_endpoint", "https://api.openai.com/v1"),
            ApiKey = Read("api_key"), Model = Read("model")
        };
        return new Configuration
        {
            Endpoints = [endpoint], ActiveEndpointId = endpoint.Id,
            PromptStop = Read("prompt_stop", "||"),
            DefaultPromptKeyword = Read("default_prompt", "normal"),
            SaveConversation = root.TryGetProperty("save_conversation", out var save) && (save.ValueKind == JsonValueKind.True || save.ToString().Equals("true", StringComparison.OrdinalIgnoreCase))
        };
    }

    public void Save(Configuration configuration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, Options));
        File.Move(temporary, path, true);
    }
}
