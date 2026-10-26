# Explorer Dirs Preview 0.6.0 temp.70

发布位置：`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.70\app\ExplorerDirs.App.exe`

本包基于提交 `9b0e91c`，继承 temp.69 的功能，并增加文件操作后的焦点恢复行为：窗口暂时失焦时，仍有效的有界恢复请求会保留；应用在后台时不读取 Shell 焦点或调用 `SetFocus`；切回后若活动窗格、标签、路径及用户输入代数仍匹配，继续恢复。

产品版本：`0.6.0-preview.temp.70+9b0e91c596a134c10979b270a4aa91edd53aff03`

SHA-256：

- `ExplorerDirs.App.exe`：`1225C43B0989E52D749FD13AB16E91B097CC6A4ECC2DAB431EC1F8332C94EC23`
- `ExplorerDirs.App.dll`：`7F39DCFF6D1A2658C8E023B84D8E7872D343789D6459136AB8A79C9E27323431`

验证：完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；发布脚本将版本、哈希清单、目录打开能力清单及独立 `app/profile` 写入发布目录，包内哈希和 profile 文件哈希复核通过。

profile 从 temp.69 的空闲隔离 profile 复制，仅包含 `ExplorerDirs.ini` 与 `PinnedTabs.ini`；不与当前运行的 temp.58 共用配置。发布程序没有启动。

尚待验收：真实 Ctrl+C/V、Shift+Delete 和 Alt+Tab 时序下方向键是否始终回到正确文件视图；目录树图标修复的可见绘制；其它多窗格键盘与 Shell 操作行为。隐藏状态回归和构建结果不等于可见 UI 验收。

补充诊断：隔离 `--tree-host-core` 探针核对生产目录树的已选节点，系统图像列表已绑定且普通/选中图标索引均为 `3`，列表共 512 项；探针未验证屏幕像素是否绘制。该结果不替代目录树图标的可见验收。
