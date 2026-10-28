# Q-Dir 式空白标签栏双击新建标签实施计划

**目标：** 在标签栏空白区域左键双击时复用现有 `NewTab()` 创建标签，同时保留双击标签标题关闭标签的行为。

**约束：** 不改标签会话、目录默认值、标签拖动或关闭行为；不操作 GUI；每批提交本地 Git。

## 步骤

- [x] 在 `TabHeaderScroll` 注册预览左键按下处理器。仅当 `ClickCount == 2` 且 `ItemsControl.ContainerFromElement(Tabs, OriginalSource)` 不是 `TabItem` 时调用 `NewTab()` 并消费输入。
- [x] 在 SettingsSmoke 增加 XAML 与处理器接线检查，确认双击现有标签被排除并复用既有 `NewTab()`。
- [x] 更新 Q-Dir 功能差距记录，注明实际双击验收尚未完成。
- [x] 运行完整 Release SettingsSmoke、App Release build 和 `git diff --check`，然后本地提交。

## 验证边界

构建与接线 smoke 只能证明源码路由和编译状态。它们不能证明实体鼠标双击的 WPF 命中、现有标签关闭手势或新标签视觉行为。
