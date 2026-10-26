# 原生预览窗格接入调查

## 目标与现状

补齐 Q-Dir / Explorer 的文件预览能力，同时继续使用每窗格现有的 Windows Shell ExplorerBrowser 文件视图。应用当前以 `EBO_NOBORDER | EBO_NOPERSISTVIEWSTATE` 初始化浏览器，因此 Shell 帧未开启，预览宿主没有可用区域。

## 实现路径选择

- **ExplorerBrowser 原生帧（当前候选）**：启用 `EBO_SHOWFRAMES`，通过 `SID_ExplorerPaneVisibility` 只打开 Preview Pane，并关闭 Shell 自带导航、详情、命令、状态栏等帧。优点是继续调用 Windows 已注册的预览处理器，并由系统预览宿主隔离运行；缺点是帧布局与可见性状态需要与多窗格及应用自己的工具/状态栏协调。
- **应用自建预览处理器宿主**：逐文件解析注册处理器、创建进程外宿主并实现窗口/消息/焦点生命周期。控制力较强，但需重做 Windows 预览处理器框架，增加 COM 生命周期和快捷键路由复杂度。

采用原生帧接入产品，菜单与工具栏均可按当前标签开关，默认关闭；标签状态写入会话 v7。本轮仍未通过可见窗口验证预览内容、动态开关、焦点与布局。Windows 文档说明 ExplorerBrowser 默认关闭帧，`EBO_SHOWFRAMES` 会启用标准窗格；`IExplorerPaneVisibility` 可由宿主服务控制窗格状态。[SetOptions](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowser-setoptions)、[IExplorerPaneVisibility](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerpanevisibility)。预览处理器应通过系统进程外宿主运行。[Preview Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers)

## 隔离宿主试验

- `ShellBrowserSite` 接入 `IExplorerPaneVisibility` 服务；`ExplorerBrowserHost` 可按标签构建时启用或通过生产切换方法运行中开关。
- SettingsSmoke 验证预览窗格开关位及所有其它标准 Shell 帧默认隐藏；HostProbe `--preview-pane` 在四个隔离 ExplorerBrowser 中请求原生预览帧。
- Shell 成功查询 pane visibility 服务及接口；不使用 `EPS_FORCE` 时导航树仍占位，预览处理器区域为 0×0。改用 `EPS_FORCE | EPS_INITIALSTATE` 后导航树隐藏，文件区宽度从 338 px 扩为 221 px（500 px 宿主，四窗格隔离尺寸），Shell Preview Extension Host 仍为 0×0。隐藏宿主选择了 `initial.txt`，父窗口未显示、前台未改变。
- 同一隔离浏览器运行中通过生产 `SetPreviewPaneVisible` 反复开关帧后，`GetOptions` 确认帧位更新，`SHELLDLL_DefView` HWND、当前目录、计数与 `initial.txt` 选区均保持。由此验证了产品所用的切换路径；此结果不说明预览处理器已渲染。
- 这证明原生帧的服务接线和 pane 布局状态被消费，但由于父窗口始终不可见，不能证明 Preview Handler 实际激活或绘制。预览区域在 500×300 四窗格中的空间占比也还未评估可用性。

## Q-Dir 预览格式过滤的实现边界（2026-10-25）

Q-Dir [FAQ 34](https://www.softwareok.com/?faq=34&seite=faq-Q-DIR) 将设置放在 Extras → more options → General，并说用 `*.*` 可查看预览中的所有格式；相邻 [FAQ 17](https://www.softwareok.com/?faq=17&seite=faq-Q-DIR) 说明 PDF 预览依赖 Windows 中安装的预览能力。由这些描述可推断此项是对可进入预览的格式做限制，不是从文件列表中隐藏文件。FAQ 34 的通用说明还提及日期、大小与关键词过滤，但没有说明这些字段如何出现在 General 页的预览过滤设置中；在真实界面确认前，不把这段泛化说明当成已证实的 Q-Dir 设置项。

当前应用的 `IFolderFilterSite` 是文件枚举过滤点；复用它会隐藏列表项目，语义不正确。微软定义的 `IExplorerPaneVisibility::GetPaneState` 只返回 Preview Pane 等整个窗格的可见状态，不能按当前选择文件的扩展名单独禁用预览。保持系统注册的 Preview Handler 与 ExplorerBrowser 原生窗格时，现有接入没有逐文件格式过滤钩子。改由应用自己托管 Preview Handler 则要接管 Shell 的预览处理器生命周期、窗口、焦点与快捷键交互；微软指出处理器通常在进程外系统宿主运行，这一变更扩大兼容和稳定性边界。

因此本轮不把快速筛选当作替代实现，也不随文件选择反复隐藏/显示整个预览窗格。保留差异，待可见观察确认 Q-Dir 的精确交互后，再评估是否值得为此引入自建 Preview Handler 宿主；原生预览功能本身继续按上文计划验收。接口依据：[Preview Handler Architecture](https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers)、[IExplorerPaneVisibility::GetPaneState](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerpanevisibility-getpanestate)、[IExplorerBrowser](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)。

## 后续验收

在用户明确安排的隔离可见窗口时，检查文本、图片、PDF 等已注册处理器是否在 Preview Host 中显示；切换文件、标签和布局；开关预览后检查文件区尺寸、焦点、Tab/F6 与导航状态。产品菜单/工具栏、默认关闭和每标签保存语义已接入，仍待验证它们的可见交互及重启恢复。未通过渲染与布局验收前，不将预览功能标记为完成，也不发布。
