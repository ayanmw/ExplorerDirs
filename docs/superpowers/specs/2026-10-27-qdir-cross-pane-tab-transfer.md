# 跨窗格移动和复制标签

## 行为

- 在另一窗格的标签标题或标签栏空白区域释放普通拖动，将原标签移入目标窗格。保留 `TabState`、`ExplorerBrowserHost`、原生 HWND 与 Shell Travel Log。
- Ctrl+拖动复制标签，在目标窗格创建独立 Shell view，并复制当前位置、视图设置、筛选、预览/详情窗格状态和地址历史。
- 普通移动不能让源窗格失去最后一个标签；复制不受此限制。
- 固定与普通标签只能移入相同类别的区域；放到标签标题上按释放位置插入，放到标签栏空白处追加到对应类别末尾。
- 同窗格拖动继续重排标签。Shell 文件/文件夹拖放行为保持独立。

## 原生宿主

跨窗格移动只从源 `PersistentTabHost` 暂时卸下 HwndHost。由 WPF 自身将原 HWND 暂存到隐藏宿主，再挂接目标；原 COM browser 和 host HWND 保留。不能以保留标记跳过 `DestroyWindowCore`：此路径不会再次调用 `BuildWindowCore` 清除标记，之后关闭标签会漏掉清理。关闭标签或正常销毁时仍须销毁浏览器和原 HWND。依据：[WPF HwndHost 的 BuildOrReparentWindow](https://source.dot.net/PresentationFramework/System/Windows/Interop/HwndHost.cs.html)。

## 验收

- Release SettingsSmoke 覆盖移动/复制/最后标签/固定分区策略和源码接线。
- 隐藏 HostProbe 通过真实 PaneControl 与 ExplorerBrowser 验证同一 TabState、同一 host 对象/HWND、可调整大小以及移动前的 Shell 后退历史仍可用；Ctrl 复制创建独立 host。
- 不操作可见桌面。真实拖动输入、指针游标、不同 DPI 和多个显示器仍待可见验收。
- 2026-10-27 生命周期补验：非 Shell 隐藏普通 HWND 连续 8 次重挂保留身份；产品宿主清理入口旧代码在保留标记下跳过销毁，修复后销毁成功。真实 Shell 探针已加入移动后关闭的 HWND/COM 释放检查，当前无桌面授权，未重跑。
- 2026-10-27 排序补验：未挂接窗口的实际 PaneControl 复现固定标签复制到空白栏后落入普通区；修复后覆盖固定/普通标签的前插、后插、追加，以及选中索引、会话顺序和副本宿主独立性。未创建 HWND/Shell 视图，不替代实际 Ctrl+拖动验收。
