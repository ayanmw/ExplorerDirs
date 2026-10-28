# Explorer Dirs Preview 0.6.0 temp.71

发布位置：D:\projects\explorer-dirs\releases\preview-0.6.0-temp.71\app\ExplorerDirs.App.exe

本包基于提交 7ad7c23。新增“开发者 > 优化内存（一次）”：仅尝试释放非活动标签中导航历史已确认为空、文件系统位置、Shell 视图设置可完整读取、无选中项且详细/列表视图位于首行的视图。跳过不满足条件的视图；被释放标签重新选中时会重新加载 Shell 视图。此功能不保存配置。

产品版本：`0.6.0-preview.temp.71+7ad7c23784bec4069d0a9f10d7dcd85c5a3c0460`

SHA-256：

- `ExplorerDirs.App.exe`：`34A1E86BC1263A0D96BFCE9C6BA4A4D30D3BB85CDBB1D9C7BDE822B2859346A5`
- `ExplorerDirs.App.dll`：`61DD9E2827BD263E892EF65A6B34D7E2BC0EB4A510F250A590EF58CC06A7F43B`

验证：Release 构建 0 警告、0 错误；SettingsSmoke 完整检查通过；git diff --check 通过；发布包哈希清单已复核。隔离 profile 文件数：2。发布程序未启动，尚无真实桌面行为或内存前后实测数据。

建议你在相同目录、相同布局下先记录优化前物理内存/提交大小，再点击一次“开发者 > 优化内存（一次）”，记录被释放数量及优化后的数据；最后切回被释放的标签观察 Shell 刷新耗时、当前查看模式、排序/分组与标签导航行为。此包不是完整 GUI 验收版本。
