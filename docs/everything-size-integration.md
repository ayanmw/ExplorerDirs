# Everything 文件夹大小数据源

## 接入边界

temp.86 目标为默认显示、可关闭的文件夹大小列，提供 Auto / Everything / 内置扫描三种来源，并复用到独立空间分析。当前本文件记录数据源阶段，不表示列表或设置已接入。

EverythingSizeClient 使用官方 SDK 文档公开的 IPC QUERY2W 协议，批量查询指定目录直接子项，不打开 Everything 搜索窗口、不更改外部配置。没有导入 Everything 程序，也没有新增 SDK DLL 运行依赖。

官方资料：https://www.voidtools.com/support/everything/sdk/ 及 https://www.voidtools.com/support/everything/sdk/ipc/ 。2026-11-02 下载官方 Everything-SDK.zip 用于接口核对，SHA256 F5716D9513CCE6B462B5170A0A2E7E081E191D9BCAC6774F5DECC061497DF443；SDK C 源和公开头部附 MIT 许可。下载材料仅在 src/.state/everything-sdk 调试目录，不随产品打包。IPC 字段偏移依据该包 ipc/everything_ipc.h。

## 已实现合同

- 查询前检查服务窗口、索引就绪和目录大小索引能力；无运行实例、索引不可用、超时、错误报文分别返回状态。
- 只创建 HWND_MESSAGE 接收窗口；所有工作在后台 STA，最多两条并行查询。发送使用 SendMessageTimeout，回包等待 1.5 秒，等待期间检查取消；排队取消不创建窗口。
- 每页最多 2048 项，返回总数与 offset 以支持后续分页；请求完整路径和逻辑大小，负值保留为未知，不冒充 0。
- 回包检查大小上限、计数、偏移、字段、UTF-16、终止符及路径；查询结果再校验每项确为请求目录的直接子项。

## 当前证据

- 本机 Everything 1.4.1.1032 正在运行。只读查询项目目录：Available，13 行，13 项大小可用，约 430 ms；前台不变，外部配置不变。
- --everything-size-protocol-smoke：中文路径、0 与未知值区分、报文截断/坏偏移/计数/字段、预取消通过，无窗口/IPC。
- --everything-size-live-fixture：新建两个临时目录，分别含 5 B / 37 B 文件，真实 Everything 查询读回两目录正确汇总，约 734 ms；前台不变，夹具清理成功。
- --navigation-chrome-smoke：修正旧检查对电脑菜单构造方式的假设后通过图标/可访问名称与焦点遍历合同。未进行可见 UI 验收。

## 待完成

大小来源 INI 与视图开关、数据源选择与自动回退、有限缓存/失效、分页、普通列表列接入和空间分析尚未完成。尚未覆盖真实 Everything 未运行/关闭目录索引、查询中服务退出、挂起服务和多页目录；当前测试不能替代这些运行期边界。
