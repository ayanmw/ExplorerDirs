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
### 编码身份导出：纯渲染阶段

QdrExporter.Render 增加可选的已解析路径→编码 PIDL 映射；有映射时写 FolderN/FolderN_index，否则沿用 DirN。写前用既有 DecodePidl 校验结构，不调用 Shell、不探测磁盘，基础哈希已包含 Folder 字段。映射应由调用方从同一快照取得，不能用另一目录的 PIDL 冒充该路径。

新增真实后台回归将同一百分号目录编码写入四窗格；删除全部 ExplorerDirs 扩展字段后，再通过实际 Shell 解析导入，四个标签仍指向原目录。带扩展时校验通过并恢复完整状态；非法编码拒绝输出。完整 SettingsSmoke 与 App Release build 通过，构建零警告错误，日志 src/.state/qdr-encoded-export-{green,build}.log。无窗口、无 Q-Dir UI；不是独立 Q-Dir 读取验收。

当前产品导出调用尚未传入映射，故上一节记录的日常导出限制仍存在。源码确认普通导出、工作区快捷方式和导入前备份的 Render 均发生在 UI 线程，只有部分写入在后台；下一阶段必须在状态快照之后通过静态 state-only 后台任务取得编码身份，处理离线/解析失败的可见提示，再接入三个入口。不能直接在纯 Render 内加入 Shell 调用，也不能让某个离线标签静默丢失或阻断原会话备份。固定 UI 候选及发布包不变。
### 普通导出入口：后台准备已接入

QdrExporter.Prepare 对快照中含 `%` 的不同路径逐一取得真实 folder PIDL（不展开环境变量），用 Folder 字段输出；不含百分号的路径不解析 Shell，普通离线/网络路径保持原导出。所有 PIDL 在 finally 释放。同一路径多标签只解析一次；无法取得 PIDL 时保留 Dir 字段及完整扩展，返回不同路径的失败数量，不丢标签。

ExportQdrAsync 已通过 WindowBackgroundOperation 的静态 state-only 委托调用 Prepare，UI 捕获 Session/Pins/Proportions/Widths 后不把窗口传给工作线程。关闭窗口取消 UI 等待；Shell 解析本身不保证立即停止，沿用现有迟到异常观察规则。存在回退路径时，在写入前给出兼容性确认；非活动窗口不弹出该确认、不继续写入。解析成功则使用既有确认后的冲突保护写入。

实际 ThreadPool 后台准备回归：真实百分号目录在四窗格基础字段中保持身份，无扩展也能正确导入；不存在的百分号目录报告一个不同失败路径，四个标签和扩展全部保留。普通离线/网络假路径无 `%` 的输出与原 Render 逐字相同。完整 Release SettingsSmoke 与 App Release build 通过，零警告错误；日志 src/.state/qdr-prepare-{green,final,build}.log。

范围限制：普通“导出 .qdr”及其保存工作区入口已接入；“保存到桌面”的快捷方式生成及导入前自动备份仍使用原纯 Render。后两处需要各自的后台/失败语义，不将普通导出完成当作三个入口全部完成。UI 进度、失败确认、关闭时行为和 Q-Dir 独立读取仍待集中验收。本修复不包含在固定 141624 候选或 temp.76 中。
### 桌面工作区入口：后台准备及写入生命周期

保存桌面工作区现在使用与普通导出相同的静态状态快照后台 Prepare 和兼容性确认；准备与文件发布期间共用导出 busy gate，不把窗口或对话框传入工作线程。只有确认后的路径/应用参数/文本进入静态后台委托。

DesktopWorkspaceShortcut.CreateWithWorkspace 在同一个工作线程保存新的 QDR 并创建 .lnk；后一步失败时由该工作线程删除它刚写入的 QDR。QDR 保存冲突发生在清理 try 之前，不会删除已存在的配置。UI 关闭仅取消等待、不清理后台仍在写的文件，迟到成功/失败不更新关闭窗口，沿用 WindowBackgroundOperation 的异常观察。快捷方式发布仍沿用既有不覆盖现存 .lnk 的行为；本轮不改变覆盖语义。

真实 ThreadPool 回归创建并读回 QDR/.lnk 配对；目标 .lnk 已存在时发布失败，原快捷方式字节保留，新 QDR 清理；目标 QDR 已存在时保留其内容且不发布新 .lnk。完整 Release SettingsSmoke 通过，App Release build 零警告错误，日志 src/.state/qdr-desktop-{worker,final,build}.log。测试只操作临时目录、读取快捷方式目标，不启动快捷方式或应用窗口。

剩余入口是导入前自动备份；仍需处理等待期间会话变化及备份之后的追加原子边界。普通导出/桌面项的实际确认、关闭时 UI 行为及 Q-Dir 读取仍待集中验收。固定 UI 候选和 temp.76 不变。