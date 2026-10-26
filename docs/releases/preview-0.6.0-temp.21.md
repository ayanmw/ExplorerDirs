# Explorer Dirs `preview-0.6.0-temp.21`

此版本基于当前源码生成，使用独立 profile；不覆盖既有预览版或日常 profile。

## 相较 temp.20 的变化

- `.qdr` 的 `tree_mode` 基础字段现按已核实映射读写：`0` 关闭目录树，`1` 使用共享树，`2` 每个窗格各用一棵树。`Tree1`～`Tree4` 的布尔语义和 `TreeNsplit` 的宽度含义仍未获可见互操作确认；后者继续提示不支持。
- 每窗格目录树可见时，Tab/Shift+Tab 停靠顺序包含该窗格导航栏的“树”按钮；目录树关闭或改为共享树时，该按钮不再占用停靠点。
- 键盘触发 Shell 上下文菜单的 `(-1,-1)` 坐标现在按消息低 32 位识别，兼容零扩展和符号扩展的 `LPARAM`；菜单键/Shift+F10 不再误走鼠标命中探测。

## 验证与边界

- App Release 构建、Release `SettingsSmoke`、发布隔离 smoke 和文件夹右键入口隔离 smoke 均通过；包内产品版本和 EXE/DLL SHA-256 已核对。
- 本包使用 `app/profile/` 独立配置目录。未启动本预览、ExplorerBrowser 或 Q-Dir；键盘焦点、可见目录树、原生菜单及 `.qdr` 的 Q-Dir 实际互操作仍待桌面验收。
- 本预览不代表路线图中的 Shell 体验与稳定性目标已经完成。
