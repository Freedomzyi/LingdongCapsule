# 材质实现与验收边界

当前为开发阶段。`Capsule.Core/GlassMaterial.cs` 是独立编写的 CPU 像素合成器，尚未接入实际岛体；目前用户可用材质仍是实体背景。未接入前不在设置中提供空功能开关。

输入为有边距的 BGRA 背景，输出为岛体尺寸的 BGRA 图像。运行和测试不将真实桌面画面写盘。合成器支持模糊、边缘折射、RGB 分离色散、高光、折射带厚度、透镜、饱和度、雾度、明度与倍率，另外定义抓取间隔和帧率的五档映射；后两项须由宿主调度执行，不能仅凭数值表宣称已生效。另有亚克力模糊与主题染色路径。

模糊采用两次运行和滤波，半径变化不增加每像素的循环长度；折射根据圆角矩形距离与边缘法线计算，并做双线性采样。模糊半径和折射距离按实际像素/逻辑像素比例换算。CPU 负载、质量档与完整 DPI 显示仍须在接入真实背景后测量，不能用合成测试证明低占用。

核心测试检查各图像参数实际改变非对称背景、源像素不被改写、输出长度/透明度、常量图像模糊能量保持、时序映射和无效尺寸拒绝。Windows `--material-test` 将人工生成的图案合成后编码为 PNG；这些不是实际桌面材质截图。

## 背景抓取决策证据

2026-11-03 本机实测：专用红色背景窗口前放置绿色 WPF 透明分层窗口，GDI `SRCCOPY` 即使没有 `CAPTUREBLT` 仍返回绿色。不能假设省略 `CAPTUREBLT` 就会排除自身；该路径没有接入应用，避免递归反馈。

参照版使用的 Magnification 回调被微软标记为弃用，且其文档对 DWM 支持有限制，因此不直接继承为新程序唯一抓取实现。下一步优先验证 Windows Graphics Capture 的监视器抓取、自身排除与截图显示设置的兼容，保留完整失败反馈和资源清理。

API 依据：[MagSetImageScalingCallback](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-magsetimagescalingcallback)、[MAGIMAGEHEADER](https://learn.microsoft.com/en-us/windows/win32/api/magnification/ns-magnification-magimageheader)、[窗口排除列表](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-magsetwindowfilterlist)。这些资料证明接口限制，不证明本机抓取后端已经完成。
