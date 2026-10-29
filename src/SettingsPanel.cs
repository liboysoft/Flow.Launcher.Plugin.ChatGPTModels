using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Flow.Launcher.Plugin.ChatGPT;

public sealed class SettingsPanel : UserControl
{
    private readonly Configuration draft;
    private readonly EndpointClient client;
    private readonly Action<Configuration> apply;
    private readonly ComboBox endpoints = new() { MinWidth = 260 };
    private readonly TextBox name = new();
    private readonly TextBox url = new();
    private readonly TextBox modelsUrl = new();
    private readonly PasswordBox key = new();
    private readonly ComboBox model = new() { IsEditable = true, IsTextSearchEnabled = true, MaxDropDownHeight = 260 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
    private readonly TextBlock cacheStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox stop = new();
    private readonly TextBox system = new() { AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox history = new() { Content = "保存对话到本地文本文件" };
    private readonly CheckBox active = new() { Content = "将此端点用于聊天" };
    private readonly Button fetch = new() { Content = "拉取模型", MinWidth = 96 };
    private readonly Button cancel = new() { Content = "取消拉取", IsEnabled = false };
    private readonly List<Control> editing = [];
    private CancellationTokenSource? fetching;
    private bool loading;
    private Endpoint? Selected => endpoints.SelectedItem as Endpoint;

    public SettingsPanel(Configuration configuration, EndpointClient endpointClient, Action<Configuration> save)
    {
        draft = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(configuration))!;
        client = endpointClient;
        apply = save;
        var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 740, HorizontalAlignment = HorizontalAlignment.Stretch };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "端点与模型", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "配置兼容 OpenAI Chat Completions 的服务，主动获取模型后选择使用。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        Row(panel, "端点配置", endpoints);
        var add = new Button { Content = "添加端点" };
        var remove = new Button { Content = "删除端点" };
        Buttons(panel, add, remove);
        Row(panel, "名称", name);
        Row(panel, "API 地址", url, "支持基础地址（如 https://host/v1）或完整的 /chat/completions 地址。");
        Row(panel, "API Key", key, "本地服务可留空；密钥随配置保存在本机设置文件中。");
        Row(panel, "模型列表地址（可选）", modelsUrl, "留空时根据 API 地址生成 /models；填写时使用完整 URL，并向该地址发送此端点的 API Key。");
        Buttons(panel, fetch, cancel);
        panel.Children.Add(cacheStatus);
        Row(panel, "聊天模型", model, "从拉取的列表中选择，也可直接输入模型 ID。模型能否聊天取决于端点支持。");
        panel.Children.Add(active);
        Row(panel, "提问结束标记", stop);
        Row(panel, "默认系统提示词", system);
        panel.Children.Add(history);
        var saveButton = new Button { Content = "保存设置", MinWidth = 100 };
        Buttons(panel, saveButton);
        panel.Children.Add(status);
        editing.AddRange([endpoints, add, remove, name, url, key, modelsUrl, model, active, stop, system, history, saveButton]);

        endpoints.SelectionChanged += (_, _) => LoadEndpoint();
        name.TextChanged += (_, _) => { if (!loading && Selected is { } e) { e.Name = name.Text; } };
        url.TextChanged += (_, _) => { if (!loading && Selected is { } e) { InvalidateModels(e); e.Url = url.Text; } };
        modelsUrl.TextChanged += (_, _) => { if (!loading && Selected is { } e) { InvalidateModels(e); e.ModelsUrl = modelsUrl.Text; } };
        key.PasswordChanged += (_, _) => { if (!loading && Selected is { } e) { InvalidateModels(e); e.ApiKey = key.Password; } };
        model.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (!loading && Selected is { } e) e.Model = model.Text; }));
        model.SelectionChanged += (_, _) => { if (!loading && Selected is { } e && model.SelectedItem is string id) e.Model = id; };
        active.Checked += (_, _) => { if (!loading && Selected is { } e) draft.ActiveEndpointId = e.Id; };
        active.Unchecked += (_, _) => { if (!loading) { loading = true; active.IsChecked = Selected?.Id == draft.ActiveEndpointId; loading = false; } };
        add.Click += (_, _) => { var e = new Endpoint(); draft.Endpoints.Add(e); RefreshEndpoints(e); };
        remove.Click += (_, _) =>
        {
            if (Selected is not { } e) return;
            if (draft.Endpoints.Count == 1) { status.Text = "至少保留一个端点。"; return; }
            if (MessageBox.Show("删除此端点及其模型配置？", "ChatGPT", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            draft.Endpoints.Remove(e);
            if (draft.ActiveEndpointId == e.Id) draft.ActiveEndpointId = draft.Endpoints[0].Id;
            RefreshEndpoints(draft.ActiveEndpoint);
        };
        fetch.Click += async (_, _) => await FetchAsync();
        cancel.Click += (_, _) => fetching?.Cancel();
        Unloaded += (_, _) => fetching?.Cancel();
        saveButton.Click += (_, _) => SaveDraft();
        stop.Text = draft.PromptStop;
        system.Text = draft.SystemPrompt;
        history.IsChecked = draft.SaveConversation;
        draft.ActiveEndpointId = draft.ActiveEndpoint?.Id ?? "";
        RefreshEndpoints(draft.ActiveEndpoint);
    }

    private static void Row(Panel panel, string label, Control control, string? description = null)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.MinHeight = Math.Max(control.MinHeight, 30);
        panel.Children.Add(control);
        if (description is not null) panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Opacity = 0.75 });
    }

    private static void Buttons(Panel panel, params Button[] buttons)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        foreach (var button in buttons) { button.Padding = new Thickness(12, 5, 12, 5); button.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(button); }
        panel.Children.Add(row);
    }

    private void RefreshEndpoints(Endpoint? selected)
    {
        endpoints.ItemsSource = null;
        endpoints.ItemsSource = draft.Endpoints;
        endpoints.SelectedItem = selected;
    }

    private void LoadEndpoint()
    {
        if (Selected is not { } e) return;
        loading = true;
        name.Text = e.Name; url.Text = e.Url; key.Password = e.ApiKey; modelsUrl.Text = e.ModelsUrl;
        model.ItemsSource = e.Models.ToList(); model.Text = e.Model;
        active.IsChecked = draft.ActiveEndpointId == e.Id;
        UpdateCacheStatus(e);
        loading = false;
        status.Text = "";
    }

    private void InvalidateModels(Endpoint endpoint)
    {
        var selectedModel = endpoint.Model;
        loading = true;
        endpoint.Models = []; endpoint.ModelsFetchedAt = null;
        model.ItemsSource = null;
        model.Text = selectedModel;
        loading = false;
        UpdateCacheStatus(endpoint);
    }

    private void UpdateCacheStatus(Endpoint e) => cacheStatus.Text = e.ModelsFetchedAt is { } date
        ? $"已缓存 {e.Models.Count} 个模型 · {date.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "尚未拉取此端点的模型。";

    private async Task FetchAsync()
    {
        if (Selected is not { } endpoint || fetching is not null) return;
        using var cancellation = new CancellationTokenSource();
        fetching = cancellation;
        foreach (var control in editing) control.IsEnabled = false;
        fetch.IsEnabled = false; cancel.IsEnabled = true;
        status.Text = "正在拉取模型…";
        try
        {
            var models = await client.FetchModelsAsync(endpoint.Url, endpoint.ModelsUrl, endpoint.ApiKey, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var selectedModel = endpoint.Model;
            loading = true;
            endpoint.Models = models; endpoint.ModelsFetchedAt = DateTimeOffset.Now;
            model.ItemsSource = models;
            model.Text = selectedModel;
            loading = false;
            UpdateCacheStatus(endpoint);
            status.Text = $"成功拉取 {models.Count} 个模型。请选择模型，然后保存设置。";
        }
        catch (OperationCanceledException) { status.Text = cancellation.IsCancellationRequested ? "已取消拉取；保留原列表。" : "拉取超时；请检查端点后重试。"; }
        catch (HttpRequestException) { status.Text = "无法连接端点，请检查网络、代理或服务状态。"; }
        catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException) { status.Text = e.Message; }
        finally
        {
            loading = false;
            fetching = null;
            foreach (var control in editing) control.IsEnabled = true;
            fetch.IsEnabled = true; cancel.IsEnabled = false;
        }
    }

    private void SaveDraft()
    {
        try
        {
            if (Selected is { } selected) selected.Model = model.Text.Trim();
            foreach (var e in draft.Endpoints)
            {
                if (string.IsNullOrWhiteSpace(e.Name)) throw new InvalidOperationException("请为每个端点填写名称。");
                _ = EndpointClient.ResolveUrl(e.Url, "models");
            }
            if (string.IsNullOrWhiteSpace(stop.Text)) throw new InvalidOperationException("提问结束标记不能为空。");
            if (string.IsNullOrWhiteSpace(draft.ActiveEndpoint?.Model)) throw new InvalidOperationException("请为当前聊天端点选择或输入模型。");
            draft.PromptStop = stop.Text;
            draft.SystemPrompt = system.Text;
            draft.DefaultPromptKeyword = "";
            draft.SaveConversation = history.IsChecked == true;
            apply(JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(draft))!);
            endpoints.Items.Refresh();
            status.Text = "设置已保存，下次提问将使用所选端点和模型。";
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        { status.Text = e is InvalidOperationException ? e.Message : "保存失败，请检查设置目录的写入权限。"; }
    }
}
