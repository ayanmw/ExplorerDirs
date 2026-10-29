# 跨窗格移动和复制标签实施计划

- [x] 加入策略 smoke，定义同窗格重排、跨窗格移动/复制、最后标签保护和固定标签分区。
- [x] 将拖动 payload 改为携带来源窗格与原 TabItem；普通拖动移动，Ctrl+拖动复制；支持目标标签与空标签栏。
- [x] 令标签事件回调按可变 pane owner 路由，移动后仍由新窗格维护地址栏、Shell 命令和焦点。
- [x] 暂时卸载 HwndHost 时保留 ExplorerBrowser 和原生 HWND，在目标窗格重挂，不销毁 Shell Travel Log。
- [x] 增加隐藏 HostProbe，验证原对象/HWND、目标宿主尺寸及移动前后 Back 历史，并验证复制为独立 host。
- [x] 更新路线图和 Q-Dir 功能对照。
- [x] 运行完整 Release SettingsSmoke、App/HostProbe Release build、隐藏 HostProbe 和 `git diff --check`。
- [x] 检查 diff 并提交本地 Git。
- [x] 复制标签时保留同目录文件系统选区，并在目标 Shell view 导航完成后恢复。
- [x] 隐藏 HostProbe 验证多文件选区在独立 Shell view 中实际恢复，运行 Release SettingsSmoke、App/HostProbe Release build 与 diff 检查后单独提交。
- [x] 选区成员在复制期间消失时跳过失效项，并覆盖部分存活与全部失效路径。
