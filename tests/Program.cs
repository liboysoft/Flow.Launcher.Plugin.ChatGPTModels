using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Flow.Launcher.Plugin.ChatGPT;

internal static class Program
{
    private static int assertions;
    [STAThread]
    private static void Main()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var task = Run();
        task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background), TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.Run();
        try { task.GetAwaiter().GetResult(); Console.WriteLine($"PASS: {assertions} assertions (API, storage, settings UI)."); }
        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new Exception(message); }

    private static async Task Run()
    {
        foreach (var input in new[] { "https://host", "https://host/v1/", "https://host/v1/chat/completions", "https://host/v1/models" })
            Check(EndpointClient.ResolveUrl(input, "models").AbsoluteUri == "https://host/v1/models", "URL resolution: " + input);
        Check(EndpointClient.ResolveUrl("https://host/proxy/v2/chat/completions?api-version=abc", "models").AbsoluteUri == "https://host/proxy/v2/models?api-version=abc", "Custom path/query preserved");
        foreach (var input in new[] { "file:///local", "garbage", "https://secret@host/v1" })
        {
            try { EndpointClient.ResolveUrl(input, "models"); throw new Exception("Accepted unsafe URL"); }
            catch (InvalidOperationException) { assertions++; }
        }
        var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new EndpointClient(http);
        handler.Body = "{\"data\":[{\"id\":\"z-model\"},{\"id\":\"a-model\"},{\"id\":\"z-model\"},{\"id\":\"\"},{\"id\":12},{}]}";
        var models = await client.FetchModelsAsync("https://host/v1/chat/completions", "", "key-for-testing", default);
        Check(models.SequenceEqual(new[] { "a-model", "z-model" }), "Sorted unique model IDs");
        Check(handler.Url == "https://host/v1/models" && handler.Auth == "Bearer key-for-testing", "GET URL and Bearer auth");
        await client.FetchModelsAsync("https://host/v1", "https://host/catalog?x=1", "", default);
        Check(handler.Url == "https://host/catalog?x=1" && handler.Auth is null, "Custom models route / optional key");
        handler.Body = "{\"choices\":[{\"message\":{\"content\":\"hello\"}}]}";
        var answer = await client.CompleteAsync(new Endpoint { Url = "https://host/v1", Model = "custom-model", ApiKey = "test" }, "system", "问题", default);
        Check(answer == "hello" && handler.Method == HttpMethod.Post, "Chat result / POST");
        using (var payload = JsonDocument.Parse(handler.RequestBody!))
        {
            Check(payload.RootElement.GetProperty("model").GetString() == "custom-model", "Selected model sent");
            Check(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "问题", "Prompt sent");
        }
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway })
        {
            handler.Status = status; handler.Body = "private-key-should-not-be-shown";
            await ExpectError(() => client.FetchModelsAsync("https://host/v1", "", "", default), "HTTP");
        }
        // A working catalogue does not prove the gateway exposes inference routes.
        handler.Status = HttpStatusCode.OK;
        handler.Body = "{\"data\":[{\"id\":\"custom-model\"}]}";
        Check((await client.FetchModelsAsync("https://host/v1", "", "", default)).Count == 1, "Catalogue can succeed independently of chat");
        handler.Status = HttpStatusCode.NotFound;
        handler.Body = "404 page not found";
        try
        {
            await client.CompleteAsync(new Endpoint { Url = "https://host/v1", Model = "custom-model" }, "system", "test", default);
            throw new Exception("Expected missing inference route");
        }
        catch (InvalidOperationException e)
        {
            Check(handler.Url == "https://host/v1/chat/completions", "Chat route uses a single version prefix");
            Check(e.Message.Contains("聊天接口") && e.Message.Contains("管理面板") && !e.Message.Contains("自定义模型列表地址"), "Chat 404 identifies inference configuration, not model catalogue");
        }
        await ExpectError(() => client.FetchModelsAsync("https://host/v1", "", "", default), "模型列表接口");
        handler.Status = HttpStatusCode.OK;
        foreach (var body in new[] { "<html>bad gateway</html>", "[]", "null", "{}", "{\"data\":[]}", "{\"data\":[{}]}" })
        {
            handler.Body = body;
            await ExpectError(() => client.FetchModelsAsync("https://host/v1", "", "", default), null);
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await client.FetchModelsAsync("https://host/v1", "", "", cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { assertions++; }
        }
        var directory = Path.Combine(Path.GetTempPath(), "flow-chatgpt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Settings.json");
        var store = new ConfigurationStore(path);
        File.WriteAllText(path, "{\"api_key\":\"old-key\",\"model\":\"old-model\",\"api_endpoint\":\"https://legacy/v1/chat/completions\",\"save_conversation\":\"true\"}");
        var legacy = store.Load();
        Check(legacy.ActiveEndpoint!.Model == "old-model" && legacy.ActiveEndpoint.ApiKey == "old-key" && legacy.SaveConversation, "Python config migration");
        legacy.Endpoints.Add(new Endpoint { Name = "Local", Url = "http://localhost:1234/v1", Model = "local", Models = ["local"] });
        legacy.ActiveEndpointId = legacy.Endpoints[1].Id;
        store.Save(legacy);
        Check(store.Load().ActiveEndpoint!.Model == "local" && store.Load().Endpoints[0].Model == "old-model", "Per endpoint storage round trip");
        File.WriteAllText(path, "corrupt");
        try { store.Load(); throw new Exception("Corrupt settings ignored"); } catch (JsonException) { assertions++; }
        // Exercise the actual WPF controls: fetch, choose, save, switch, failure, invalidate.
        var config = new Configuration();
        config.Endpoints[0].Model = "manual-current";
        Configuration? saved = null;
        handler.Body = "{\"data\":[{\"id\":\"alpha\"},{\"id\":\"beta\"}]}";
        var ui = new SettingsPanel(config, client, value => saved = value) { Width = 700, Height = 1080 };
        ui.Measure(new Size(700, 1080)); ui.Arrange(new Rect(0, 0, 700, 1080)); ui.UpdateLayout();
        var children = Descendants(ui).ToList();
        var combos = children.OfType<ComboBox>().ToList();
        var buttons = children.OfType<Button>().ToDictionary(b => b.Content.ToString()!);
        buttons["拉取模型"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Pump();
        Check(combos[1].Items.Count == 2 && combos[1].Text == "manual-current", "Fetch fills dropdown and preserves model");
        combos[1].SelectedItem = "beta";
        await Pump();
        buttons["保存设置"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(saved?.ActiveEndpoint?.Model == "beta" && saved.ActiveEndpoint.Models.Count == 2, "UI model choice is saved: " + JsonSerializer.Serialize(saved) + string.Join("|", children.OfType<TextBlock>().Select(t => t.Text)));
        handler.Status = HttpStatusCode.Unauthorized;
        buttons["拉取模型"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Pump();
        Check(combos[1].Items.Count == 2 && combos[1].Text == "beta", "Failed refresh preserves model list");
        handler.Status = HttpStatusCode.OK;
        handler.WaitForCancellation = true;
        buttons["拉取模型"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!combos[0].IsEnabled && buttons["取消拉取"].IsEnabled, "In-flight request locks editor and offers cancel");
        buttons["取消拉取"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Pump();
        Check(combos[1].Items.Count == 2 && combos[0].IsEnabled, "Cancelled refresh preserves cache and unlocks editor");
        handler.WaitForCancellation = false;
        combos[1].Text = "hand-typed-model";
        await Pump();
        buttons["保存设置"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(saved!.ActiveEndpoint!.Model == "hand-typed-model", "Editable model ID is saved");
        buttons["添加端点"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(combos[1].Items.Count == 0 && combos[1].Text == "", "New endpoint has isolated list");
        combos[0].SelectedIndex = 0;
        Check(combos[1].Items.Count == 2 && combos[1].Text == "hand-typed-model", "Switch restores endpoint model");
        var fields = children.OfType<TextBox>().ToList();
        fields[1].Text = "http://localhost:1234/v1";
        Check(combos[1].Items.Count == 0, "Endpoint edit invalidates cached list");
        // Visual QA artifact using representative data only.
        var preview = new SettingsPanel(saved!, client, _ => { }) { Width = 700, Height = 1080, Background = Brushes.White, Foreground = Brushes.Black };
        preview.Measure(new Size(700, 1080)); preview.Arrange(new Rect(0, 0, 700, 1080)); preview.UpdateLayout();
        var bitmap = new RenderTargetBitmap(700, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(preview);
        var output = Path.GetFullPath("artifacts/settings-preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(output)) png.Save(file);
        File.Delete(path); Directory.Delete(directory);
    }

    private static async Task Pump() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject obj)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(obj).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static async Task ExpectError(Func<Task<List<string>>> action, string? contains)
    {
        try { await action(); throw new Exception("Expected endpoint failure"); }
        catch (InvalidOperationException e)
        { Check(!e.Message.Contains("private-key") && (contains is null || e.Message.Contains(contains)), "Safe endpoint error"); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string Body = "{}";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool WaitForCancellation;
        public string? Url, Auth, RequestBody;
        public HttpMethod? Method;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, token);
            Url = request.RequestUri!.AbsoluteUri; Auth = request.Headers.Authorization?.ToString(); Method = request.Method;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }
}
