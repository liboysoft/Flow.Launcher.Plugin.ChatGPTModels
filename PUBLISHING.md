# 发布到 GitHub 与 Flow 插件市场

本流程适用于 **ChatGPT Models 1.0.0**，作者 **liboy**，目标仓库：

[liboysoft/Flow.Launcher.Plugin.ChatGPTModels](https://github.com/liboysoft/Flow.Launcher.Plugin.ChatGPTModels)

项目已准备独立插件 ID、MIT 署名、中英文 README、自动构建与 Release 工作流、市场提交清单。本地准备不代表已经创建远程仓库、上传代码或提交市场审核。

## 一、创建公开仓库并上传源码

1. 登录 GitHub 账号 **liboysoft**，进入 [新建仓库页面](https://github.com/new)。
2. 仓库名填写 `Flow.Launcher.Plugin.ChatGPTModels`，选择 **Public**。
3. 创建空仓库，不额外初始化 README、LICENSE 或 .gitignore；这些文件本地已有。
4. 在本项目目录打开 PowerShell，先检查，再提交并推送：

```powershell
git status --short
git diff
# 检查将提交的文件，确认没有 API Key 或本机 Settings 文件。
git add .
git commit -m "Release ChatGPT Models 1.0.0 by liboy"
git remote -v
# 以下两条仅在首次从参考项目切换远程时执行：
git remote rename origin upstream
git remote add origin https://github.com/liboysoft/Flow.Launcher.Plugin.ChatGPTModels.git
git push -u origin main
```

当前本地 origin 仍指向参考项目；保留为 upstream 可以继续追踪原项目。若自己的 origin 已配置好，不要重复 rename/add。GitHub 登录和推送认证由你在 Git 凭据提示中完成。

`.dotnet/`、`artifacts/`、`.env` 和编译输出已忽略。不要将 `%APPDATA%\FlowLauncher\Settings` 复制进仓库。原项目 MIT 版权信息已保留，修改部分版权行增加了 liboy。

## 二、发布 v1.0.0 安装包

本机需有 .NET 9 SDK 和 Flow Launcher。若使用本项目已下载的本地 SDK，可把下列 `dotnet` 替换为 `.\.dotnet\dotnet.exe`。

```powershell
dotnet run --project tests/Tests.csproj -c Release
.\build.ps1
```

构建输出：`artifacts/Flow.Launcher.Plugin.ChatGPTModels.zip`。建议先使用正确的实际 API 地址验证模型拉取、选择和聊天。新公开版使用独立 ID 和程序集名，原测试版设置迁移方法见中文 README。

推送 main 后，进入 GitHub 仓库的 **Actions**，确认普通构建成功，再发布标签：

```powershell
# 此工作目录从参考项目克隆，已带有它原来的 v1.0.0 标签。
# 首次发布前保留原标签，再把本地 v1.0.0 指向刚提交的新版代码：
git tag upstream/v1.0.0 v1.0.0
git tag -f v1.0.0
git push origin refs/tags/v1.0.0
```

上面只重设本地标签，不改动原项目远程；不要使用 `git push --tags` 将参考项目全部旧标签发布到新仓库。备份标签仅创建一次。如果新仓库已经有 v1.0.0，则先检查对应提交与 Release，不要强制覆盖远程标签。

工作流会自动测试、构建、创建 GitHub Release，并上传 `Flow.Launcher.Plugin.ChatGPTModels.zip`。无需个人访问令牌，使用 GitHub 内置 `GITHUB_TOKEN`。若组织策略限制仓库写入，需要由管理员允许工作流的 `contents: write` 权限。

发布前校验：Git 标签必须等于 `v` 加 `plugin.json` 的版本，项目文件版本也必须一致，Website 必须指向当前仓库。不要提前手动创建同名 Release；如果同名 Release 已存在，工作流会停止，需先检查原因。

发布后检查下载链接：

[Flow.Launcher.Plugin.ChatGPTModels.zip · v1.0.0](https://github.com/liboysoft/Flow.Launcher.Plugin.ChatGPTModels/releases/download/v1.0.0/Flow.Launcher.Plugin.ChatGPTModels.zip)

ZIP 根目录应直接包含 `plugin.json`、`Flow.Launcher.Plugin.ChatGPTModels.dll`、依赖描述、LICENSE 和 Images 目录。市场使用该插件 ZIP，而不是 GitHub 自动生成的 Source code ZIP。

本地可通过以下命令安装；该地址在 Release 发布后才有效：

```text
pm install https://github.com/liboysoft/Flow.Launcher.Plugin.ChatGPTModels/releases/download/v1.0.0/Flow.Launcher.Plugin.ChatGPTModels.zip
```

## 三、申请上架 Flow 插件市场

按照 [Flow 官方提交说明](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest#how-to-submit-your-plugin)：

1. Fork [Flow-Launcher/Flow.Launcher.PluginsManifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest) 到 liboysoft，并在自己的 fork 中创建一个提交分支。
2. 把本项目 `publishing/ChatGPT Models-ffc644e7-e67c-4271-9057-3e7468b6ba75.json` 复制到对方仓库的 **plugins/** 目录。不要直接修改汇总文件 plugins.json。
3. 确认清单中的作者 liboy、版本 1.0.0、源码地址、Release 下载地址和图标 CDN 地址均正确。最低 Flow 版本使用已验证的 2.1.3。
4. 提交这个 JSON，向官方仓库的 **main** 分支发起 Pull Request。
5. PR 中说明多端点管理、主动拉取模型、模型选择功能，并注明基于原项目的独立改进版及 MIT 许可证。
6. 等待自动检查及 Flow 团队审核；如有修改意见，继续提交到同一分支。合并后仍需等待 CDN 同步，商店不一定立即可见。

官方要求自动构建及发布，并遵守 Plugin Store policy。本项目已准备相应工作流；保留原作者署名、图标归属及非官方说明。

PR 标题可用：

```text
Add ChatGPT Models by liboy
```

PR 描述可用：

```markdown
Adds ChatGPT Models 1.0.0 by liboy.

- Manage multiple OpenAI-compatible endpoints in a native settings panel.
- Fetch available models and select or manually enter a model ID.
- Copy answers, open answers in an editor, and optionally save conversations.

Independent C# / WPF adaptation based on MichielvanBeers/Flow.Launcher.Plugin.ChatGPT, with the original MIT notice preserved.

Source: https://github.com/liboysoft/Flow.Launcher.Plugin.ChatGPTModels
Release: https://github.com/liboysoft/Flow.Launcher.Plugin.ChatGPTModels/releases/tag/v1.0.0
Requires Flow Launcher 2.1.3 or later.
Automated build and release workflow is included.
```

## 四、之后怎么更新

同时修改 `plugin.json` 和 `src/Flow.Launcher.Plugin.ChatGPT.csproj` 的版本，例如 1.0.1；更新 RELEASE_NOTES.md，然后重新生成清单：

```powershell
.\scripts\Configure-Publishing.ps1 -GitHubOwner liboysoft
```

测试、构建后提交到 main，再推送 `v1.0.1` 标签即可。官方市场会定期检查新 Release，通常不需要每次重新提交上架 PR，具体以官方仓库规则为准。
