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

## Q-Dir 预览格式过滤的实现边界（2026-10-28 复核）

Q-Dir [FAQ 34](https://www.softwareok.com/?faq=34&seite=faq-Q-DIR) 将设置放在 Extras → more options → General，并说用 `*.*` 可查看预览中的所有格式。该页问答区还直接提到“Preview file extension list”，并举 PDF、JPG、TIF、ICO 预览扩展名作为问题/使用例；这使“按扩展名限制预览资格”成为较有支持的解释，但问答没有说明不匹配文件是否仍留在列表中。该 FAQ 后半的通用说明又混入按日期、大小、关键词过滤并减少列表项目的描述，不能把它当作该设置行为的直接证据。相邻 [FAQ 17](https://www.softwareok.com/?faq=17&seite=faq-Q-DIR) 说明 PDF 预览还依赖 Windows 已安装的预览能力。

官方 [FAQ 38](https://www.softwareok.com/?faq=38&seite=faq-Q-DIR) 则明确把文件过滤作为每个 Explorer view 的独立功能，支持 `*.exe`、分号分隔多个通配符，并可选择是否过滤文件夹。因此快速文件过滤与预览格式资格必须分开建模。当前可确认的结论是：预览格式资格过滤很可能只控制所选项目能否进入预览处理器；是否同时隐藏文件列表项，以及不匹配时预览窗格显示空白还是提示，仍需真实 Q-Dir 可见交互确认。

当前应用的 `IFolderFilterSite` 是文件枚举过滤点；复用它会隐藏列表项目，语义不正确。微软定义的 `IExplorerPaneVisibility::GetPaneState` 只返回 Preview Pane 等整个窗格的可见状态，不能按当前选择文件的扩展名单独禁用预览。保持系统注册的 Preview Handler 与 ExplorerBrowser 原生窗格时，现有接入没有逐文件格式过滤钩子。改由应用自己托管 Preview Handler 则要接管 Shell 的预览处理器生命周期、窗口、焦点与快捷键交互；微软指出处理器通常在进程外系统宿主运行，这一变更扩大兼容和稳定性边界。

本项目已有 Q-Dir 式每标签快速文件过滤；其行为按 FAQ 38 对照，不应重新实现或借用为预览资格过滤。在 Q-Dir 的可见行为得到确认前，不把快速筛选改造成预览过滤，也不随选择隐藏整个预览窗格。下一次可见对照只需区分三点：扩展名不匹配的文件是否仍在文件列表、预览区域显示什么、过滤名单是否逐窗格/全局。若只限制预览资格，再评估逐项过滤需要的宿主边界；若也筛选列表，则按文件可见性功能单独对照。本项目当前架构事实不变：`IFolderFilterSite` 过滤的是列表枚举，`IExplorerPaneVisibility` 控制整个窗格，均不能表达“列表保留项目但不对特定扩展名预览”。接口依据：[Preview Handler Architecture](https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers)、[IExplorerPaneVisibility::GetPaneState](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerpanevisibility-getpanestate)、[IExplorerBrowser](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)。

2026-10-31 用户决定：先等集中 Q-Dir 可见验收再选择“仅禁用预览”还是“同时过滤列表”。因此本项目不新增预览资格配置、不借用快速筛选，也不随选择显隐整个原生预览窗格。该决定解决的是交互语义顺序；实现边界仍需在验收后按实际行为复核。

## 后续验收

在用户明确安排的隔离可见窗口时，检查文本、图片、PDF 等已注册处理器是否在 Preview Host 中显示；切换文件、标签和布局；开关预览后检查文件区尺寸、焦点、Tab/F6 与导航状态。产品菜单/工具栏、默认关闭和每标签保存语义已接入，仍待验证它们的可见交互及重启恢复。未通过渲染与布局验收前，不将预览功能标记为完成，也不发布。
