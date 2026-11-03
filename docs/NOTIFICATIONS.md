# 系统通知图片（开发中）

通用设置的 *系统通知头像默认关闭。启用后仍需 Windows 通知权限和系统通知总开关；只对正在接受的系统通知补取图片，插件/MSP 的显式图片继续走各自来源，不需要访问通知存储。

Windows 通知 API 不提供图片元素，本功能使用 Windows 自带 winsqlite3.dll，以只读方式查询当前用户 wpndatabase.db。按已收到的通知 ID、发送应用 AUMID 和五秒到达时间窗口限制单条记录，再核对 XML 标题和全部正文；不按相似标题猜测、不扫描历史或已删除记录、不打开聊天数据库、不复制通知库或保存正文。该库格式不是稳定公共应用协议，锁定、权限不足、表结构变化或不匹配时回退应用图标。

只采用 ToastGeneric 的 appLogoOverride；circle 裁切标为头像，其他裁切标为图标。XML 禁止 DTD 和外部实体，载荷上限 64 KiB，兼容 UTF-8/UTF-16。来源支持绝对本地路径/file URI（包括无扩展名图片）及无凭据 HTTPS，拒绝 UNC/网络共享和 HTTP。图片沿用 4 MiB、1600 万像素、3 秒等待、96 像素缩略图限制，不使用 Cookie、登录凭据或图片磁盘缓存。

后台最多两项存储读取；查询仅开短连接，忙等待 100 ms，SQL 指令进度回调以 200 ms 为中止预算，最多三次落库重试。取消、关闭或更换通知不再贴回旧图；关闭头像开关隐藏已缓存的系统图片。查询结束先释放语句和连接，再解码或请求图片，不在图片网络等待期间占用通知库。

本机仅只读核对过 Notification / NotificationHandler 的表结构，未读取用户通知载荷用于开发验证。专项使用自建数据库、正文和像素，验证只读查询、身份拒绝、编码/DTD、取消与数据库内容不变，并进入已有解码链。真实 QQ 等头像、操作系统权限撤销和新旧 Windows 版本兼容仍待实机验收；不能用合成数据宣称全部系统通知头像均可用。

实现参考：[Windows SQLite 支持](https://blogs.windows.com/windowsdeveloper/2017/02/06/using-sqlite-databases-uwp-apps/)、[SQLite 只读打开](https://www.sqlite.org/c3ref/open.html)、[SQLite 绑定参数](https://www.sqlite.org/c3ref/bind_blob.html)、[SQLite 查询取消](https://www.sqlite.org/c3ref/progress_handler.html)。不分发额外 SQLite 二进制。
