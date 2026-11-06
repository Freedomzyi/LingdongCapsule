# 来源和依赖

`assets/support-wechat.png` 为作者提供的收款码原图，用于“关于软件”的自愿赞赏展示，原始字节保留。该资料的分发不授予其中第三方标识的额外使用权。

本项目源码适用根目录 MIT 许可；二进制发行包内的运行时和依赖分别适用自己的许可，不将全部 Windows 原生组件笼统称为 MIT。发行包的 `licenses/` 提供许可正文、原始 NuGet 元数据、适用范围及文件哈希，详见 [许可来源](licenses/SOURCES.md)。

ModelSensingProtocol 0.1.0-alpha.1：MSP 协议 SDK，MIT 许可。来源 https://github.com/ModelSensingProtocol/msp-dotnet ，NuGet 包声明源码提交 c1055f5edab6da32f6b2539c10b6c3adcc24a211。通过包引用用于后续独立适配，未复制 NotchPeninsula 的 MSP 桥接实现。默认不启动 MSP 节点；用户启用后使用本机共享目录发现与协议通信，主动事件广播另有默认关闭的独立开关。

本项目独立编写，使用 NotchPeninsula 的实际使用体验作为需求参照，未复制其源码、DLL、图片、字体或构建资源。感谢 https://github.com/GEORGEWWWU/NotchPeninsula 提供的体验启发；本项目与该项目无官方关联。

构建使用微软 .NET、WPF、Windows SDK 的公开接口，运行时依赖随自包含发布输出分发。构建从实际运行时包复制 MIT 与第三方声明，从 .NET 安装复制 Windows .NET Library 条款，并附 Windows SDK 条款及 targeting 包原始 RTF；原生组件范围按微软的 [Windows 许可说明](https://github.com/dotnet/core/blob/main/license-information-windows.md) 区分。源码依赖由项目文件及 .NET SDK 定义，不依赖旧程序或临时提取目录。

字体使用系统已安装字体，支持用户选择，不随包分发字体文件。程序与托盘图标为本项目原创几何图形，SVG、PNG、ICO 及生成脚本一并提供。可选图标生成脚本使用 Pillow（仅资产制作，不是构建或运行依赖），Pillow 许可见 https://github.com/python-pillow/Pillow/blob/main/LICENSE 。无外部图标或付费素材。

14 种内置轻提示音由本项目的 NotificationSound.Chime 独立合成 PCM WAV，随项目 MIT 许可；没有打包 NotchPeninsula、Apple 或聊天软件的提示音。运行时使用 Windows 提供的媒体解码器，自选音频由使用者选取并保存在自己的配置目录。

“灵动胶囊”是本项目显示名称，也被其他厂商用于相关功能；不主张对该词独占，与 Apple、Microsoft、荣耀均无隶属或背书关系。

NAudio.Core / NAudio.Wasapi 2.2.1：系统输出音频分析、默认设备及音量控制，MIT 许可。来源：https://github.com/naudio/NAudio 。通过 NuGet 引用原库，未复制 NotchPeninsula 的音频实现。

天气数据按需使用 Open-Meteo（https://open-meteo.com/）及其地名解析接口。天气数据为 CC BY 4.0，许可：https://creativecommons.org/licenses/by/4.0/ ，地名数据来源 GeoNames。界面天气标题提示中保留来源说明；未启用系统定位。公共免费接口用于非商业用途，商业发行需按服务当时的条款配置对应服务方案。
