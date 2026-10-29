# 2026-10-30 主线集中验收候选（最新）

这是后续集中可见验收用的隔离副本，不是发布版本，也没有启动窗口。候选包含 Q-Dir 颜色配置的会话保真功能和专门的验收夹具。

## 固定身份

- 源提交：`f5ba9b19e2265fafc7561ef8bfbfb553837c85f9`，准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261029-224830-914-6bbc2a/`。
- BuildIdentity：`gui-acceptance.20261029-224830-914-6bbc2a+f5ba9b19e2265fafc7561ef8bfbfb553837c85f9`。
- 状态：`prepared-not-run`，37 项待验收。
- EXE SHA-256：`C8791A3A19BBB035FD407A3AB8B718F97280AC09E418380A780C0EEDEC48C469`。
- App DLL SHA-256：`B474C648B316463EAD9E58CE08A3F5BD0EBCABD8753B2EC4C73875709B14F55A`。
- 颜色配置夹具 `qdr-color-metadata.qdr` SHA-256：`70D8C32A0F9B0831A4AD060FAA87A982B7E077DD4791A2F375023D6BF9AC8D34`。
- 初始布局为四窗格，使用副本自己的 profile 和隔离文件夹；包含 10,000 个递归大小测试文件及跨窗格复制/移动样本。

## 本次更新与验证范围

验收清单覆盖 `Ctrl+Shift+C`、`Alt+K`/`Alt+V`、F7 路径层级、F8/“此电脑”按钮和地址栏 Tab 顺序，以及标签栏“使用 Explorer 浏览”。Q-Dir 树分隔值仅作不透明字段往返，不代表 DIP 树宽度。

新增的颜色元数据步骤会导入 `qdr-color-metadata.qdr`，检查预览说明“保留但未应用”，再导出到新文件并逐行比对 `useColor`、`useTreeColor`、`colorthema` 和 `[Filter2]` 规则顺序。夹具明确指定 Q-Dir 四窗格布局，包含 `#Hidden`、扩展名、`#BG` 和 `#BG-A` 规则。只读 importer 检查确认其恢复 1 个标签且没有“布局尚未支持”警告。它检查状态文件保真，不表示 ExplorerBrowser 已显示颜色；Q-Dir 实际读取与重存仍待用户集中验收。用于浅深主题检查的 `qdr-color-baseline.qdr` 继续保持最小布局，不与颜色规则夹具混用。

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功，App Release 发布成功。独立核对确认 manifest 源提交匹配、`SourceWorkingTreeDirty=false`、37 项待验收及 EXE/DLL 哈希匹配；颜色配置夹具哈希匹配，ExplorerDirs 进程数为 0。准备流程未启动窗口、创建 ExplorerBrowser、发送输入、改动剪贴板或真实注册表。

候选仅供之后集中可见验收；准备成功不代表可见交互、Shell 菜单或真实 Q-Dir 互操作通过。旧副本仍保留在各自 `src/.state/gui-smoke/` 目录。
