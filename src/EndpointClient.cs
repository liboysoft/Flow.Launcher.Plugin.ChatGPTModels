using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Flow.Launcher.Plugin.ChatGPT;

public sealed class EndpointClient(HttpClient http)
{
    public static Uri ResolveUrl(string url, string resource)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("请输入有效的 HTTP 或 HTTPS 端点地址（不要在地址中填写密钥）。");
        var builder = new UriBuilder(uri) { Fragment = "" };
        var path = builder.Path.TrimEnd('/');
        foreach (var suffix in new[] { "/chat/completions", "/models" })
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length];
                break;
            }
        if (path.Length == 0) path = "/v1";
        builder.Path = path + "/" + resource;
        return builder.Uri;
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri url, string key)
    {
        if (key.Contains('\r') || key.Contains('\n')) throw new InvalidOperationException("API Key 不能包含换行符。");
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public async Task<List<string>> FetchModelsAsync(string endpointUrl, string modelsUrl, string key, CancellationToken token)
    {
        var url = string.IsNullOrWhiteSpace(modelsUrl) ? ResolveUrl(endpointUrl, "models") : ValidateModelsUrl(modelsUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = Request(HttpMethod.Get, url, key);
        using var response = await http.SendAsync(request, timeout.Token);
        using var json = await ReadJsonAsync(response, timeout.Token, isChat: false);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("模型接口响应格式无效：需要 data 数组。");
        var models = data.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("id").GetString()!)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (models.Count == 0) throw new InvalidOperationException("端点未返回可用模型；保留之前的模型列表。");
        return models;
    }

    private static Uri ValidateModelsUrl(string url)
    {
        // Validate without changing a custom models route or its query parameters.
        _ = ResolveUrl(url, "models");
        return new Uri(url.Trim());
    }

    public async Task<string> CompleteAsync(Endpoint endpoint, string system, string prompt, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Model)) throw new InvalidOperationException("请先在设置中选择或输入模型。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        using var request = Request(HttpMethod.Post, ResolveUrl(endpoint.Url, "chat/completions"), endpoint.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = endpoint.Model,
            messages = new[] { new { role = "system", content = system }, new { role = "user", content = prompt } }
        });
        using var response = await http.SendAsync(request, timeout.Token);
        using var json = await ReadJsonAsync(response, timeout.Token, isChat: true);
        var root = json.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
            choices[0].ValueKind == JsonValueKind.Object && choices[0].TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            return content.GetString()!;
        throw new InvalidOperationException("端点未返回有效的 Chat Completions 文本回答。");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken token, bool isChat)
    {
        // Do not show response bodies: gateways may echo credentials or request headers.
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"认证失败（HTTP {(int)response.StatusCode}），请检查 API Key 和端点权限。",
                HttpStatusCode.NotFound when isChat => "聊天接口不存在（HTTP 404）。请确认 API 地址指向模型 API 服务，而非管理面板；模型列表可用不代表聊天接口可用。",
                HttpStatusCode.NotFound => "模型列表接口不存在（HTTP 404），请检查 API 地址或自定义模型列表地址。",
                HttpStatusCode.TooManyRequests => "请求过于频繁（HTTP 429），请稍后重试。",
                _ => $"端点请求失败（HTTP {(int)response.StatusCode}），请检查服务状态。"
            });
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); }
        catch (JsonException) { throw new InvalidOperationException("端点返回了非 JSON 响应，请检查接口地址。"); }
    }
}
