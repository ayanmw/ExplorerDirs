# 2026-10-30 主线集中验收候选（最新）

此候选用于之后集中做可见验收，不是 `releases/` 日常预览版；状态为 `prepared-not-run`。准备过程只构建并生成隔离副本与夹具，没有启动程序、创建 ExplorerBrowser、发送输入、修改真实注册表或剪贴板。

## 固定身份

- 源提交：`14e6aa5cfc2f6fbc9a230211faf3a0270f8fa0fa`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-052522-882-ebcdd5/`。
- BuildIdentity：`gui-acceptance.20261030-052522-882-ebcdd5+14e6aa5cfc2f6fbc9a230211faf3a0270f8fa0fa`。
- 状态：`prepared-not-run`，39 项待验收；四窗格初始布局。
- EXE SHA-256：`DBADF0398F70872BD0698E92805538A117D578DC1261B700594D7D7F4D8AA7F7`。
- App DLL SHA-256：`BBD282355C74B9359075B5CFE9FC5B9FC15EA3D7B7B37E7FA24A6B60F14B72D7`。
- `qdr-color-metadata.qdr` SHA-256：`D82975F40BC12155AE4F235C30AC5E75EAA254B5F136DA2FCA171853805438B4`。
- `qdr-tab-capacity-99.qdr` SHA-256：`EEF8DC579CF9B0C0A29F0AB42269D0DA6D51263C10ABC24433339D9A563231CE`。
- `qdir-program-items.ini` SHA-256：`ED433EA6803E47D30CC85B117A839C2049BDA0C3F3D6A390A457641EDE4D99BC`。
- `acceptance-files/recursive-size-progress` 有 10,000 个测试文件，并包含跨窗格复制/移动样本；Profile 与真实 Explorer 注册表分离。

## 本次可见验收重点

- 文件区焦点、Tab/Shift+Tab、复制粘贴和 Shift+Delete 后方向键、F2 编辑；逐窗格查看方式与列值筛选菜单。
- 标签栏“使用 Explorer 浏览”、空白处右键、独立“克隆视图”的路径/逐站选区/搜索历史行为。
- Q-Dir `.qdr` 颜色字段导入导出保真、浅深主题及原生菜单边框；文件夹入口只读预览，不启用真实注册表入口。
- 10,000 项列值筛选分页、F9 递归大小进度与取消、程序启动器导入预览、双窗口位置与 session 恢复。

完整 39 项清单在副本的 `preparation.json` 中。候选于 2026-10-30 05:25 UTC 构建；复核源提交与 `SourceWorkingTreeDirty=false`，并从 manifest 核对 EXE、DLL 和主要导入夹具身份。该候选包含当前主线的克隆视图、单次目录树扫描及近期功能，但不证明逐项大小列已接入；该能力仍停留在隔离架构验证。候选尚未运行，后续可见验收请用此副本和独立 profile，不要将其视为日常预览发布。
