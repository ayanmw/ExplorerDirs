# 临时预览 temp.47

## 本版内容

本版基于 temp.46 之后的源码构建。Shell 文件操作结束后，焦点恢复会继续监测当前文件内容区，并确认焦点回到实际内容 HWND；如果输入来自其他 Shell 控件、失活窗格或导航已变化，则取消旧恢复请求。右键目标判定在原生 `SysListView32` 上使用 `LVM_SUBITEMHITTEST`：文件图标/文字算条目，行尾空白算背景，以便保留 Explorer 原生背景菜单。扩展命中标志 `LVHT_EX_ONCONTENTS` 也会计入条目判断。

此版本使用包内独立 profile，不会覆盖 temp.46 或日常配置。

## 已验证

- Release SettingsSmoke 全量通过，含文件操作焦点恢复门控、原生列表命中分类与 Shell 状态回归。
- App Release 构建通过，0 警告、0 错误。
- NativeProbe Release 构建通过；隔离原生视图探针通过。
- 发布隔离 smoke 通过；EXE/DLL SHA-256 清单及独立 profile 已写入包。

## 待预览/验收

- 没有启动应用、ExplorerBrowser、Q-Dir 或可见 GUI。真实文件操作后的物理键盘焦点、选区保持、列表行尾空白右键、列头和第三方 Shell 菜单仍待桌面验收。
- 此包是功能预览，不代表 Explorer/Q-Dir 体验验收完成；需要 .NET 10 Windows Desktop Runtime。
