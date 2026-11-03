# 本机外部接入

MSP 默认关闭，开启后以 `lingdongcapsule` 节点提供 `ping`、`notify`、`media_status`、`media_control`。控制只接受播放、暂停、切换及上下首；`prev` 为 `previous` 别名。独立事件广播开关默认关闭，隐私预览会隐藏通知事件中的标题和正文。MSP 通知不会再次广播。

`notify` 的 `title`、`body`、`content` 均可省略，但至少有一项有效内容。`content` 最多 64 项，支持 `text`、`image`、`resource_link`，未知类型跳过。首个文本块在 body 为空时作为正文；标题也为空时用作标题。标题上限 4096 字符，正文上限 16384 字符。订阅 `notify.**` 使用同一解析规则并保持对端身份；遵守来源过滤、安静及隐私设置。

```json
{"title":"任务完成","content":[{"type":"text","text":"可以查看结果"},{"type":"image","mimeType":"image/png","data":"<base64>","annotations":{"role":"avatar"}}]}
```

`annotations.role=avatar`（同义词 head/headimg/headimage/portrait/profile/photo，不区分大小写）优先于 icon，与块顺序无关。没有明确图标时取首个未标记图片；坏头像回退图标、系统应用图标、本程序图标。头像居中裁切为圆形，比图标大 5 个逻辑像素。

`image` 使用 base64；`resource_link.uri` 支持 data 图片、绝对本地路径/file URI、HTTPS。顶层 avatar/avatarUrl/avatar_url/head/headimg 可提供头像，块内优先。本地图片按内容解码，允许无扩展名；拒绝网络共享/UNC、HTTP 和带用户名密码的 URL。当前 HTTPS 不跟随重定向，失败即回退，不使用 Cookie 或应用凭据。读取最多 4 MiB、1600 万像素、3 秒超时，解码宽度 96 像素。通知替换取消旧请求；图片仅在内存，不记录路径或正文。

只启用可信本机节点：图片消息可引发本机文件读取或外部图片请求，只使用消息明确提供的地址，不扫描聊天目录。系统通知头像、多级系统图标回退及图片事件广播仍待完成，MSP 图片接入不代替它们的验收。
