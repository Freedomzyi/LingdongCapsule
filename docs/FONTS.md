# 字体

在个性化中心的同一个字体卡片中选择已安装字体，或使用“选择字体文件…”加载本地 TTF、OTF、TTC。下拉菜单首项“系统默认（恢复默认）”清除两种选择；不会同时启用两个字体设置。

本地文件由 WPF 按 URI 读取，保存在原位置，不复制进安装目录、不安装到 Windows，也不加入源码或发行包。只在个人设置中记忆路径；移动、删除、无权限或损坏时回退系统默认并在字体卡片显示反馈，原选择保留，可重新选择或恢复默认。TTC 使用集合中首个字体家族，缺失字形由 WPF 回退。读取支持本地绝对路径、大小 12 字节至 64 MiB，拒绝网络共享和远程 URL。

选择新字体前验证文件确实能解析到所选文件的字形，避免把 WPF 静默回退误判成成功。保存失败保留原选择与显示；取消选择不保存。字体在岛体、歌词和设置中应用，不改变系统字体。字体文件有各自许可，用户自行选择不构成程序对字体的再分发；含本地路径的设置不应上传公开问题单。

实现依据：[WPF FontFamily 与字体回退](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.fontfamily?view=windowsdesktop-10.0)、[WPF 字体资源与分发边界](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/packaging-fonts-with-applications)。

开发验证使用本机字体的临时测试副本，以及 Adobe 官方 Source Sans 3 的 OTF，仅位于忽略的 artifacts 目录，不进入主包。普通 --font-test 不访问网络；可显式传 --otf-file 检查额外 OTF 文件。专项不替代 Owner 的实际字体选择器与长期使用体验。
