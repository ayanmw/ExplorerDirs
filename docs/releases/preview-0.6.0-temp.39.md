# 临时预览 temp.39

## 本版内容

- 在选区快照匹配中加入 PIDL 二进制相等快速路径：`ILIsEqual` 精确匹配时跳过逐项 `IShellFolder.CompareIDs`；二进制不同时保留 `SHCIDS_CANONICALONLY` 回退，避免误把不同编码 PIDL 判成不同 Shell 项。
- NativeProbe 增加隐藏默认 `IShellView` 回归，直接使用生产 `ShellViewSelectionSnapshot` 验证未变化选区走精确路径、选区被识别为已恢复且跳过批量重选。
- 包含 temp.38 的原生列标题选项尝试。此功能仍待实际 ExplorerBrowser/GUI 验收。
- 使用独立 profile；不会覆盖 temp.38，也不会写入日常 profile。

## 已验证

- Release App、NativeProbe、HostProbe 构建通过，0 警告、0 错误。
- 完整 Release SettingsSmoke、隐藏 `--ishellview-color-spike` 探针和发布隔离 smoke 通过。
- 隐藏默认 `IShellView` 的选区 PIDL trace 为 `method=pidl-exact`、`selection-current matched=True`、`selection-restored=True`、`batch-skipped=True`；该单项匹配样本约 0.6 ms。父窗口隐藏，前台窗口未变。
- 程序集产品版本：`0.6.0-preview.temp.39+77495f5d72a4739c2bb33a21d021328294f728c0`。
- EXE SHA-256：`59E756D9B7E66FBE9EBDAB41816E096AB73AF2F3F7932291E48C32AE9FCD1C88`。
- DLL SHA-256：`5913D4D0A221502AEE2309B37E12FD489D2F68E0241F0CBA5754EF61FD5D42FF`。

## 待预览/验收

- 此样本未启动 ExplorerBrowser/HostProbe 生产宿主或可见 GUI，不证明 50,000 项压力下的 STA 性能提升，也不证明生产宿主的选区/焦点恢复。隐藏 `IShellView` 的焦点读回为 false，故不作为焦点恢复证据。
- 各查看方式的列标题、排序/排列/选择/焦点保持、右键菜单、实际文件操作及导入后颜色仍需实际预览。
- 这是隔离功能预览，不是 GUI 验收完成的正式版本。
