# 下一批集中 UI 验收（2026-10-27）

按用户要求，开发与验证优先后台完成。此清单只安排后续集中可见验收，本次不启动窗口；后台通过不替代以下 UI 结果，也不覆盖日常 temp.76。

## 当前验收基线（2026-10-28，主分支）

当前唯一验收候选为[main 主目录候选](gui-acceptance-2026-10-28-main-prepared.md)：源码提交 `50cb324c9393271785f50b31eb8568f4b6f063d5`，状态 `prepared-not-run`，28 项待验收。下文保留的 2026-10-27 worktree 候选记录仅作历史证据；对应 worktree 已移除，旧候选目录未全部保留，不可再作为测试程序使用。

## 本批实际范围

相对发布源 4dcafc2，产品新增/修复为：原生列标题聚焦（04263d9）、超限会话写入保护（2328266）、标签入口容量检查（084e19f）、查看预设与列宽失效保护（f2d46fb）、普通查看切换与滚轮失效保护（7fc1b66）、延迟焦点绑定原标签（ee6bd8c）。b853f2a 只扩展外部内存采样器，不是产品常驻功能。

后台证据分别在[键盘验收记录](gui-acceptance-2026-10-27-shell-baseline.md)、[路线图](roadmap.md)及[内存采样](memory-sampling.md)。此前指定候选中的局部键盘通过结果保留，新候选仍需代表性回归。产品继续 ExplorerBrowser，不包含 IShellView 宿主迁移。

## 2026-10-28 后台补充：四窗格 F2 编辑器键路由

扩展 Release HostProbe，在 pane 0–3 的隐藏 ExplorerBrowser 宿主中分别切换 `ViewStyle.All` 的 8 个预设，共 32 组：超大/大/中等/小图标、列表、详细信息、平铺、内容。逐组经生产 `TranslateAcceleratorCore` 投递 F2；在原生重命名编辑框内检查 Left/Right 插入点移动、Up/Down 不逃离编辑器、Shift+Left/Right 文本选区、焦点保持以及文件选择集不变。最终源码完整矩阵 32/32 退出码 0；日志在 `%TEMP%\explorer-dirs-rename-editor-view-modes-final-20261028-rerun.log`。pane 3 中等图标首次矩阵有一次 2 秒内未观察到编辑焦点的失败，随后该组合单独复跑 13 次通过，最终完整矩阵复跑也通过；保留这次间歇观测，不据此修改产品。

这是逐窗格、逐预设运行的合成消息/隐藏 Shell 回归，不等于同时编辑多个 pane、物理键盘输入或可见焦点体验。方向键视图矩阵已有后台覆盖，但物理输入和可见模式仍留在集中验收范围。

## 后续候选补充：反向选择的产品 Shell 验收

此项属于当前源码后续候选，不属于上方固定的 `193147/c83af6`；新候选准备完成后再执行。

- 在四窗格分别验证 `编辑 → 反向选择` 与 `Ctrl+Shift+A`：从单选和部分多选分别反选，核对选中项是当前视图的身份补集；再次反选应恢复原选区，焦点项应保持，其他三个窗格选区不变。
- 在八种查看方式中至少覆盖列表、详细信息、图标、平铺和内容等代表方式；另验证空目录、标签切换后命令落到当前标签，以及地址栏/搜索框/重命名编辑框聚焦时快捷键不误操作文件区。
- 菜单点击、物理快捷键、焦点与兄弟窗格隔离分别记录；现有 Shell 选择算法和 NativeProbe 结果不代替产品 ExplorerBrowser 宿主及可见输入验收。

## 第一段：核心键盘与已接受主题回归

1. 四个窗格分别选样本：文件 → Shift+Tab → 列标题 → Right → Tab → 原文件 → Up/Down。核对焦点与选择，Tab/Shift+Tab 和 F6/Shift+F6 分别跨窗格循环；禁用导航按钮不占停靠点。
2. 在 Details、List 和图标方式中执行 F2，方向键及 Shift/Ctrl 文本编辑保持在原生编辑框；先取消，再用可丢弃样本提交一次，核对其他窗格选择没有变化。
3. 同目录及跨窗格 Ctrl+C/V 后，连续方向键必须直接工作，另验证无需 Alt+Tab 才恢复。删除后焦点作为单独项目：工具执行删除前需要当次确认，未取得确认时保留待验收，不影响其他项目。
4. 快速切标签前后关闭地址编辑/搜索、清除筛选：旧延迟请求不移动新标签焦点，同标签正常路径仍能返回文件。时间条件无法稳定控制时记未复现，不用一次普通点击宣称已通过竞态验收。
5. Light/Dark 各做代表性回归：冷启动、复制标签、切窗格、滚动条两端、目录树、文件提示、原生主/子菜单。保持 temp.76 已接受的无白闪、单细框贴合及子菜单系统重叠；文件区不恢复自定义前置命令。

## 第二段：视图状态、QDR 与窗口恢复

1. 不同标签设置不同查看、排序、分组和列宽；复制后改副本，原标签不变。查看预设只改变模式/列，不重置排序或分组。Ctrl+滚轮应保留逐标签状态。
2. 组合快速导航、切标签与查看切换/预设应用。检查新标签不被旧命令修改、没有旧错误提示或多余滚轮动作；这些真实时序结果与后台控制回调测试分别记录。
3. 导出本程序 QDR，另用 Q-Dir 普通界面打开，核对布局、标签顺序/选择、可识别排序/分组/列宽；再由 Q-Dir 保存，导入本程序。只操作隔离配置，不改本机 Q-Dir 原配置。不把本程序读写两端往返当作独立互操作完成。
4. 重启隔离副本核对逐标签视图、布局比例与活动标签；多屏位置、负坐标及混合 DPI 根据可用显示器单独记录。无可用环境的项目保持待验收。

## 最后再做容量边界与资源观察

新夹具 `qdr-tab-capacity-99.qdr` 向最初只有一个标签的 pane-1 追加 99 个隔离路径标签，达到 100。此步骤放在普通内存基线之后；若前面已新建标签，应使用新的隔离 profile，不去覆盖用户正在用的配置。

- 满额时新建、复制、中键和拖动复制/移动到该窗格不能新增；选择和源标签保留，状态提示可读。
- 再导入夹具应在追加前拒绝；关闭历史在恢复被拒绝后保留，腾出位置可恢复。
- 记录版本、PID/启动时间和窗格/已访问标签负载，再采 Private Bytes、GDI/USER 与句柄。100 个未访问宿主与已访问 Shell 视图不是同一负载，不混成内存优化 A/B。

使用准备清单的 `prepared-not-run` 状态起步，按实际行为逐项记录通过、失败、未运行及环境；不将整份清单一次标为通过。

## 已固定副本：准备完成，尚未运行

后续的显式设备路径百分号字面量修复不在这份固定副本中；该项等待下一批候选，不覆盖本目录或将其结果混入本批。

- 目录：`D:\projects\explorer-dirs-worktrees\windows-accent-theme\src\.state\gui-smoke\20261027-141624-675-4ec809`，程序位于其 `app\ExplorerDirs.App.exe`；便携 profile 与用户 releases 分开。
- 干净源码：`359d86cd3085889efc233bd9cfba8601a24c1186`，SourceWorkingTreeDirty=false。
- BuildIdentity / DLL ProductVersion：`gui-acceptance.20261027-141624-675-4ec809+359d86cd3085889efc233bd9cfba8601a24c1186`。
- EXE SHA-256：`B7F43E50AA40431223EBF1C1C5618547F0DB611E50722AA2038CC743A5FBEFD8`。
- DLL SHA-256：`02B2103F3519710B0E21AFFA0542C86D15A70D4E3767A11CECEB136EDD96767B`。
- 99 标签 QDR SHA-256：`3032AAF6EA271573347276C1D99FB11DE7C3DCE4CBA7FA5AA8253350148B7ECF`。

执行 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly -CaptureFocusTrace` 成功。独立复核 manifest 的 EXE/DLL/夹具哈希及版本一致，23 项 PendingAcceptance 均未运行；该 EXE 进程数为 0，focus-trace.log 和 performance.json 不存在。PrepareOnly 在 UIAutomation 装载与启动进程之前返回。

实际 `--inspect-qdr` 加完整 Release SettingsSmoke 通过：新夹具识别 99 标签，窗格计数 99/0/0/0，完整视图快照为 0（夹具只指定路径/查看模式，故意不捏造 Shell 排序/列宽）。此后台检查没有应用导入确认或打开标签，不算容量 UI 验收。日常 temp.76 没有覆盖或重启。

## 固定候选之后：QDR 后台导出批次（另建候选再验）

以下不在已固定的 141624/4ec809 中，不能改写其 23 项准备状态：

- 普通导出/保存工作区、桌面工作区快捷方式、导入前自动备份：含匹配环境变量的百分号目录应保持原身份；无法编码时完整保留标签，普通导出与桌面项给出兼容性确认。
- 后台解析期间关闭窗口，不弹迟到错误或更新关闭窗口；已确认写入的文件生命周期由后台负责，检查实际落盘，不仅看 UI 是否退出。
- 导入备份等待期间改变标签/排序/布局比例或切到其他窗口：保留旧快照备份，停止追加；稳定会话下仍正常导入。备份 I/O 失败必须不追加，精确恢复文件可能已保留。
- 独立 Q-Dir 打开基础 Folder 字段，检查活动标签、排序/分组和四窗格路径。后台本程序消费成功不代替这项互操作验收。
## 新 QDR 批次候选：150314/822ae1（仅准备，未启动）

- 路径：`D:\projects\explorer-dirs-worktrees\windows-accent-theme\src\.state\gui-smoke\20261027-150314-676-822ae1`，程序位于 `app\ExplorerDirs.App.exe`。
- 干净源码：`2ea51a180a10093a332c9e8f22bbe1fc70fa16cd`；BuildIdentity/DLL ProductVersion：`gui-acceptance.20261027-150314-676-822ae1+2ea51a180a10093a332c9e8f22bbe1fc70fa16cd`。
- EXE SHA-256：`5E7E5BAC0EED880D071A3BBE30992425F61546D34920AD821BD4FC2E91719C30`。
- DLL SHA-256：`902204284D8E0F66E3D86F1FC7192650B2ADA78A08702BB899F55930C69960D0`。
- `preparation.json` 为 prepared-not-run、SourceWorkingTreeDirty=false，27 项 PendingAcceptance；新增 QDR 三入口、追加前状态复核、后台关闭生命周期和多窗口恢复回归。原有 141624 候选保留，不将其旧 manifest 改成新批次结果。
- 百分号夹具：`acceptance-files\percent-paths\%ExplorerDirsQdrAcceptance%` 与同级 `expanded-target`。后续仅向被启动的测试进程传入 `ExplorerDirsQdrAcceptance=expanded-target`，通过文件列表/树进入字面量文件夹；不要写用户/系统环境，也不要将地址栏主动展开变量误判为目录选择错误。

执行 PrepareOnly 成功；独立核对源码、ProductVersion、两个哈希与夹具目录通过。该 EXE 对应进程数为 0，focus-trace.log、performance.json 不存在。本次不做可见验收、不更新日常 temp.76。新候选包含最新 QDR 后台导出/桌面项/备份及多窗口版本保护，但不因打包完成宣称 UI 或 Q-Dir 独立互操作通过。

## 当前集中验收候选：193147/c83af6（仅准备，未启动）

- 路径：`D:\projects\explorer-dirs-worktrees\windows-accent-theme\src\.state\gui-smoke\20261027-193147-376-c83af6`，程序位于 `app\ExplorerDirs.App.exe`。
- 干净源码：`dbc3bfbed2541503a155930cf512cd2867774ea1`；BuildIdentity：`gui-acceptance.20261027-193147-376-c83af6+dbc3bfbed2541503a155930cf512cd2867774ea1`；文件版本 `0.6.0.0`。
- EXE SHA-256：`09DDC52C9C249833209942C79E1B2E20DEEA155122B9734614E5FCE26BC1E304`；App DLL SHA-256：`FD362FC209402EA9F1A1094811392AC76BEE30F2FD28A2268277A71115B766CF`。
- `preparation.json` 状态为 `prepared-not-run`，列有 27 项待验收；隔离 profile 在副本 `app\profile\ExplorerDirs.ini`。四窗格样本与 QDR 夹具均位于副本目录。
- PrepareOnly 构建成功；独立复核 EXE/DLL 哈希与 manifest 一致，且该 EXE 进程数为 0。本次没有打开窗口、运行 GUI 验收或覆盖用户 `releases`。
- 目录树图标的隐藏控件接线已有后台证据，图标像素和主题跟随仍在第一段第 5 项可见验收中；其它待验收项按 manifest 逐项记录，不预先标记通过。
