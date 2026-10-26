# 所有查看方式显示列标题

## 目标

让原生 Shell 文件视图在图标、列表、平铺等查看方式下也显示列标题，缩小与 Q-Dir 的常用浏览体验差异，同时保留 ExplorerBrowser 的原生文件项、菜单、排序和选择行为。

## 方案

在 ExplorerBrowser 初始化前设置 `FVO_CUSTOMPOSITION`，并在每次 `OnViewCreated` 中只清除 `FWF_NOHEADERINALLVIEWS` 与 `FWF_NOCOLUMNHEADER`。保留其它 `FOLDERFLAGS`，继续使用现有 ExplorerBrowser 和原生 Shell 列管理器。

不自行绘制标题栏，也不替换为自定义 `IShellView`；这两种做法会扩大对原生排序、列状态和文件视图操作的影响范围。若 Shell 不支持相关选项，应保留原视图并报告能力未能应用，不降级成模拟列头。

## 验收边界

纯逻辑测试和 Release 构建验证标志掩码及代码接线。隐藏 ExplorerBrowser 与可见窗口中八种模式、排序/排列、列宽顺序、选择和菜单行为仍需后续验收；在该验收完成前不发布为已确认的 Q-Dir 等效行为。

## 依据

- Microsoft [`FOLDERVIEWOPTIONS`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/ne-shobjidl-folderviewoptions) 定义 `FVO_CUSTOMPOSITION`，并说明通过 `IFolderViewOptions` 控制该选项。
- Microsoft [`IFolderViewOptions`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/nn-shobjidl-ifolderviewoptions) 说明 Windows 提供的实现用于 `CLSID_ExplorerBrowser`。
- Microsoft [`IFolderView2::SetCurrentFolderFlags`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setcurrentfolderflags) 要求 Windows 7 及以后与 `FVO_CUSTOMPOSITION` 配合使用。
- Microsoft [`FOLDERFLAGS`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags) 定义两个列标题抑制位及其行为。
