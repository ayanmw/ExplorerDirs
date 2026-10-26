# Explorer Dirs 0.6.0 临时预览第十四版

日期：2026-10-24。目录版本：`preview-0.6.0-temp.14`；程序集产品版本：`0.6.0-preview.temp.14+77495f5d72a4739c2bb33a21d021328294f728c0`。

程序位于 `app/`，使用包内独立 `profile/`；日常 profile 不会被此次打包升级，程序也不依赖 `src/` 下的调试输出。

## 相比 temp.13

- 修复“导出 Q-Dir 状态”时手工创建的快照遗漏目录树模式的问题。共享树、逐窗格树和关闭模式现在从完整窗口会话快照导出，并由 Explorer Dirs 扩展保存。
- 将无效 Shell 位置的导入提示改为说明目标路径无效或不是可用文件夹；可解析并经 Shell 确认为文件夹的绝对解析名称仍可导入。

## 构建验证与已知边界

- 全量 Release `SettingsSmoke` 通过；App、HostProbe、NativeProbe Release 构建均为 0 警告、0 错误；发布隔离 profile smoke 通过。
- 打包后核对 EXE/DLL SHA-256、程序集产品版本、隔离 profile 与本说明。未启动程序、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir。
- 该版本供预览，不代表真实窗口行为、Q-Dir 实际读写互操作、文件区颜色问题或自动刷新长尾已验收。目录树模式的本程序扩展往返已测；Q-Dir 原生 `tree_mode` 数值仍未映射。

从 `app/ExplorerDirs.App.exe` 启动。需要 .NET 10 Windows Desktop Runtime。
