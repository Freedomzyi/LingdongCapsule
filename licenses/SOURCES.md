# 发行包依赖许可来源

灵动胶囊源码使用仓库根目录的 MIT 许可。依赖与 Windows 原生组件分别适用自己的许可；本目录不把整个二进制发行包重新许可为 MIT。

- `NAudio-LICENSE.txt`：NAudio v2.2.1 的原始许可，版权行为 Copyright 2020 Mark Heath。来源：https://raw.githubusercontent.com/naudio/NAudio/v2.2.1/license.txt 。NuGet 的 Core/Wasapi 2.2.1 元数据另声明 © Mark Heath 2023，原始 nuspec 一并保留在 package-metadata。
- `ModelSensingProtocol-NOTICE.txt`、`SPDX-MIT.txt` 与相应 nuspec：0.1.0-alpha.1 的公开 NuGet 元数据声明 MIT、作者 ModelSensingProtocol，包内没有独立 LICENSE 或版权行；保留元数据与其链接的标准许可文本，不编造作者版权年份。来源：https://www.nuget.org/packages/ModelSensingProtocol/0.1.0-alpha.1 、https://licenses.nuget.org/MIT 、https://raw.githubusercontent.com/spdx/license-list-data/main/text/MIT.txt 。元数据所指仓库在 2026-11-04 检查时返回 404，不将标准模板冒充从该仓库取得的作者许可文件。
- `Microsoft.NETCore.App-LICENSE.txt`、`Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt`、`Microsoft.WindowsDesktop.App-LICENSE.txt`：构建时从实际恢复的运行时包复制，版本在 components.json 中记录，保留原字节。
- `NET-Windows-Library-LICENSE.txt`：构建时从实际 .NET 安装根目录复制原始 Windows .NET Library 条款。官方条款：https://dotnet.microsoft.com/en-us/dotnet_library_license.htm 。
- `Windows-SDK-LICENSE.txt`：https://learn.microsoft.com/en-us/legal/windows-sdk/license 的许可证正文转为纯文本，包含其原文 EULAID；不包含网页导航、脚本和页脚。
- `Windows-SDK-Targeting-LICENSE.rtf`：Microsoft.Windows.SDK.NET.Ref 的原始许可链接 https://aka.ms/WinSDKLicenseURL 所返回的 RTF，保留原文件；其 NuGet 元数据一并保留。

Windows .NET 组件的适用范围按微软说明区分：https://github.com/dotnet/core/blob/main/license-information-windows.md 。该说明将 coreclr、部分 WPF 原生组件列于 .NET Library License，将 D3DCompiler_47_cor3.dll 列于 Windows SDK License，其余按该说明适用 MIT。不要只依据 NuGet 的 MIT 标签推断全部 Windows 原生二进制都适用 MIT。

components.json 由构建生成，记录实际依赖版本及本目录文件哈希；不记录本机 NuGet 缓存路径。新依赖或固定元数据版本不符时，构建要求补齐来源后继续。
