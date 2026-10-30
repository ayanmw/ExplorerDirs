# 2026-10-30 主线集中验收候选（最新）

此候选供后续集中可见验收使用，不是 `releases/` 日常预览版；状态为 `prepared-not-run`。准备过程只构建并生成隔离副本与夹具，没有启动程序、创建 ExplorerBrowser、发送输入、修改真实注册表或剪贴板。

## 固定身份

- 源提交：`290d05a0a455618abcf15dd57a70d21e0d8a7412`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-060610-762-f18303/`。
- BuildIdentity：`gui-acceptance.20261030-060610-762-f18303+290d05a0a455618abcf15dd57a70d21e0d8a7412`。
- 状态：`prepared-not-run`，40 项待验收；初始为四窗格布局。
- EXE SHA-256：`510E9E39383550639E7CF210196A39405F2657042C80BFADD28D8D2F6B891642`。
- App DLL SHA-256：`15CBC20F5BB11C6EE12B62AD523BA2983270D18A50DDA2C7BFC6CE7609180D8B`。
- Q-Dir 颜色、标签容量及 Program Starter 夹具的哈希与逐项清单记录在副本 `preparation.json`。
- `acceptance-files/` 含克隆历史、跨窗格文件操作、10,000 项筛选/大小扫描等隔离夹具；profile 与用户正式配置分离。

## 本次可见验收重点

- 标签栏“使用 Explorer 浏览”、空白处右键，以及独立“克隆视图”的路径、逐站选区、Back/Forward、搜索结果恢复和前进分支行为；确认克隆命令不改变“复制标签”的既有行为。
- 文件视图键盘焦点、Tab/Shift+Tab、文件操作后方向键、列标题筛选分页、布局与查看方式；四窗格之间切换时观察是否有无关重绘。
- Q-Dir `.qdr` 颜色字段往返保真、浅/深主题及 Shell 菜单边界；文件夹入口只读预览，不启用真实注册表入口。
- 程序启动器导入预览、双窗口位置/session 恢复、F9 递归大小进度与取消。
- Windows Explorer 重启只在专用 disposable desktop session 验收；不可对日常桌面执行。

完整 40 项清单、构建身份和夹具哈希在副本 `preparation.json`。候选于 2026-10-30 06:06 UTC 从提交 `290d05a` 构建；manifest 确认 `SourceWorkingTreeDirty=false`。克隆视图代码已包含完整应用级导航历史及每站选区快照，后台 SettingsSmoke/HostProbe 证据见路线图；此候选只准备了人工验收材料，不代表可见 UI 已通过。逐行派生大小仍是隔离架构调查，不在产品 ExplorerBrowser 中实现。
