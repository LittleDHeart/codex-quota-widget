# 安装、升级与卸载（含 Agent 操作步骤）

项目：https://github.com/LittleDHeart/codex-quota-widget

## 环境要求

- Windows 10 / 11 x64，.NET Framework 4.7.2 或更新版本（通常系统已自带）。暂不支持 macOS、Linux 或原生 ARM64 安装。
- 已安装并用 ChatGPT 账号登录 Codex。仅 API Key 登录的环境可能不提供订阅额度。
- 源码安装需要 Git 和 Windows PowerShell 5.1+；无需 Node.js、Python、Visual Studio 或 .NET SDK。
- 安装目录必须允许当前用户写入，用于保存 `preferences.json`。默认使用 `%LOCALAPPDATA%\CodexQuotaWidget`，不需要管理员权限。

## 交给 Agent 安装

把下面这段话发给 Codex、Claude Code 或其他能操作本机文件和终端的 Agent：

> 请安装 https://github.com/LittleDHeart/codex-quota-widget 。先读取仓库 README.md、INSTALL.md 和 AGENTS.md，确认当前系统是 Windows x64 且已经登录 Codex。下载完整仓库，检查 install.ps1，再用 Windows PowerShell 执行源码安装。保留已有 preferences.json，默认不启用开机启动，默认取消置顶并跟随 Codex 窗口。安装后启动组件，告诉我安装位置和右键菜单的用法。若缺少 Codex 或账号登录，请让我通过官方流程完成，不要索取或复制 token、auth.json，也不要替我关闭安全软件。

Agent 可执行的命令如下；如果目录已存在，应先检查是不是本仓库，再更新，不能覆盖其他项目。

```powershell
git clone https://github.com/LittleDHeart/codex-quota-widget.git
if ($LASTEXITCODE -ne 0) { throw 'Clone failed' }
Set-Location codex-quota-widget
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

这里的 `-ExecutionPolicy Bypass` 只对本次 PowerShell 进程生效，不修改系统执行策略。组织策略禁止脚本时，应遵循组织的安装要求。

安装脚本会先用 Windows 自带的 C# 编译器构建，再复制程序和说明文档，并创建开始菜单快捷方式。默认随后启动；检测不到 Codex / GPT 窗口时，跟随模式会暂时隐藏，打开 Codex 后出现。

### 安装参数

```powershell
# 明确需要时才启用开机启动
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -StartWithWindows

# 只安装，不启动、不创建快捷方式（用于部署或验证）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -NoLaunch -NoShortcut

# 自定义安装目录，请选择单独的可写目录
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -InstallDirectory "D:\Apps\CodexQuotaWidget"
```

默认启动文件：`%LOCALAPPDATA%\CodexQuotaWidget\CodexQuota.exe`。重复执行脚本可升级同一目录，保留设置；如果该目录的组件正在运行，会先请求它正常退出。

开机启动保存到当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，条目名为 `CodexQuotaWidget`。不创建系统服务。未指定 `-StartWithWindows` 时不更改已有启动项；换安装目录后需在新组件右键菜单重新勾选开机启动。

## 不使用 Git 安装

在 GitHub 仓库页面选择 **Code → Download ZIP**，解压完整源码，然后在该目录执行 `install.ps1`，命令同上。不要只下载单个 `.cs` 文件。

如果 [Releases](https://github.com/LittleDHeart/codex-quota-widget/releases) 中有已编译的 Windows ZIP，可解压后直接运行 `CodexQuota.exe`，也可以运行 ZIP 内的 `install.ps1` 安装到用户目录。不要直接在压缩包内运行。程序未进行代码签名；需要自行构建时，使用仓库源码和 `build.ps1`。

## 找不到 Codex CLI

自动查找顺序：`CODEX_QUOTA_CLI` 指定的文件、`%LOCALAPPDATA%\OpenAI\Codex\bin\<版本>\codex.exe`、PATH 中的 `codex.exe`。

安装位置特殊时，可在当前 PowerShell 设置后启动（仅影响本次进程及其子进程）：

```powershell
$env:CODEX_QUOTA_CLI = 'D:\YourCodexInstall\codex.exe'
& "$env:LOCALAPPDATA\CodexQuotaWidget\CodexQuota.exe"
```

应指向真正的 `codex.exe`，而不是桌面应用的 `ChatGPT.exe` 或 `.cmd` 启动器。需要持久化时通过 Windows 用户环境变量设置，重新登录 Windows 后生效。

## 更新和卸载

源码安装的更新：确认工作区没有要保留的源码改动后执行 `git pull --ff-only`，成功后重新运行 `install.ps1`，保持安装目录一致。使用发布 ZIP 时下载新 ZIP 并重新安装。程序不自动下载或执行更新。

```powershell
# 默认保留 preferences.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\CodexQuotaWidget\uninstall.ps1"

# 同时移除位置和偏好
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\CodexQuotaWidget\uninstall.ps1" -RemovePreferences
```

自定义目录应执行该目录内的卸载脚本。脚本只移除本组件已知文件，以及确实指向此安装目录的启动项和快捷方式；不会递归删除其他文件，也不会卸载 Codex。

## 安装后验证

1. 打开 Codex 桌面窗口，找到左下角蓝色小圆环。可拖动到合适位置。
2. 点击后应显示五小时 / 每周额度和各自重置倒计时；窗口未提供的数据应显示 `—`。
3. 移动、最小化和恢复 Codex 窗口，确认组件随之移动、隐藏和显示。右键启用“始终置顶”则改为独立悬浮。
4. 如需检查连接，在安装目录运行 `CodexQuota.exe --probe`，随后查看 `probe.json` 的 `source`：`api` 表示在线读取，`session` 表示本地快照。该文件含个人额度和时间，不应原样上传。

遇到启动异常查看同目录 `error.log`。额度暂时不可用时先检查 Codex 登录、CLI 路径和网络代理；本地快照只有在 Codex 写入新额度记录后才更新。不要把空值当作 100%。
