# 参照源码设置入口映射

2026-11-04 只读核对本机定制源码 UI 中 52 个字面量 SaveSetting 键。实际安装 EXE 的 SHA256 仍为 B94C964328745D21F16340473AA1E15671510B2D0A7BD2E7E02A644B8BA19E2B。原始入口清单保存在 artifacts/reference-settings-inventory.json；不修改参照源码、安装文件或注册表。

这是功能入口映射，不是注册表导入承诺，也不证明每个实际状态均已验收。独立产品使用自己的设置文件；现行知识库正式规范明确合并/取消的旧入口按规范处理。

| 参照键 | 独立实现中的去向及差距 |
| --- | --- |
| `ThemeMode` | Settings.ThemeMode，整个自有界面主题 |
| `IslandAcrylic`、`IslandLiquidGlass` | MaterialKind 单选，避免两个开关冲突；真实背板见材质专项 |
| `BgOpacityLevel` | BackgroundOpacity；实体背景透明度/亚克力主题涂层，玻璃时隐藏 |
| `LiquidGlassQuality` | GlassQuality；只在玻璃参数区域显示 |
| `CustomFontPath` | FontFilePath；已补文件加载、恢复、失效反馈与唯一默认入口 |
| `Custom_Dpi` | VisualScale，独立于 Windows DPI 的整体视觉比例 |
| `Custom_MediaH` | CompactHeight，默认 35 |
| `Custom_NotchBottomR` | BottomRadius |
| `Custom_ToastH` | NotificationHeight |
| `GlobalWidthLimit` | Width，全局上限/播放固定宽度同一入口 |
| `Custom_MediaW`、`Custom_StandbyW`、`Custom_ToastW`、`ToastMinimum` | 旧宽度入口按正式规范收敛到全局宽度，不重建重复入口 |
| `MotionMilliseconds`、`SmoothMotion` | MotionMs；新时长替代旧布尔 UI，不让旧关闭值压过时长 |
| `ToastIconSize`、`IconGap` | NotificationIconSize、NotificationIconGap，随全局宽度约束 |
| `GroupNotifications`、`ExpandNotifications` | GroupNotifications、ExpandNotifications |
| `ToastContentMode`、`ToastEnabled` | NotificationMode 三模式、NotificationsEnabled |
| `ToastSoundEnabled`、`ToastSoundVolume` | NotificationSoundEnabled、NotificationSoundVolume |
| `ShowInCapture` | ShowInCapture，仅主岛体的 Windows 捕获属性 |
| `ShowTomorrow` | ShowTomorrow，空闲信息面板唯一天气设置 |
| `AutoHide`、`FocusAutoHide`、`PauseAutoHide`、`FullscreenAutoHide` | 自动隐藏总开关与三个组合条件 |
| `PassthroughMode` | Passthrough、原生命中测试与唤醒入口 |
| `TopmostEnabled` | Topmost |
| `NotchStyle`、`TargetMonitorIndex` | IslandShape、按设备身份保存的 MonitorId；实际混合双屏仍待验收 |
| `StandbyActive`、`StandbyScene`、`StandbyToggleByDoubleClick` | StandbyActive、StandbyScene、StandbyDoubleClick |
| `MediaControl`、`TargetPlatform` | MediaEnabled、MediaSourceKind |
| `ManualSessionMatch`、`ManualSessionAppId` | ManualMediaSource，空值自动，具体身份手动且保留未知身份 |
| `MediaInteractionMode`、`MediaAppLaunchEnabled` | ExpandedMediaControls、ReturnToPlayer；左右键与对应设置页路由已补 |
| `MediaClockSwitch`、`CoverProgress` | MediaClockSwitch、CoverProgress |
| `LyricsEnabled`、`TranslationEnabled`、`LyricScanEnabled`、`ScrollLyrics` | LyricsEnabled、TranslationEnabled、KaraokeEnabled、ScrollLyrics |
| `LyricDelayOffset` | LyricOffsetSeconds；已补无硬边界的 0.1 秒加减、还原、保存和非有限值保护 |
| `MspEnabled` | MspEnabled，实验性本机接入默认关闭 |

另有通过帮助方法或插件宿主写入的设置，不能因未出现在上述字面量列表而漏算：显示组件启用/顺序、六个全局热键及录制、十四类内置声音/文件选择、十二项玻璃参数、插件包/市场/升级/停用与偏好、两个定制组件、手工城市、天气缓存和内存排行。它们继续以 PARITY.md 的功能矩阵及各专项记录追踪。

本轮静态核对已实际发现并推动字体文件、材质显示条件与点击路由差距修正，并补齐歌词偏移范围差距。运行版逐页对照、物理输入、真实第三方、安装维护和 Owner 体验仍是独立门槛，不能用这张键表替代。
