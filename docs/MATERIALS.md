# 材质实现与验收边界

当前为开发阶段。`Capsule.Core/GlassMaterial.cs` 是独立编写的 CPU 像素合成器，已接入实际岛体。设置可选择实体、亚克力和液态玻璃、三档质量与十二项参数；默认仍为实体背景。

输入为有边距的 BGRA 背景，输出为岛体尺寸的 BGRA 图像。运行和测试不将真实桌面画面写盘。合成器支持模糊、边缘折射、RGB 分离色散、高光、折射带厚度、透镜、饱和度、雾度、明度与倍率，另外定义抓取间隔和帧率的五档映射；抓取间隔已用于 UI 线程抓取计时，帧率上限用于歌词、频谱与进度线的逐帧重绘；WPF 收放和淡出动画同时设置目标帧率，设置变更从下一次动画生效；实际呈现仍受系统合成与显示器刷新率限制。另有亚克力模糊与主题染色路径。

模糊采用两次运行和滤波，半径变化不增加每像素的循环长度；折射根据圆角矩形距离与边缘法线计算，并做双线性采样。模糊半径和折射距离按实际像素/逻辑像素比例换算。三档质量按 0.5 / 0.75 / 1 倍像素尺寸在后台合成；真实 CPU 负载与完整 DPI 显示仍须测量，不能用合成测试证明低占用。

核心测试检查各图像参数实际改变非对称背景、源像素不被改写、输出长度/透明度、常量图像模糊能量保持、时序映射和无效尺寸拒绝。Windows `--material-test` 将人工生成的图案合成后编码为 PNG；这些不是实际桌面材质截图。

## 背景抓取决策证据

2026-11-03 本机实测：专用红色背景窗口前放置绿色 WPF 透明分层窗口，GDI `SRCCOPY` 即使没有 `CAPTUREBLT` 仍返回绿色。不能假设省略 `CAPTUREBLT` 就会排除自身；该路径没有接入应用，避免递归反馈。

参照版使用的 Magnification 回调被微软标记为弃用，且其文档对 DWM 支持有限制，因此不直接继承为新程序唯一抓取实现。核对 Windows Graphics Capture 后，未找到独立于全局截图排除的每次抓取窗口排除能力；本机能力测试证明 Magnification 排除列表可以取得绿色分层窗口下面的红色窗口，且不改变显示亲和性。因此将其作为可检测、可失败回退的兼容后端接入，并明确保留弃用风险；不可用时回退实体，不使用会反馈自身的 GDI。现代替代后端仍待后续兼容性验证。

API 依据：[MagSetImageScalingCallback](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-magsetimagescalingcallback)、[MAGIMAGEHEADER](https://learn.microsoft.com/en-us/windows/win32/api/magnification/ns-magnification-magimageheader)、[窗口排除列表](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-magsetwindowfilterlist)。这些资料证明接口限制，不证明本机抓取后端已经完成。

## 实机接入回归

- `--material-test` 已验证实际主窗口产生玻璃与亚克力背景，像素来自专用红色背景窗口；测试窗口移离用户正在运行的 NotchPeninsula，避免它真实遮挡背景导致错误断言。
- 捕获矩形先与虚拟桌面求交，再对缺失边距复制边缘像素。开发中曾在顶部负边距触发原生访问异常；修正后默认顶端启动及隐藏/重建回归通过，仍需混合屏幕和睡眠压力验证。
- 隐藏时停止抓取、释放后台窗口与画面引用；旧后台合成通过代次检查拒绝回写。恢复后重新取得画面；切回实体释放材质并恢复实体画刷。失败退避五秒，不记录真实桌面图像。
- 实际岛体 WPF 渲染证据位于私有 `artifacts/verification/material/actual-island-glass.png`。还未完成全质量/参数的 Owner 视觉对齐、长期占用和所有显示驱动兼容性验收。
