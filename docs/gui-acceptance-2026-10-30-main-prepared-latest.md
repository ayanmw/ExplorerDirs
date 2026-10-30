# 2026-10-30 主线集中验收候选（最新）

此候选用于之后集中做可见验收，不是 `releases/` 日常预览版；状态为 `prepared-not-run`。准备过程只构建并生成隔离副本与夹具，没有启动程序、创建 ExplorerBrowser、发送输入、修改真实注册表或剪贴板。

## 固定身份

- 源提交：`6e9de3c4548e75b741398decb0cc29517a4dd1a0`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-055901-649-45914e/`。
- BuildIdentity：`gui-acceptance.20261030-055901-649-45914e+6e9de3c4548e75b741398decb0cc29517a4dd1a0`。
- 状态：`prepared-not-run`，40 项待验收；四窗格初始布局。
- EXE SHA-256：`CE27E61A8E7AA4F25E11E0D13DF4205525E1FD0AEF43D54AC00785B010E02EC7`。
- App DLL SHA-256：`FADF74C6F654B209EE5804F0B7D3563A10EFAC3316B83892842C2D7F516F5487`。
- `qdr-color-metadata.qdr` SHA-256：`5046826193F399573EA8CB90A39A03A279DFA388EF94FDC681D6EC16E2BCE8AF`。
- `qdr-tab-capacity-99.qdr` SHA-256：`27029ED6C3539D42A46BA2837221414B0145330DA3EDB3C2998076C081357DDC`。
- `qdir-program-items.ini` SHA-256：`ED433EA6803E47D30CC85B117A839C2049BDA0C3F3D6A390A457641EDE4D99BC`。
- `acceptance-files/recursive-size-progress` 有 10,000 个测试文件，并包含跨窗格复制/移动样本；Profile 与真实 Explorer 注册表分离。

## 本次可见验收重点

- 文件区焦点、Tab/Shift+Tab、复制粘贴和 Shift+Delete 后方向键、逐窗格查看方式与列值筛选菜单。
- 标签栏“使用 Explorer 浏览”、空白处右键、独立“克隆视图”的路径/逐站选区/搜索历史行为；克隆视图不替换现有复制标签语义。新增 `clone-history` 两站夹具，用于确认克隆时选区、Back/Forward 每站选区快照及源/克隆互相独立。
- Q-Dir `.qdr` 颜色字段导入导出保真、浅深主题及原生菜单边框；文件夹入口只读预览，不启用真实注册表入口。
- 10,000 项列值筛选分页、F9 递归大小进度与取消、程序启动器导入预览、双窗口位置与 session 恢复。
- Explorer 重启确认/桌面恢复仅在专门的 disposable desktop session 验收；不对日常用户桌面执行。

完整 40 项清单在副本的 `preparation.json` 中。候选于 2026-10-30 05:59 UTC 构建；复核源提交与 `SourceWorkingTreeDirty=false`，并从 manifest 核对 EXE、DLL 与导入夹具身份。候选尚未运行，未启动 GUI 或创建 ExplorerBrowser。克隆视图产品代码和后台探针已覆盖独立命令、选区快照及可重放历史；新增手动项仍需可见验收。候选包含单次目录树扫描及近期功能，但不证明逐项大小列已接入；该能力仍停留在隔离架构验证。后续可见验收请使用此副本和独立 profile，不要将其视为日常预览发布。
