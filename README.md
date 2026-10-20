# Codex Quota Widget · 额度圆环

一个贴合 Codex 侧边栏风格的 Windows 桌面额度组件。用 30px 蓝色渐变圆环查看五小时剩余额度，点击展开五小时和每周额度卡片。

**默认不置顶**：随 Codex / GPT 窗口移动、调整大小、最小化和恢复。无托盘图标、无任务栏按钮、无悬浮提示。

这是社区工具，与 OpenAI 无隶属关系。支持 Windows 10 / 11 x64，使用系统自带的 .NET Framework 4.7.2+，无需安装 Node.js 或 .NET SDK。

## 让 Agent 帮你安装

把下面这句话复制给能操作你电脑的 Agent：

> 请帮我安装 https://github.com/LittleDHeart/codex-quota-widget 。先阅读仓库 README.md、INSTALL.md 和 AGENTS.md，检查 Windows 和 Codex 登录状态，再执行 install.ps1 安装并启动。保留已有设置，默认不启用开机启动，安装后告诉我如何点击查看额度、拖动和退出。

完整步骤和参数见 [INSTALL.md](INSTALL.md)。Agent 开发及安装约定见 [AGENTS.md](AGENTS.md)。

## 自己安装

先安装并用 ChatGPT 账号登录 Codex，然后在 PowerShell 执行：

```powershell
git clone https://github.com/LittleDHeart/codex-quota-widget.git
if ($LASTEXITCODE -ne 0) { throw 'Clone failed' }
Set-Location codex-quota-widget
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

脚本会从源码编译，安装到 `%LOCALAPPDATA%\CodexQuotaWidget`，创建开始菜单快捷方式并启动。全程无需管理员权限；`Bypass` 只用于本次 PowerShell 进程。默认不开机启动，可稍后在右键菜单开启，或安装时加 `-StartWithWindows`。

没有 Git 时，在 **Code → Download ZIP** 下载完整源码并解压，再运行相同的安装命令。若 [Releases](https://github.com/LittleDHeart/codex-quota-widget/releases) 中提供编译版，也可解压直接运行 `CodexQuota.exe`。安装位置、升级、卸载及 CLI 路径设置详见 [安装说明](INSTALL.md)。

## 使用

| 操作 | 效果 |
| --- | --- |
| 点击圆环 | 展开 192×192 圆角卡片，显示五小时 / 每周剩余比例及重置倒计时 |
| 鼠标离开圆环与卡片 | 约 350ms 后收起；再次点击、点击外部或 Esc 也可收起 |
| 拖动圆环 | 调整位置；距离宿主窗口边缘 12px 内自动吸附 |
| 移动或缩放 Codex / GPT 窗口 | 组件保持相对边缘的距离 |
| 最小化 / 恢复宿主窗口 | 组件自动隐藏 / 显示 |
| 右键 → 始终置顶 | 开启后独立桌面悬浮；取消后恢复跟随 |
| 右键菜单 | 设置开机启动、浅/深色底色、刷新间隔、默认位置，或退出 |
| 再次启动程序 | 唤回已运行的组件，不重复创建实例 |

圆环只表示 **5 小时剩余额度**，中心无数字；底环是淡蓝色，前景是 4px 宽的蓝色渐变。卡片与圆环使用统一配色，剩余比例使用大号文字。默认跟随系统浅/深色主题，右键可手动调整。

首次安装出现在宿主窗口左下角，可拖动到头像附近，再右键“将当前位置设为默认”。跟随模式下，宿主关闭或尚未打开时组件会隐藏并等待，重新打开宿主后恢复。不会修改宿主界面或控制宿主窗口。窗口识别依赖官方 Windows 应用的进程及安装路径，应用更新或非标准安装可能需要适配。

## 额度从哪里来，如何重置

通过本机 `codex app-server --stdio` 调用只读的 `account/rateLimits/read`，同时接收额度更新通知；默认每 30 秒轮询，可选 15 / 30 / 60 / 300 秒。圆环表示剩余比例，即 `100 - usedPercent`。

在线连接失败时，尝试读取本地 Codex 会话中的最新额度记录，并逐步退避重试在线接口。**本地快照可能滞后**；组件不等于实时服务器余额。缺少额度显示 `—`；没有数据或到了重置时间都不会自行推断成 100%。

五小时和每周额度各自按账号返回的时间重置，卡片显示倒计时；到时等待服务端新数据。右键“打开官方额度页面”可查看账号提供的具体选项。组件不会手动重置额度、购买额度或改变订阅。

## 本地数据与隐私

- 组件不创建聊天、不发出模型请求、不收集遥测。
- 不直接读取或复制 `auth.json`。Codex CLI 使用你现有的本机登录状态。
- 在线不可用时会扫描 `$CODEX_HOME/sessions`（默认 `~/.codex/sessions`）中最近 12 个会话文件的尾部，只解析额度字段和时间，不保存聊天内容。
- `preferences.json` 仅保存窗口位置和偏好，位于程序同目录；目录需要可写。
- `--probe` 生成含个人额度的 `probe.json`，启动异常记录 `error.log`；分享问题前请检查并脱敏。这些文件和预览图片均被 `.gitignore` 排除。
- 继承已有代理环境变量；未设置 `HTTPS_PROXY` 时尝试使用 Windows 手动代理配置。不自动配置 PAC 或绕过组织网络策略。

## 构建、测试和打包

```powershell
# 独立输出目录，不覆盖正在运行的程序
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\out\build

# 离线模型、stdio、WPF 交互与原生窗口跟随测试
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1

# 生成干净的 Windows ZIP 与 SHA256 校验文件
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

测试使用模拟额度接口和自己的测试窗口，无需账号登录，也不移动或最小化用户的 Codex 窗口。UI 测试需要 Windows 桌面会话。GitHub Actions 提供 Windows 构建与测试，产物保存在运行记录的 Artifacts。

| 文件 | 用途 |
| --- | --- |
| `Widget.cs` | 额度解析、RPC、本地快照、设置及启动入口 |
| `RingWidget.cs` / `Ring.xaml` | 圆环、卡片与交互 |
| `WindowAttachment.cs` | 窗口识别、吸附、跟随与层级 |
| `install.ps1` / `uninstall.ps1` | 当前用户安装、升级和卸载 |
| `package.ps1` | 按文件白名单打包，排除个人配置与诊断数据 |

诊断参数：`--probe` 检查额度连接，`--attachment-probe` 检查宿主识别，`--quit` 退出运行中的组件。诊断结果写入程序目录，不应原样公开。

## License

[MIT](LICENSE) · Copyright 2026 LittleDHeart
