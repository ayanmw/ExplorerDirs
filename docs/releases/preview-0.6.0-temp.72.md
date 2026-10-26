# Explorer Dirs Preview 0.6.0 temp.72

发布位置：`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.72\app\ExplorerDirs.App.exe`

本包基于提交 `4ea5a41`。新增“开发者 > 优化内存（一次）”：对未选中的标签，以及当前布局隐藏窗格中的标签，尝试释放可安全重建的 ExplorerBrowser 视图。只有 Shell 前进/后退历史明确为空、文件系统位置和视图状态读取成功、没有选中项，并且详细/列表视图位于首行时才会释放。标签与已捕获视图设置保留；切回后 Shell 文件视图重新加载。功能不保存设置。

产品版本：`0.6.0-preview.temp.72+4ea5a418144a8a15896d43755f9d9d47028e513e`

SHA-256：

- `ExplorerDirs.App.exe`：`CEF6F03337709A1506167A345439D036224E9F4C11A2A47FBE9B0A2685AA8673`
- `ExplorerDirs.App.dll`：`F5FCB377D04B75160DB2C05BE5858059E371CA531DFEA480350A96203DFC160B`

验证：Release 构建 0 警告、0 错误；完整 SettingsSmoke 通过；`git diff --check` 通过；发布包哈希清单已复核；隔离 profile 包含 2 个文件。发布程序未启动，尚无真实桌面行为或内存前后实测数据。

建议按相同目录和布局记录优化前的物理内存与提交大小，执行一次命令并记录“释放/跳过”数量，再记录优化后数据。若当前是四窗格布局且每个窗格都显示唯一活动标签，且没有其他未选中标签，预期为 0 个视图释放；可先切到较少窗格的布局，让隐藏窗格参与优化。然后切回已释放的标签/窗格，检查视图加载时间、查看方式、排序/分组和历史行为。本预览不代表完整 GUI 验收。
