# ChatGPT Models

[English](README.md) | [简体中文](README.zh-CN.md)

A Flow Launcher plugin for chatting with OpenAI-compatible services, with model discovery and selection in a native settings panel.

**Author:** liboy · **Version:** 1.0.0 · **License:** MIT

## Features

- Add multiple endpoints with separate addresses, API keys, model lists, and selected models.
- Fetch models on demand, then select a model from the dropdown or enter a model ID manually.
- Keep cached lists and selections when refreshing fails or is cancelled.
- Configure a custom models URL, system prompt, and prompt terminator.
- Copy answers, open them in a text editor, or optionally save conversations locally.

The settings UI is currently in Simplified Chinese. Chat prompts and answers can use any language supported by your model.

![Settings with sample models](docs/settings.png)

## Requirements

Windows and Flow Launcher **2.1.3 or later**. This is a C# / WPF plugin targeting .NET 9. Python is not required to run the release package.

The service must support OpenAI-style **Chat Completions** (`POST /chat/completions`). Model discovery uses `GET /models` and a `data[].id` response. Responses-only and native Anthropic endpoints are not supported.

## Installation

After a release is published, download the ZIP from [GitHub Releases](https://github.com/liboysoft/Flow.Launcher.Plugin.ChatGPTModels/releases), or enter this in Flow Launcher:

```text
pm install https://github.com/liboysoft/Flow.Launcher.Plugin.ChatGPTModels/releases/download/v1.0.0/Flow.Launcher.Plugin.ChatGPTModels.zip
```

The plugin has not been submitted to the Flow plugin store by this local preparation step. Once accepted, it can also be installed from the store.

## Configure and use

1. Open Flow Launcher settings → Plugins → **ChatGPT Models**.
2. Select or add an endpoint. Enter its name, API address, and API key. Local services may allow an empty key.
3. Click **拉取模型** (Fetch models). Select a model under **聊天模型**, or type its ID.
4. Enable **将此端点用于聊天** (Use this endpoint for chat), then click **保存设置** (Save settings).
5. Ask a question and finish it with the terminator, `||` by default:

```text
ai What is the symbol for the British pound? ||
ai short Explain DNS ||
ai long Explain DNS ||
```

API addresses may be a base URL such as `https://host/v1`, a full `https://host/v1/chat/completions` URL, or a local HTTP address. A bare hostname defaults to `/v1`; custom path prefixes are preserved. Leave the models URL empty to derive `/models`, or specify a complete custom catalog URL. The configured endpoint key is sent to that custom URL.

Fetching does not select a model automatically. Click Save settings after selecting the endpoint and model. Editing an address or key clears the old catalog while keeping the selected model ID. The model list may include non-chat models; choose one that supports Chat Completions.

For custom prompt keywords, place a UTF-8 `system_messages.csv` in the installed plugin directory, then reload the plugin. It uses a header and semicolon-separated keyword / system-prompt columns, with normal CSV quoting.

## Storage and migration

API keys are stored in the local plugin settings JSON. Do not publish settings or real keys. Conversation saving is opt-in and writes `Conversations.txt` in the plugin settings directory.

This independent release uses a new plugin ID and assembly name. Earlier local builds and the original plugin keep their own settings. To migrate, exit Flow Launcher, back up the old `Settings.json`, and copy it into the new `Flow.Launcher.Plugin.ChatGPTModels` settings directory. The original Python settings schema can also be read. Both plugins default to `ai`; change one keyword in Flow settings if you keep both installed.

## Build and test

Install the .NET 9 SDK and Flow Launcher. In PowerShell:

```powershell
dotnet run --project tests/Tests.csproj -c Release
.\build.ps1
# For a portable/custom installation:
.\build.ps1 -FlowLauncherDirectory 'D:\Apps\FlowLauncher'
```

The build produces `artifacts/Flow.Launcher.Plugin.ChatGPTModels.zip`. It does not install the plugin. Tests use simulated HTTP responses and actual WPF controls; they do not require API credentials. The generated settings preview is at `artifacts/settings-preview.png`.

The current entry point is under `src/`. Original Python files, `lib/`, and the old settings template are retained as reference and are not distributed in the release ZIP.

## Troubleshooting

A successful model fetch does not prove that the chat endpoint exists. If chat returns HTTP 404, check that the base URL points to the inference API, and that reverse-proxy rules forward chat requests to the correct service. Changing the catalog URL cannot fix an absent chat route. Authentication failures, rate limits, connection errors, and timeouts are reported separately.

## Publishing

See [PUBLISHING.md](PUBLISHING.md) for the GitHub release and Flow plugin store submission process. The tag-triggered workflow builds and releases the plugin ZIP. The generated store submission manifest is under `publishing/`.

## Credits and license

Based on [MichielvanBeers/Flow.Launcher.Plugin.ChatGPT](https://github.com/MichielvanBeers/Flow.Launcher.Plugin.ChatGPT). Original copyright notices are preserved; modifications are by liboy. See [LICENSE](LICENSE).

The ChatGPT / OpenAI logo belongs to OpenAI; see [Images/NOTICE.txt](Images/NOTICE.txt) for the official asset source. This is an independent community plugin and is not affiliated with or endorsed by OpenAI.
