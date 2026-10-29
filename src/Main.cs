using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic.FileIO;

namespace Flow.Launcher.Plugin.ChatGPT;

public sealed class Main : IAsyncPlugin, ISettingProvider, ISavable
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly EndpointClient client = new(Http);
    private PluginInitContext context = null!;
    private ConfigurationStore store = null!;
    private Configuration configuration = new();
    private string? loadError;
    private readonly Dictionary<string, string> prompts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["short"] = "Keep answers brief and to the point.",
        ["long"] = "Give detailed explanations with examples."
    };

    public Task InitAsync(PluginInitContext context)
    {
        this.context = context;
        store = new ConfigurationStore(Path.Combine(context.CurrentPluginMetadata.PluginSettingsDirectoryPath, "Settings.json"));
        try
        {
            configuration = store.Load();
            var csv = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "system_messages.csv");
            if (File.Exists(csv))
            {
                using var parser = new TextFieldParser(csv) { HasFieldsEnclosedInQuotes = true };
                parser.SetDelimiters(";");
                _ = parser.ReadFields();
                while (!parser.EndOfData)
                {
                    var row = parser.ReadFields();
                    if (row is { Length: >= 2 } && !string.IsNullOrWhiteSpace(row[0])) prompts[row[0]] = row[1];
                }
            }
            if (prompts.TryGetValue(configuration.DefaultPromptKeyword, out var legacyDefault))
                configuration.SystemPrompt = legacyDefault;
        }
        catch (MalformedLineException) { loadError = "system_messages.csv 格式无效，请检查分号分隔和引号配对后重新加载插件。"; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { loadError = "无法读取设置文件，请检查或备份修复 Settings.json 后重新加载插件。"; }
        return Task.CompletedTask;
    }

    public Control CreateSettingPanel()
    {
        if (loadError is not null)
            return new UserControl { Content = new TextBlock { Text = loadError, Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
        return new SettingsPanel(configuration, client, value => { store.Save(value); configuration = value; });
    }

    // Settings are persisted by the explicit Save button; never overwrite a damaged file on shutdown.
    public void Save() { }

    public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        if (loadError is not null) return [Item("设置读取失败", loadError)];
        var config = configuration;
        var endpoint = config.ActiveEndpoint;
        if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.Model))
            return [Item("请先配置端点和模型", "打开 Flow Launcher 设置 → 插件 → ChatGPT Models")];
        var search = query.Search.Trim();
        if (string.IsNullOrEmpty(config.PromptStop) || !search.EndsWith(config.PromptStop, StringComparison.Ordinal))
            return [Item($"输入问题并以 {config.PromptStop} 结束", $"{endpoint.Name} · {endpoint.Model}")];
        var prompt = search[..^config.PromptStop.Length].Trim();
        if (prompt.Length == 0) return [Item("请输入问题", $"{endpoint.Name} · {endpoint.Model}")];
        var system = config.SystemPrompt;
        var keywordEnd = prompt.IndexOf(' ');
        if (keywordEnd > 0)
        {
            var keyword = prompt[..keywordEnd];
            if (keyword.Equals("normal", StringComparison.OrdinalIgnoreCase)) prompt = prompt[(keywordEnd + 1)..];
            else if (prompts.TryGetValue(keyword, out var instruction))
            { prompt = prompt[(keywordEnd + 1)..]; system = instruction; }
        }
        try
        {
            var answer = await client.CompleteAsync(endpoint, system, prompt, token);
            token.ThrowIfCancellationRequested();
            var saved = "";
            if (config.SaveConversation)
            {
                saved = Path.Combine(context.CurrentPluginMetadata.PluginSettingsDirectoryPath, "Conversations.txt");
                try { await File.AppendAllTextAsync(saved, $"[{DateTimeOffset.Now:u}] {endpoint.Name} / {endpoint.Model}\nUser: {prompt}\nAssistant: {answer}\n\n", token); }
                catch (IOException) { saved = "对话保存失败；回答仍可复制。"; }
                catch (UnauthorizedAccessException) { saved = "对话保存失败；回答仍可复制。"; }
            }
            return
            [
                new Result { Title = "复制回答", SubTitle = answer.ReplaceLineEndings(" "), IcoPath = "Images/app.png", CopyText = answer,
                    Action = _ => { Clipboard.SetText(answer); return true; } },
                new Result { Title = "在文本编辑器打开", SubTitle = string.IsNullOrEmpty(saved) ? $"{endpoint.Name} · {endpoint.Model}" : saved, IcoPath = "Images/app.png",
                    Action = _ => { var file = Path.Combine(Path.GetTempPath(), $"Flow-ChatGPT-{Guid.NewGuid():N}.txt"); File.WriteAllText(file, answer); Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); return true; } }
            ];
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return []; }
        catch (OperationCanceledException) { return [Item("请求超时", "端点在 120 秒内未完成回答，请稍后重试。")]; }
        catch (HttpRequestException) { return [Item("无法连接端点", "请检查网络、代理或服务状态。")]; }
        catch (InvalidOperationException e) { return [Item("请求失败", e.Message)]; }
    }

    private static Result Item(string title, string subtitle) => new() { Title = title, SubTitle = subtitle, IcoPath = "Images/app.png" };
}
