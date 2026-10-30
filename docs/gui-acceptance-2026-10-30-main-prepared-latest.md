# 2026-10-30 主线集中验收候选（最新）

这是后续集中可见验收用的隔离副本，不是发布版本，也没有启动窗口。候选包含当前克隆视图历史/选区实现、Q-Dir 颜色配置会话保真、复制标签选区恢复及隔离文件夹夹具。

## 固定身份

- 源提交：`08ab2a82102ada97fc277b76095963544dc96193`，准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-004055-897-500aed/`。
- BuildIdentity：`gui-acceptance.20261030-004055-897-500aed+08ab2a82102ada97fc277b76095963544dc96193`。
- 状态：`prepared-not-run`，39 项待验收。
- EXE SHA-256：`464F0BF88CCCE10B29640C9414EF2A4534C83CD12EC5C5E1170DF2FF4C2F7A88`。
- App DLL SHA-256：`E959DBBFAD0569CC2B717BF9EEFFD621746595F4313B3251E74376A6D1EC1BFA`。
- 颜色配置夹具 `qdr-color-metadata.qdr` SHA-256：`6EBC8D9C80ADF2B8A24C88BD60276628FCED240255A9CFCAE3D3BA0B9D724B22`。
- 初始布局为四窗格，使用副本自己的 profile 和隔离文件夹；包含 10,000 个递归大小测试文件及跨窗格复制/移动样本。

## 本次更新与验证范围

验收清单覆盖 `Ctrl+Shift+C`、`Alt+K`/`Alt+V`、F7 路径层级、F8/“此电脑”按钮、地址栏 Tab 顺序，以及标签栏“使用 Explorer 浏览”。克隆视图可见场景检查普通目录和搜索结果选区、逐站 Back/Forward、前进分支截断，以及导航/选区在克隆快照期间变化时不建立混合状态标签。异步搜索结果行身份复核由 SettingsSmoke 覆盖；可见验收仍需确认搜索选区结果。列值筛选菜单的候选值/选择语义有 SettingsSmoke，菜单实际外观及大目录性能未做 GUI 测量。Q-Dir 树分隔值仅作不透明字段往返，不代表 DIP 树宽度。

颜色元数据步骤会导入 `qdr-color-metadata.qdr`，检查预览说明“保留但未应用”，再导出到新文件并逐行比对 `useColor`、`useTreeColor`、`colorthema` 和 `[Filter2]` 规则顺序。夹具明确指定 Q-Dir 四窗格布局，包含 `#Hidden`、扩展名、`#BG` 和 `#BG-A` 规则。只读 importer 检查确认其恢复 1 个标签且没有“布局尚未支持”警告。它检查状态文件保真，不表示 ExplorerBrowser 已显示颜色；Q-Dir 实际读取与重存仍待用户集中验收。浅深主题检查使用独立的最小 `qdr-color-baseline.qdr`。

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 于 2026-10-30 成功。manifest 源提交匹配、`SourceWorkingTreeDirty=false`、39 项待验收，EXE/DLL 和颜色夹具 SHA-256 已核对。准备流程未启动窗口、创建 ExplorerBrowser、发送输入、改动剪贴板或真实注册表。

候选仅供之后集中可见验收；准备成功不代表可见交互、Shell 菜单或真实 Q-Dir 互操作通过。旧副本保留在各自 `src/.state/gui-smoke/` 目录。
