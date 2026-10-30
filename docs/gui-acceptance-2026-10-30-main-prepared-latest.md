# 2026-10-30 主线集中验收候选（最新）

这是后续集中可见验收用的隔离副本，不是发布版本，也没有启动窗口。候选基于当前主线，包含克隆视图历史/选区、Q-Dir 颜色配置会话保真、复制标签选区恢复、最近收藏标题按窗口 session 跨重启保存及隔离文件夹夹具。

## 固定身份

- 源提交：`103d15c6c94044834bc47323c07a43993ec91a44`，准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-022742-972-3c2dad/`。
- BuildIdentity：`gui-acceptance.20261030-022742-972-3c2dad+103d15c6c94044834bc47323c07a43993ec91a44`。
- 状态：`prepared-not-run`，39 项待验收。
- EXE SHA-256：`33BD4494E39F30F628A94EA186F9FF7E23D5F1ED1871B0E39D19FA72D7CCF51A`。
- App DLL SHA-256：`0A23E2E568B168E513D9E0AA0CA20DFB8F6F598430A86F029D272F45C6AD5E73`。
- `qdr-color-metadata.qdr` SHA-256：`D9452C2B460C95BC05A8D0D2179B199AAFD9EF17F7A577CD2E383649C93CD200`。
- `qdr-tab-capacity-99.qdr` SHA-256：`08E49B117BEE4D3443A47635896B7E1C2A033EB8354DAD9137E51A2501402265`。
- `qdir-program-items.ini` SHA-256：`ED433EA6803E47D30CC85B117A839C2049BDA0C3F3D6A390A457641EDE4D99BC`。
- 初始布局为四窗格，使用副本自己的 profile 和隔离文件夹；包含 10,000 个递归大小测试文件及跨窗格复制/移动样本。

## 本次更新与验证范围

验收清单包含“使用 Explorer 浏览”、最近收藏标题的双窗口独立恢复、地址栏/键盘导航、文件操作和 10,000 个文件的递归大小进度。克隆视图保持独立于“复制当前标签”：检查普通目录与搜索结果选区、逐站 Back/Forward、前进分支截断，以及捕获期间导航/选区变化时拒绝创建混合状态标签。异步搜索结果身份和快照代次由 SettingsSmoke 覆盖，可见窗口中的焦点和自动选区恢复仍待验收。

列值分页菜单纳入大目录验收：在 10,000 个不同名称中翻页、勾选后返回并确认勾选状态，再清除并核对结果恢复；分页纯逻辑由 SettingsSmoke 覆盖，菜单实际观感与响应仍待验收。

颜色元数据步骤导入 `qdr-color-metadata.qdr`，检查预览说明颜色规则被保留但未应用，再导出到新文件并逐行比对 `useColor`、`useTreeColor`、`colorthema` 和 `[Filter2]` 行顺序。此项验证状态文件保真，不代表 ExplorerBrowser 已显示逐项颜色；Q-Dir 打开并重存导出文件仍待验收。浅深主题回归使用独立的 `qdr-color-baseline.qdr`。

准备命令为 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly`，于 2026-10-30 成功。manifest 的源提交与主线 HEAD 一致，`SourceWorkingTreeDirty=false`；EXE、App DLL、颜色夹具、容量夹具和程序启动器夹具的实测哈希均与 manifest 一致。准备过程未启动窗口、创建 ExplorerBrowser、发送输入、改动剪贴板或真实注册表。

候选仅供之后集中可见验收；准备成功不代表可见交互、Shell 菜单或真实 Q-Dir 互操作通过。旧副本保留在各自 `src/.state/gui-smoke/` 目录。
