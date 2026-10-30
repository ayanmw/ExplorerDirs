# 2026-10-30 主线集中验收候选（最新）

这是后续集中可见验收用的隔离副本，不是发布版本，也没有启动窗口。候选包含当前克隆视图历史/选区实现、Q-Dir 颜色配置会话保真、复制标签选区恢复及隔离文件夹夹具。

## 固定身份

- 源提交：`e96976c39089b0f77ccf4ffd400c28fc09a6f5a9`，准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-003019-700-b8acd5/`。
- BuildIdentity：`gui-acceptance.20261030-003019-700-b8acd5+e96976c39089b0f77ccf4ffd400c28fc09a6f5a9`。
- 状态：`prepared-not-run`，39 项待验收。
- EXE SHA-256：`C14CC7417A847AF37A037A10CBED51EC308CD34BBE7E25F70FAA643B97711989`。
- App DLL SHA-256：`89044737C2A90DB0B501977E2E23BDA4179AD26B9290EE259C563BBAD42E8E32`。
- 颜色配置夹具 `qdr-color-metadata.qdr` SHA-256：`C6693CCA888F141460E14E249577CA88ED1D91B56CE76DE1E518BA2FAFEFE511`。
- 初始布局为四窗格，使用副本自己的 profile 和隔离文件夹；包含 10,000 个递归大小测试文件及跨窗格复制/移动样本。

## 本次更新与验证范围

验收清单覆盖 `Ctrl+Shift+C`、`Alt+K`/`Alt+V`、F7 路径层级、F8/“此电脑”按钮、地址栏 Tab 顺序，以及标签栏“使用 Explorer 浏览”。新增克隆视图场景：普通目录和搜索结果选区、逐站 Back/Forward、前进分支截断，以及导航/选区在克隆快照期间变化时不建立混合状态标签。Q-Dir 树分隔值仅作不透明字段往返，不代表 DIP 树宽度。

颜色元数据步骤会导入 `qdr-color-metadata.qdr`，检查预览说明“保留但未应用”，再导出到新文件并逐行比对 `useColor`、`useTreeColor`、`colorthema` 和 `[Filter2]` 规则顺序。夹具明确指定 Q-Dir 四窗格布局，包含 `#Hidden`、扩展名、`#BG` 和 `#BG-A` 规则。只读 importer 检查确认其恢复 1 个标签且没有“布局尚未支持”警告。它检查状态文件保真，不表示 ExplorerBrowser 已显示颜色；Q-Dir 实际读取与重存仍待用户集中验收。浅深主题检查使用独立的最小 `qdr-color-baseline.qdr`。

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 于 2026-10-30 成功。manifest 源提交匹配、`SourceWorkingTreeDirty=false`、39 项待验收，EXE/DLL 和颜色夹具 SHA-256 已核对。准备流程未启动窗口、创建 ExplorerBrowser、发送输入、改动剪贴板或真实注册表。

候选仅供之后集中可见验收；准备成功不代表可见交互、Shell 菜单或真实 Q-Dir 互操作通过。旧副本保留在各自 `src/.state/gui-smoke/` 目录。
