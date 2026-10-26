# Explorer Dirs 0.6.0 临时预览版 53

本版修正了目录刷新后 Shell 视图短暂返回空列表时的计数恢复行为，并更新了对应的隔离生产宿主回归。

## 本版重点

- 刷新回调连续读到空视图后，若列表随后恢复为稳定的非零数量，先观察其稳定性，再按实际视图数量重建计数基准，避免针对同一差值反复刷新。
- 文件操作监视探针现在在通过 Shell COM 设置多选和独立焦点后，同步产品选区快照，再验证刷新恢复；避免用未同步的焦点基线制造误报。

## 验证范围

- 新增状态回归覆盖“连续空视图 → 稳定非零但计数不匹配 → 重建基准”，测试先失败后通过。
- 隔离生产 HostProbe `--live-shell-product-monitor-no-parent-observer` 通过：复制创建、计数失配恢复、刷新期间 watcher 事件、选区/焦点恢复及跨窗格复制/移动/删除、重命名和文件夹操作均通过。
- 完整 Release SettingsSmoke 通过；App 与 HostProbe Release 构建均为 0 警告、0 错误。
- 本版没有启动 GUI、ExplorerBrowser 可见窗口或 Q-Dir。测试未使用系统剪贴板；真实 Ctrl+V、键盘焦点和用户可见刷新时序仍待桌面验收。

请从 `app/ExplorerDirs.App.exe` 启动。预览运行时对本包 `app/profile` 的修改不会写回项目日常 `profile`。
