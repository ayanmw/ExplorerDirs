# Explorer Dirs Preview 0.6.0 temp.73

发布位置：`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.73\app\ExplorerDirs.App.exe`

本包基于提交 `0d874d4`。根据内存优化预览中约 2 MB 的有限变化，移除“优化内存”菜单和相关实现。新增“文件 > 重启应用”：确认后保存当前进程管理的全部窗口会话，保留多窗口会话清单，关闭这些窗口，释放单实例路由，再从同一发布目录启动应用。电脑上其他独立运行的 Explorer Dirs 进程不受影响；任一窗口会话无法保存时会取消重启。该项不改变 Explorer Dirs 的默认系统集成或文件夹路由。

用户实测参考：截图中不同进程的物理内存约为 229 MB、293 MB、316–318 MB；其中一个进程的提交大小约 156 MB。样本涉及多个进程且工作状态并非完全一致，只作为后续运行期增长调查的起点，不作为单实例基准。

产品版本：`0.6.0-preview.temp.73+0d874d442d0f6efb0535cb920729fa89fd691da7`

SHA-256：

- `ExplorerDirs.App.exe`：`1230251D4E33F5E932669E4A20C7C55FB5C42D7B254C9ED2B9C2549533EC60F3`
- `ExplorerDirs.App.dll`：`7BFA7311A8AC5976CAABB2177862279A219D35756BE256622142CAABD6EDDB31`

验证：Release 构建 0 警告、0 错误；完整 SettingsSmoke 通过；`git diff --check` 通过；发布包哈希清单已复核；隔离 profile 包含 2 个文件。预览程序未启动，重启的可见窗口恢复流程仍需桌面验收。

后续观察方向：用同一个应用进程、固定窗格/标签数和相同目录，分别记录启动稳定后及持续浏览后的物理内存、Private Bytes/提交大小、线程和句柄数；再逐步加入目录切换、标签切换、缩略图/预览、网络位置和 Shell 扩展场景，定位增长来源。当前尚未添加自动监视器。
