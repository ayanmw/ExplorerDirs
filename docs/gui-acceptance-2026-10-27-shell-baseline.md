# 2026-10-27 Shell 键盘验收补充

本轮在已落地主题修复后继续 P0 验收，产品架构仍为 ExplorerBrowser，未修改键盘路由或发布包。

## 固定夹具

- 候选：`src/.state/gui-smoke/20261027-125449-909-179260/app/ExplorerDirs.App.exe`
- 干净源码：`1770a00b161c6a485783576e57d58d272695ed03`
- EXE SHA256：`8D317F102002792EB0D06A640EECF9CDBC826A4DFBAAD56FFD596FBBBC1D6743`
- DLL SHA256：`AC1FC821A54F80367CB8B7055F9B0462EEA22A98BEEF813629BE3B095544436E`
- 独立四窗格 Details 夹具；原 preparation.json 保留准备时状态，另写 acceptance-results.json 记录本次实际运行，不将全部清单改成已通过。

## 结果

| 项目 | 本轮证据 | 结论 |
|---|---|---|
| F2 进入重命名及方向键保持编辑 | 用户在指定隔离窗口手动确认“能进入，方向键保持编辑” | 当前样本可见通过；不外推每窗格、八种视图、全部键或提交/取消矩阵 |
| 自动 F2 尝试 | 真实鼠标选中文件后工具 F2 未取得编辑框；之后观测活动 pane 已变化 | 并发输入与焦点影响未排除，不记录为产品缺陷，不据此改代码 |
| F6 / Shift+F6 | 开始续测时窗口绑定失败；重新 list_windows 证实隔离进程窗口已不在列表 | 未运行；不对用户日常 release 继续注入输入，不重复启动抢占桌面 |
| Ctrl+C/V、Shift+Delete 后方向键 | 未使用剪贴板、未删除文件 | 未运行，仍为 P0 可见验收项 |

前台操作未输入新文件名，未提交重命名；未执行复制、删除或真实文件修改。该结论只描述本任务发出的操作，不推断用户并发动作。

## 后续顺序

1. 保持同一夹具身份，继续正反 Tab 与 F6 的可逆顺序，再检查原生列标题/选中文件焦点。
2. 桌面条件合适时完成文件操作后的键盘焦点；删除行为按 Computer Use 确认策略另行取得当次确认。
3. 基础交互验收后，再推进筛选分组、QDR 真实互操作、多屏窗口恢复和隔离 IShellView 迁移门槛。

总体 Q-Dir 追赶目标仍开放；本轮只新增了具有明确范围的用户可见证据，没有将单个样本标为全部 P0 完成。

## 后续实测：正反焦点切换

复用同一 125449/179260 夹具，先确认 `1770a00..HEAD` 的 App 源码无差异；启动后返回窗口 ID 15146116。以下为第一窗格 Details 模式的真实工具输入与逐步截图观察，不外推其余窗格或所有模式。

- 鼠标选中 sample-1.txt，Tab 后状态栏查看按钮出现键盘焦点框；Shift+Tab 返回该文件的焦点虚线。随后 Down 将选择移到 sample-2.txt，证明返回后方向键可操作文件。
- F6 后状态栏查看按钮出现焦点框；Shift+F6 返回 sample-2.txt，焦点虚线和原选择保留。
- **列标题停靠未通过**：从 sample-2.txt 再按 Shift+Tab，实际到达 pane-1 标签标题，跳过列标题；Tab 返回 sample-2.txt。源码已有 `FindColumnHeader` / `FocusColumnHeader` UIA 路径，但本轮未区分查询未找到、SetFocus 失败或后续焦点转移，不能据此归因。此项保持 P0 开放，下一步需定位原生 Header 可聚焦性与宿主焦点判定。

UIA focused_element 有缺失或与截图焦点框不同步，以上结论以逐步可见焦点框、选择状态和 Down 行为为依据。本轮未操作剪贴板、删除或提交重命名。Alt+F4 关闭隔离窗口后再次枚举确认该窗口已退出，日常 release 未改。

## 列标题跳过：原生控件诊断

使用两份仅含临时日志的隔离构建重复 Shift+Tab，仍从 sample-1.txt 跳到标签标题。第二份 `src/.state/gui-smoke/20261027-131400-261-853c3d/app/column-header-diagnostic.log` 给出明确失败边界：

```text
header-find ... class=SysListView32 root=ControlType.DataGrid group=ControlType.Header
header-setfocus before=SysListView32 target=ControlType.SplitButton name=名称 focusable=False
header-error System.InvalidOperationException: 目标元素无法接收焦点。
   at System.Windows.Automation.AutomationElement.SetFocus()
   at ExplorerDirs.App.ExplorerBrowserHost.FocusColumnHeader()
```

因此本次不是未找到 Header，也不是 SetFocus 成功后被抢走：现有 UIA SetFocus 直接失败，FocusStop 返回 false 后遍历跳到标签标题。外部 UIA 快照同样能列出 Header 和“名称”等 SplitButton，但“存在”不表示“可通过 UIA 聚焦”。

下一步验证原生 Header HWND 的焦点与 `HDM_SETFOCUSEDITEM`，保留 DirectUI 路径及原文件选择。官方接口依据：[HDM_SETFOCUSEDITEM](https://learn.microsoft.com/en-us/windows/win32/controls/hdm-setfocuseditem)、[HDM_GETFOCUSEDITEM](https://learn.microsoft.com/en-us/windows/win32/controls/hdm-getfocuseditem)。这些文档只证明接口契约，不证明当前 Shell 控件已接受修复。

日志构建的源码明确为临时诊断脏构建；取证后全部诊断代码撤回，App 源码与提交基线一致，未更新发布包。两份窗口均已关闭，后续窗口枚举确认均已退出。

## 原生 Header 聚焦修复与局部验收

产品为可见的 `SysListView32` Header 新增原生聚焦路径：通过 `LVM_GETHEADER` 取得 `SysHeader32`，保留 `HDM_GETFOCUSEDITEM` 索引，SetFocus 后用 `HDM_SETFOCUSEDITEM` 进入该列，并检查原生焦点。HasColumnHeader / IsColumnHeaderFocused 同时识别这个原生停靠点；其他 Shell 控件继续用原 UIA 路径。没有改变文件选择、排序或保存格式。

新增 HostProbe `--column-header-focus`：缺少原生路径时先失败，接入后验证原生 Header 聚焦、Right 移动列焦点、再次进入保留列索引、空句柄拒绝及返回原 ListView。测试显式激活版本 6 common-controls；未激活时系统创建旧版 Header，SetFocus 可到 HWND，但不支持该项消息，不应作为 Shell 接口可用性结论。增加 Right 检查后曾发现隐藏父窗被 Header 激活，前台不变断言正确失败；父窗加 WS_EX_NOACTIVATE 后最终通过，断言保留。此探针没有 Shell view 或桌面输入。

隔离产品候选 `20261027-131931-834-a5f9ac`（源码基线 0d6a741 加本批未提交修复）中，第一窗格选中 sample-1.txt 后 Shift+Tab 成功高亮“名称”标题，Right 成功将高亮移到“修改日期”，原 sample-1.txt 保持选择。随后 Tab 操作被工具报告并发输入，重新观察时窗格/选择已变；这段不记通过。续测前枚举确认候选已退出，不对日常 release 注入输入。因此产品中列标题进入与 Right 有可见证据，返回原文件及完整跨窗格正反顺序仍待后续验收。

App publish、完整 Release SettingsSmoke 和最终原生 Header probe 通过。日常 temp.76 未覆盖；本批修复与主题发布分开。

## 续测：列标题返回与同目录粘贴后方向键

重新启动同一 131931/a5f9ac 候选（窗口 34412232），在第二窗格 Details 中逐步操作：

1. 点击 sample-1.txt；Shift+Tab 进入“名称”列标题；Tab 返回 sample-1.txt 的焦点虚线；Down 将选中项移动到 sample-2.txt。列标题返回后文件键盘操作通过。
2. 对当前 sample-2.txt 发出 Ctrl+C，再 Ctrl+V。列表从 6 项变成 7 项，新 sample-2 - 副本.txt 被选中且有焦点虚线。
3. 不点击、不切换窗口，下一次输入为 Up，选择和焦点移至相邻 sample-5.txt。此次同目录粘贴后无需重新获取焦点；工具调用间包含截图观测，未验证低于截图延时的连续高速按键。

磁盘复核原文件和副本均为 27 字节，保留副本，不执行撤销或删除。此次 Ctrl+C 使用了系统剪贴板，内容为隔离样本文件。没有修改真实用户文件。Alt+F4 后枚举确认候选已退出，日常 temp.76 未改。

范围仍限第二窗格 Details 和本次小文件同目录复制。Shift+Delete 确认后、跨窗格粘贴、大批量/慢 IO、全视图模式和跨窗格完整遍历仍未验收，不能据此关闭全部文件操作焦点 P0。
