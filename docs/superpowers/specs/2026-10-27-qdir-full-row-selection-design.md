# Q-Dir 整行选择选项设计

## 意图

补齐 Q-Dir 的“Full row select”体验，让文件列表可以把选择高亮延伸到详细信息视图的整行，同时保留 Windows Shell 文件视图、命中测试、快捷键和原生文件操作。

## 范围与约束

- 在“视图”菜单增加整行选择开关，作为应用级偏好作用于所有现有和新建 Shell 视图。
- 首次运行及未显式选择时不修改 Shell 的默认 `FOLDERFLAGS`；用户切换后才写入便携配置并覆盖 `FWF_FULLROWSELECT`。
- 只修改该 flag，保留 Shell 返回的其他 flags；失败时在状态栏报告，配置与实际视图状态保持一致。
- 不替换 ExplorerBrowser，不重做文件视图、不改变项目选择或右键命中逻辑。
- 真实像素效果及不同查看方式下的行为仍需可见 GUI 验收。

## 实现方式

采用 `IFolderView2::GetCurrentFolderFlags/SetCurrentFolderFlags` 的原生方式，在 ExplorerBrowser 创建视图后应用显式偏好，并在用户切换时更新所有窗格与标签。WPF 自绘高亮无法改变 Shell 子项选中范围；迁移到自定义 `IShellView` 会显著扩大范围并偏离当前产品架构。

偏好独立保存于 profile 的 `FullRowSelection.ini`。文件缺失表示跟随 Shell；显式 `true/false` 分别打开/关闭 `FWF_FULLROWSELECT`。格式错误需先备份再回到跟随 Shell，未知新版本不得覆盖。

## 验收

- 状态规则覆盖默认跟随、显式开/关及无关 flag 保留。
- 配置覆盖缺失、往返、外部修改、格式损坏备份和未来版本保护。
- Shell flag 更新覆盖已有视图、延迟创建视图和失败回报。
- Release SettingsSmoke 与 App Release build 通过；后续在隔离测试目录中对照详细信息/列表视图的实际选中高亮、选择、多选、右键和重启持久化。

## 考虑过的方案

- **推荐：**原生 Shell folder flag 加应用级显式覆盖，保持 Shell 输入和操作语义。
- WPF 绘制整行：ExplorerBrowser 内的项目由 Shell 绘制，无法可靠扩展到子项，也可能破坏主题和可访问性。
- 替换为自定义文件视图：超出单项设置所需范围，并牺牲当前 Shell 兼容性。
