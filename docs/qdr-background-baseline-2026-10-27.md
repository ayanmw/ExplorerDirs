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
