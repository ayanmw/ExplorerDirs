# Modern 地址栏 Shell 目录树

## Intent

补齐 Q-Dir Modern 地址栏的目录树交互，让用户能从每个文件窗格的地址栏展开真实 Windows Shell 文件夹树并导航当前标签。Q-Dir 官方 FAQ 116 描述第一次 F4 打开地址树、第二次 F4 聚焦路径编辑；本项目现有 F4 历史快捷键与独立历史按钮重复，因此 F4 将用于地址树/编辑两阶段，历史保留在按钮。

## Goals

- 仅 Modern 地址栏样式使用地址树；Classic 样式下 F4 直接进入当前窗格的路径编辑。
- 第一次 F4 打开当前窗格的 Shell 树并聚焦树；再次 F4 关闭树、显示完整路径编辑框并聚焦文本。
- 地址树从 Desktop 命名空间显示文件夹，按 Shell 文件夹身份导航文件系统与可解析的虚拟 Shell 文件夹；搜索结果、非文件夹和协议 URI 不作为地址目标。
- 鼠标选择文件夹后只导航该窗格当前标签，并保留应用现有导航历史、会话路径和活动窗格语义。
- Escape 关闭地址树并把焦点还给地址栏；关闭/切换标签、窗格卸载或窗口失活时清理弹出状态，不创建共享的第二份浏览会话。
- 地址历史下拉按钮继续保持独立入口，历史范围和持久化规则不变。

## Non-goals

- 不修改 ExplorerBrowser 文件视图、系统文件夹关联或 Win+E 路由。
- 不替换每窗格/共享/关闭目录树模式，不把地址树状态写进会话或 `.qdr`。
- 不仿制 Q-Dir 自有的图标、颜色、展开延时或树线主题。
- 不在 Classic 地址栏复刻面包屑或树弹出；Classic 下 F4 用于编辑地址。

## Constraints

- 使用已有 `NativeShellTree` 与原生 Shell 命名空间；Shell COM 留在创建它的 STA，不能通过隐藏 ExplorerBrowser 探针测试。
- 任何树项成为地址前都要通过 `ShellLocationIdentity` 的绝对位置、文件夹属性校验；持久化/导航用规范 Shell parsing name。
- 弹出树需由当前 `PaneControl` 独立拥有，切标签不得把树选择带到别的标签；导航与关闭回调需校验窗格和当前标签身份。
- 不记录浏览路径到诊断日志；构建和纯状态 smoke 不证明原生树弹出、键盘焦点或虚拟文件夹 UI 可用。

## Approach

Use a WPF `Popup` anchored to each pane's address host, containing a dedicated `ShellTreeHost` rooted at `shell:Desktop`. Extend the native tree selection path to return a validated canonical absolute Shell folder name rather than a filesystem path only. Keep the existing history button unchanged. This is the closest supported route to Q-Dir's real Shell tree and avoids disguising the existing left navigation tree or a flat ancestor menu as address-tree parity; it also requires later visible acceptance for popup HWND and focus behavior.

## Acceptance

- Pure shortcut/state tests prove first F4 opens tree in Modern, second F4 enters path edit, Classic F4 edits directly, and history remains a button action.
- Shell location tests prove filesystem and virtual absolute folder names are accepted, while URI, search identity and non-folder targets are rejected.
- Release App and SettingsSmoke build with no warnings/errors; full SettingsSmoke and focused address-tree state smoke pass.
- Visible GUI later verifies placement, expansion/selection, F4/Escape focus, tab switching, and virtual locations. Until then the feature is implemented but visible Shell acceptance remains pending.
