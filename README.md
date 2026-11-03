# 灵动胶囊 · LingdongCapsule

Windows 桌面交互胶囊。把时间、系统媒体和通知收在屏幕顶部，以少量操作完成日常查看和控制。

**开发中 · Windows 11 x64。目标已明确为本机 NotchPeninsula 定制版的全部功能与交互 1:1 对齐。当前基础原型尚不完整，不构成最终交付，也没有正式 Release。** 完整差距见 [对齐清单](docs/PARITY.md)，已完成部分的证据见 [验证记录](docs/VALIDATION.md)。

## 使用

本机 MSP 工具、内容块与图片读取规则见 [外部接入](docs/EXTERNAL.md)。

安装、升级备份、回退与卸载边界见 [安装维护](docs/INSTALLATION.md)。

可选系统通知头像的数据来源和限制见 [通知图片](docs/NOTIFICATIONS.md)。

已安装字体、本地字体文件与恢复默认见 [字体](docs/FONTS.md)。

目前可从源码构建开发包；正式发布后下载附件中的 `LingdongCapsule-win-x64.zip` 并完整解压。双击 `安装.cmd` 打开 iOS 风格安装页，点击“安装并打开”安装到当前用户目录并创建开始菜单入口；不需要管理员权限。也可直接运行 `LingdongCapsule.exe` 使用便携版。自包含包无需预装 .NET，不能只复制一个 EXE。

首次运行展示四页引导，可选择形态、主题、材质、显示器、普通/待机模式和开机启动，点击“开始使用”后保存。自启默认关闭，由用户自行选择；完成后只驻留系统托盘，不占任务栏。“稍后设置”保留原有选择，关闭未完成的引导不会冒充完成。设置里的“使用指南”可再次打开。

- 点击胶囊：播放时展开媒体控制；暂停后点击时钟仍可展开控制；无媒体时打开已启用的空闲信息面板，关闭该功能后打开设置。
- 通知：第一次点击展开，再次点击收起；预览默认 6 秒、展开默认 20 秒收起。连续通知显示最新一条。
- 右键胶囊或双击系统托盘图标：设置。托盘右键菜单可测试通知、收起和退出。
- 折叠高度默认 **35 个逻辑像素**，可调 32～60；同时控制时钟与紧凑媒体形态。展开内容独立留足空间，不靠缩小字号塞入。全局宽度上限 80～300，收放时长 150～900 ms。
- “视觉比例”默认 1 倍，最低 0.5 倍，以 0.05 调整，可一键还原。整体缩放主岛体，独立于 Windows DPI；宽高设置以 1 倍时的逻辑尺寸为准。屏幕不足时暂时限制实际倍率或宽度，保存的偏好不变。
- 全部自绘页面使用统一的 iOS 风格圆角、卡片、滑块与胶囊开关；设置和岛体支持深色、浅色及跟随系统，岛体可选实体、亚克力或液态玻璃。包含原创程序和托盘图标。
- 设置中的本地测试通知用于检查交互，**不代表真实系统通知已经接通**。真实通知需在设置中主动授权，只覆盖 Windows 通知中心可提供的来源；不支持仅有提示声的微信消息。
- 系统横幅不被自动关闭。若同时运行其他桌面灵动岛，两者可能重叠；请按需从托盘退出其中一个。

配置位于 `%LOCALAPPDATA%/LingdongCapsule/settings.json`，升级安装或替换程序目录即可保留设置。安装版可从 Windows 已安装的应用中卸载，便携版退出后移除解压目录。个人设置默认保留。开机启动只写入当前用户的本程序启动项；安装只登记本程序的开始菜单和卸载入口，不改其他应用策略。无服务器和遥测，通知正文不写入磁盘。

## 从源码构建

安装 .NET 10 SDK（版本策略见 `global.json`），在仓库根运行：

```powershell
./scripts/build.ps1
```

构建脚本优先使用仓库本地可选的 `.tools/dotnet/dotnet.exe`，不存在时使用系统 SDK。本地 SDK 不提交 Git，也不随软件发布；项目不依赖任何会话临时目录。

生成 `artifacts/LingdongCapsule-win-x64.zip` 和 `artifacts/SHA256SUMS.txt`；同时构建独立插件包及校验和到 `artifacts/plugins`，并随主包提供。仅构建插件可运行 `./scripts/build-plugins.ps1`。插件源码和接口见 [插件说明](docs/PLUGINS.md)。源码调试：

候选包使用 `./scripts/build.ps1 -RequireClean`，包内 BUILD-INFO.json 记录提交、源码是否有未提交改动、SDK、程序和许可清单哈希。每次从全新暂存目录打包，插件只收录本次校验清单中的包；生成目录属于可再生构建输出。依赖许可保存在 licenses，来源及适用范围见 [许可来源](licenses/SOURCES.md)。

`./scripts/test-published.ps1 -RequireClean` 解压并启动自己的安装入口，核对包内运行时与退出，不执行安装。`-MainStartup` 另外检查主程序和首次引导，只允许在没有本程序数据的新用户环境使用；CI 使用独立 runner 执行。两者都不替代干净 Windows 的完整安装维护验收。

```powershell
dotnet run --project src/Capsule.App
dotnet run --project tests/Capsule.Tests
```

`--settings` 启动时打开设置，`--inspect` 在任务栏显示主岛体，便于辅助功能检查。

Windows 集成验证默认只读，**不会请求权限或打印通知正文**：

```powershell
dotnet run --project tests/Capsule.WindowsTests
```

可选的 `-- --window-scenario` 在 STA 消息循环中实例化实际 WPF 窗口，用独立临时测试身份发送真实 Windows 通知，并操作 QQ 音乐播放、暂停和切歌来验证窗口恢复。它会改变当前歌曲，须在专门的交互测试时运行；测试仅清理自己的通知、快捷方式和注册表身份，不进入 CI。

## 项目边界

核心状态逻辑在 `src/Capsule.Core`；Windows 接入及 WPF 界面在 `src/Capsule.App`。不依赖旧版 NotchPeninsula 的源码、DLL 或资源。[开发方案](docs/PLAN.md) · [来源与第三方声明](THIRD-PARTY-NOTICES.md) · [MIT 许可证](LICENSE)

歌词、天气、插件体系、进程管理等均已纳入完整对齐范围，正在开发。播放器和接口未提供的信息不推测补齐，具体进度以对齐清单为准。
