# 本机外部接入

MSP 默认关闭，开启后以 `lingdongcapsule` 节点提供 `ping`、`notify`、`media_status`、`media_control`。控制只接受播放、暂停、切换及上下首；`prev` 为 `previous` 别名。独立事件广播开关默认关闭，隐私预览会隐藏通知事件中的标题和正文。MSP 通知不会再次广播。

`notify` 的 `title`、`body`、`content` 均可省略，但至少有一项有效内容。`content` 最多 64 项，支持 `text`、`image`、`resource_link`，未知类型跳过。首个文本块在 body 为空时作为正文；标题也为空时用作标题。标题上限 4096 字符，正文上限 16384 字符。订阅 `notify.**` 使用同一解析规则并保持对端身份；遵守来源过滤、安静及隐私设置。

```json
{"title":"任务完成","content":[{"type":"text","text":"可以查看结果"},{"type":"image","mimeType":"image/png","data":"<base64>","annotations":{"role":"avatar"}}]}
```

`annotations.role=avatar`（同义词 head/headimg/headimage/portrait/profile/photo，不区分大小写）优先于 icon，与块顺序无关。没有明确图标时取首个未标记图片；坏头像回退图标、系统应用图标、本程序图标。头像居中裁切为圆形，比图标大 5 个逻辑像素。

`image` 使用 base64；`resource_link.uri` 支持 data 图片、绝对本地路径/file URI、HTTPS。顶层 avatar/avatarUrl/avatar_url/head/headimg 可提供头像，块内优先。本地图片按内容解码，允许无扩展名；拒绝网络共享/UNC、HTTP 和带用户名密码的 URL。当前 HTTPS 不跟随重定向，失败即回退，不使用 Cookie 或应用凭据。读取最多 4 MiB、1600 万像素、3 秒超时，解码宽度 96 像素。通知替换取消旧请求；图片仅在内存，不记录路径或正文。

启用事件广播时，发送已解码缩略图的 PNG 内容块及 avatar/icon 角色，不发送本地路径或图片网址。单张广播最多 256 KiB、65536 像素；隐私预览同时去除文字和图片。图片解析失败仍发送文字，节点重启或广播被关闭后不补发旧请求。

系统应用图标先取 WinRT Logo，失败后后台查询 Shell 图标，最后使用本程序图标。Shell 提取限制两项并发，释放返回的 HBITMAP 和 COM 对象；失败缓存五分钟，避免通知密集时反复提取。图标缓存最多 48 项。实现依据 [Microsoft IShellItemImageFactory 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage)，真实来源覆盖仍待实测。

只启用可信本机节点：图片消息可引发本机文件读取或外部图片请求，只使用消息明确提供的地址，不扫描聊天目录。系统通知头像提取、特殊来源图标回退和真实第三方发送端兼容性仍待完成，MSP 图片接入不代替系统通知头像验收。
