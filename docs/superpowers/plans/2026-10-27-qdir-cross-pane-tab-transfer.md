# 跨窗格移动和复制标签实施计划

- [x] 加入策略 smoke，定义同窗格重排、跨窗格移动/复制、最后标签保护和固定标签分区。
- [x] 将拖动 payload 改为携带来源窗格与原 TabItem；普通拖动移动，Ctrl+拖动复制；支持目标标签与空标签栏。
- [x] 令标签事件回调按可变 pane owner 路由，移动后仍由新窗格维护地址栏、Shell 命令和焦点。
- [x] 暂时卸载 HwndHost 时保留 ExplorerBrowser 和原生 HWND，在目标窗格重挂，不销毁 Shell Travel Log。
- [x] 增加隐藏 HostProbe，验证原对象/HWND、目标宿主尺寸及移动前后 Back 历史，并验证复制为独立 host。
- [x] 更新路线图和 Q-Dir 功能对照。
- [x] 运行完整 Release SettingsSmoke、App/HostProbe Release build、隐藏 HostProbe 和 `git diff --check`。
- [x] 检查 diff 并提交本地 Git。
