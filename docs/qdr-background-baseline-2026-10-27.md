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

## 2026-11-01 真实 Q-Dir 原生列键只读核验

NativeProbe 的 `--inspect-qdr-viewstate <path>` 直接调用生产 `QdrImporter.Load`，只输出计数与 PROPERTYKEY，不输出标签路径。对本机当前四份文件的结果如下：

| 文件 | 标签/窗格 | 有视图快照 | 排序键摘要 |
|---|---|---:|---|
| `C:\SoftwareOK\Q-Dir\start.qdr` | 42（12/12/10/8） | 42 | Shell key `B725F130-47EF-101A-A5F1-02608C9EEBAC:10` ×34；`:14` ×8 |
| `C:\SoftwareOK\Q-Dir\start2.qdr` | 44（13/12/10/9） | 40 | `:10` ×32；`:14` ×8 |
| `C:\Users\marvin\Documents\default.qdr` | 43（12/12/10/9） | 39 | `:10` ×31；`:14` ×8 |
| `C:\SoftwareOK\Q-Dir\Favoriten\Quick-Link\server_common_tools_server.qdr` | 28（12/3/9/4） | 24 | `:10` ×17；`:14` ×7 |

四份均成功解析；已捕获的列布局以 `{B725F130-47EF-101A-A5F1-02608C9EEBAC}:10/14/4/12` 为主，排序键主要是 `:10` 与 `:14`。本机 Q-Dir 样本没有原型使用的自定义递归数据键 `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1`，也没有其排序键 `{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1`；这与递归大小是 Explorer Dirs 扩展列的定位一致，不能把它们误认成 Q-Dir 原生列。`start2.qdr` 还包含两个 Shell known-folder 格式的列键，每个出现两次；其具体标题未在本次日志中解析。

本次仅读取文件内容及哈希/元数据，没有修改或导入用户会话。当前 `start.qdr` SHA-256 为 `A4B12E81EA8F4273DBA5C52A2D114D2F3DEB04858B3C3D57B5E390873C48D15B`，与 2026-10-27 基线记录的 hash、标签数不同，说明该文件自旧基线后已变化；旧基线原值保留，不覆盖。此处只验证 importer 的解析与键分布，不证明 Shell 对这些真实标签视图的可见恢复，也不等于 Q-Dir 消费本程序导出文件。

随后以本机 `start.qdr` 运行 `--qdr-imported-view-state-restore-private-desktop`：选取第一个有列快照的已导入标签，把生产 importer 返回的 4 列、列宽、名称升序键与分组状态原样交给生产 `ShellViewStateRestore`，在隔离 synthetic `IShellView` 中应用。Shell 读回名称/日期/类型/大小四列与名称升序键，行视图保留两个合成条目；探针 exit 0，前台/输入/隔离清理守卫通过。NativeProbe Release build 0 警告/错误。

这证明真实 Q-Dir 文件的一个标准文件视图快照可被生产恢复器和原生 Shell 接受；底层 folder 是合成 namespace，数据行不是 Q-Dir 标签路径，未创建/显示真实 Q-Dir 标签视图，也没有检查同一文件中其余 41 个标签逐个恢复、分组读回、模式切换或特殊 known-folder 列。产品 ExplorerBrowser、跨窗格可见行为及 Q-Dir 对本程序导出文件的消费仍未验证。

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
### 导入前自动备份：后台准备与追加前复核

第三个入口已接入：SaveBackup 在静态状态工作线程先写精确 SessionStore 恢复文件，再 Prepare 编码身份并写 QDR。后续 Shell/QDR 失败仍保留已写入的精确恢复文件；只有两个步骤成功才返回允许进入追加阶段。无法编码的百分号路径保持文本及扩展，导入完成状态提示回退数量，不为可选互操作字段丢弃会话备份。

UI 在发出工作前保存纯 Render 的完整快照基线；完成后重新捕获同一组 Session/Pins/Proportions/Widths，逐字比较规范输出。窗口关闭不继续；隐藏/非活动窗口保留备份但不追加；内容变化也保留备份并提示重新导入，避免用旧备份保护新会话。此比较涵盖原有容量检查所用的标签数量；检查之后到原有追加循环之间没有新增 await。原有追加循环的部分失败提示仍保留，不声称整个导入已具备回滚原子性。

真实后台测试覆盖精确会话/QDR 配对、比例/树宽保留、百分号目录在无本程序扩展时的原生身份恢复，以及故意使 QDR 目标成为目录时的写入失败；失败保留精确恢复文件且向上报告。完整 Release SettingsSmoke 与 App Release build 通过，零警告错误。日志 src/.state/qdr-backup-{settings,build}.log。未创建窗口或读写用户配置。

至此普通导出、桌面工作区、导入前备份三个产品入口均已接线。后台等待期间实际改变状态、切换/关闭窗口、原生 Q-Dir 独立读取及用户可见进度尚未验收；固定 141624 候选与 temp.76 均未覆盖。后续集中候选需包含这些新提交，不能用旧候选验收本批。
### 桌面工作区的已确认替换

复核发现原保存对话框允许确认覆盖 .lnk，但底层 File.Move 从不覆盖，导致实际操作失败。新增回归先复现该失败，再增加可选 expectedShortcut 字节快照；默认 null 仍拒绝覆盖现存快捷方式。桌面入口改为后台读取当前字节后在活动窗口显式确认，快照随状态传给发布 worker，替代文件对话框内无法绑定内容的覆盖确认。

发布复用按目标路径命名的互斥锁，取得锁后比较当前文件与确认字节，再移动临时 payload；不匹配（包括新出现或被删除）拒绝覆盖。该锁协调本程序写入者，不声称能锁住所有不遵守协议的外部修改。替换成功不删除旧工作区 QDR；发布冲突时只清理本次尚未发布的新 QDR，临时 .lnk 也在 finally 清理。

真实后台测试覆盖：默认拒绝覆盖、明确旧字节后替换成功、确认后外部改字节被保留、拒绝时新 QDR 清理、旧 QDR 保留及无临时 .lnk 残留。完整 SettingsSmoke 通过，App Release build 零警告错误；日志 src/.state/desktop-replace-{red,green,build}.log。未启动窗口或快捷方式。

本修复晚于固定 150314/822ae1 候选，不改写其 27 项 manifest。后续 UI 验收需另用包含本提交的构建，检查替换确认/取消和目标冲突提示；此前候选仍不能证明本修复的可见行为。发布包不变。
