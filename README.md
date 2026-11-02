# 灵动胶囊 · LingdongCapsule

Windows 桌面交互胶囊。把时间、系统媒体和通知收在屏幕顶部，以少量操作完成日常查看和控制。

**0.1.0 预览版 · Windows 11 x64。** 当前验证与已知限制见 [验证记录](docs/VALIDATION.md)。预览版不代表全机型兼容性已验收。

## 使用

下载发布附件中的 `LingdongCapsule-win-x64.zip`，完整解压后运行 `LingdongCapsule.exe`。自包含包无需预装 .NET，不能只复制一个 EXE。首次运行打开设置。

- 点击胶囊：播放时展开媒体控制；暂停后点击时钟仍可展开控制；无媒体时打开设置。
- 通知：第一次点击展开，再次点击收起；预览默认 6 秒、展开默认 20 秒收起。连续通知显示最新一条。
- 右键胶囊或双击系统托盘图标：设置。托盘右键菜单可测试通知、收起和退出。
- 设置中的本地测试通知用于检查交互，**不代表真实系统通知已经接通**。真实通知需在设置中主动授权，只覆盖 Windows 通知中心可提供的来源；不支持仅有提示声的微信消息。
- 系统横幅不被自动关闭。若同时运行其他桌面灵动岛，两者可能重叠；请按需从托盘退出其中一个。

配置位于 `%LOCALAPPDATA%/LingdongCapsule/settings.json`，升级替换程序目录即可保留设置。卸载先从托盘退出，再删除解压目录；是否删除个人设置由你决定。无注册表修改、无开机启动、无服务器和遥测；通知正文不写入磁盘。

## 从源码构建

安装 .NET 10 SDK（版本策略见 `global.json`），在仓库根运行：

```powershell
./scripts/build.ps1
```

生成 `artifacts/LingdongCapsule-win-x64.zip` 和 `artifacts/SHA256SUMS.txt`。源码调试：

```powershell
dotnet run --project src/Capsule.App
dotnet run --project tests/Capsule.Tests
```

`--settings` 启动时打开设置，`--inspect` 在任务栏显示主岛体，便于辅助功能检查。

## 项目边界

核心状态逻辑在 `src/Capsule.Core`；Windows 接入及 WPF 界面在 `src/Capsule.App`。不依赖旧版 NotchPeninsula 的源码、DLL 或资源。[开发方案](docs/PLAN.md) · [来源与第三方声明](THIRD-PARTY-NOTICES.md) · [MIT 许可证](LICENSE)

目前不含歌词、天气、插件市场、进程清理和自动更新。播放器未提供信息时显示降级状态；系统媒体会话并不保证歌词、封面或所有控制指令可用。
