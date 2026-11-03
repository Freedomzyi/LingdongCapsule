# 灵动胶囊插件（开发中）

当前支持用户主动加载本地 DLL。插件实现本仓库独立定义的 `Capsule.PluginApi`，不宣称兼容 NotchPeninsula DLL。安装包、市场、版本升级和完整排序仍待实现。

## 开发接口

- 使用 .NET 10，目标 `net10.0-windows10.0.19041.0`，启用 WPF，并引用 `src/Capsule.PluginApi/Capsule.PluginApi.csproj`。
- 程序集只导出一个非抽象 `ICapsulePlugin` 实现，并提供无参数构造函数。
- `Identity` 提供唯一 ID、显示名和版本。ID 为小写字母开头的 3～80 位字母、数字、点或连字符；版本使用 `System.Version` 支持的格式。
- `CreateWidget` 返回没有父级的 WPF 组件；`Update` 接收时间、主题和媒体快照，在 UI 线程更新，不应执行阻塞网络或重计算。
- `Dispose` 释放自己的计时器、事件订阅和其他资源。回调失败时宿主移除该插件组件并记录故障，不中断其他插件更新。
- `ICapsulePluginHost` 提供当前媒体、按插件 ID 隔离的字符串设置及通知请求。通知仍遵守软件总开关、来源过滤、隐私和安静时段。

## 使用与限制

在“我的插件”选择可信 DLL，确认后加载。DLL 及依赖保留在原目录，重启会重新加载已启用项；停用会移除组件并从启用列表移除路径，不删除源文件。插件与宿主同进程运行，不是安全沙箱。

基础示例及故障用例位于 `tests/Capsule.WindowsTests/PluginTests.cs`。该测试程序集只用于验证，不作为用户插件发布。复验命令：`dotnet run --project tests/Capsule.WindowsTests -c Release -- --plugin-test`。

已验证独立程序集加载、共享 API 类型、组件更新、停用释放、回调异常隔离，以及实际岛体中的插件时钟显示。插件市场、打包安装、混合 DPI、跨版本兼容与复杂插件资源释放仍需专项验收。
