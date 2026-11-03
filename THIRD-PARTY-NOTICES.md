# 来源和依赖

本项目独立编写，使用 NotchPeninsula 的实际使用体验作为需求参照，未复制其源码、DLL、图片、字体或构建资源。感谢 https://github.com/GEORGEWWWU/NotchPeninsula 提供的体验启发；本项目与该项目无官方关联。

构建使用微软 .NET、WPF、Windows SDK 的公开接口，运行时依赖随自包含发布输出分发。保留输出中的微软许可和第三方声明。源码依赖由项目文件及 .NET SDK 定义，不依赖旧程序或临时提取目录。

字体使用系统已安装的 Microsoft YaHei UI，不随包分发字体文件。程序与托盘图标为本项目原创几何图形，SVG、PNG、ICO 及生成脚本一并提供。可选图标生成脚本使用 Pillow（仅资产制作，不是构建或运行依赖），Pillow 许可见 https://github.com/python-pillow/Pillow/blob/main/LICENSE 。无外部图标或付费素材。

“灵动胶囊”是本项目显示名称，也被其他厂商用于相关功能；不主张对该词独占，与 Apple、Microsoft、荣耀均无隶属或背书关系。
