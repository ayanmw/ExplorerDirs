# QDR 后台兼容基线（2026-10-27）

按用户新要求，本轮未启动/控制窗口。使用现有 Release SettingsSmoke `--roundtrip-qdr`，只读加载本机三份 Q-Dir 配置，在内存中导出并重新解析基础字段和 Explorer Dirs 扩展。没有复制原始配置到仓库，没有修改原配置。

| 样本 | 标签总数 | 各窗格标签 | 原始 ColsAndSort 字段 / 导入完整视图 | 结果 |
|---|---:|---|---|---|
| start.qdr | 43 | 12 / 12 / 10 / 9 | 43 / 43 | 基础字段及扩展往返通过 |
| start2.qdr | 44 | 13 / 12 / 10 / 9 | 40 / 40 | 基础字段及扩展往返通过 |
| server_common_tools_server.qdr | 28 | 12 / 3 / 9 / 4 | 24 / 24 | 基础字段及扩展往返通过 |

三份均为四宫格布局。全部现有 ColsAndSort 字段为 CODEMODE1、772 字节，均成功解析；后两份各有四个标签没有源视图字段，不是解析器拒绝已存在的数据。缺少字段的标签仍按现有默认/显式 ViewMode 规则处理，不虚构排序、分组及列宽。

## 证据与边界

运行命令：`dotnet src/ExplorerDirs.SettingsSmoke/bin/Release/net10.0-windows/ExplorerDirs.SettingsSmoke.dll --roundtrip-qdr <本机样本路径>`。样本分别位于 `C:\SoftwareOK\Q-Dir` 和其 `Favoriten\Quick-Link` 子目录。执行两轮；最后一轮运行前后 SHA-256 一致：

- start.qdr：`1A2B9E7BC400D38A583347F1B28317B6952E8AD8FF1DE620A4ECAD6EB4BD5DE6`
- start2.qdr：`744AD5062FA452E46684EA9C523BED865BA6D2DAD5A8F1DA211330C30A8E4BD6`
- server_common_tools_server.qdr：`BB2E769AAECD569B9BE7AD41D419A045BCA1652EB48861CCC72A038D15FD74B6`

该命令校验本程序导入、导出、重新导入的布局、窗格/标签数量、活动标签/窗格、路径、查看模式、树开关及完整视图中的图标大小、排序、分组和列设置；扩展 SessionStore 状态逐字往返一致。使用的代码仍是本程序读写两端，因此这是内部兼容基线，不是 Q-Dir 对本程序导出文件的独立确认，也不覆盖实际 Shell 视图恢复、UI 导入追加或重启后的可见状态。

本轮无证据支持修改格式映射，未改产品代码或发布包。后续后台优先核对独立标签快照/持久化及异常路径；集中 UI 批次再覆盖 Q-Dir 打开导出文件、相同布局/比例读回，以及本程序导入后标签视图和重启恢复。

## 百分号目录与编码 PIDL 的身份边界

后台原生路径解析回归新增两个实际临时目录：一个名称含 `%唯一变量名%`，另一个对应变量展开后的目标。变量仅写入测试进程并在 finally 移除，PIDL 指针在 finally 释放；不创建 Shell view/HWND。

- `Dir1` 文本路径按现有规则展开变量，实际得到 replacement 目录。
- `Folder1` 编码 PIDL 从真实百分号目录创建，再通过实际 QdrImporter.ResolveLocation 导入，仍得到百分号目录，未被同名变量改写。完整 Release SettingsSmoke 通过，日志 `src/.state/qdr-percent-pidl-green.log`。
- 曾尝试以显式设备路径验证字面量，但旧实现及跳过变量展开的临时试改都返回“未找到可导入的文件夹”；试改已撤回。失败日志 `src/.state/qdr-device-literal-{red,trial}.log` 保留。该结果只说明本机该样本当前不被导入链路接受，不推广为所有设备/长路径均不支持，也不把地址栏纯字符串回归当作 Shell 实际支持证明。

发现的互操作限制：当前导出器以 `DirN` 文本路径作为基础字段，百分号目录若恰好命中接收进程环境变量，文本解析有歧义。本程序扩展可保留原会话，但尚无 Q-Dir 独立读取证据；不能据此宣称这种目录的基础字段跨程序身份无损。若后续补充编码 PIDL 导出，应评审离线/网络路径的 Shell 解析成本及基础哈希，不能在菜单构造或 UI 线程同步探测。本轮只补真实身份回归和限制记录，未改变产品导出格式或发布版。