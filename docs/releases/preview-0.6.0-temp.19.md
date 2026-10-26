# Explorer Dirs 0.6.0 临时预览版 19

日期：2026-10-25  
基于：`preview-0.6.0-temp.18` 之后的当前源码  
用途：独立配置预览，不覆盖日常 profile

产品版本：`0.6.0-preview.temp.19+77495f5d72a4739c2bb33a21d021328294f728c0`  
EXE SHA-256：`5D1B48F9AFF4D280C6A1CEC3DDE90A77072DBF2A57E0BB658019C92B44645416`  
DLL SHA-256：`C81E9D9B3F024C50673CA0CC493B5CF4132F855DA79DB540EDB60F737AF6C852`

## 本版变化

- `.qdr` Explorer Dirs 扩展 v4 保存共享目录树和四个窗格的独立宽度。导入预览会列出五个数值；确认后经 `NavigationTreeWidths.ini` 的外部修改保护写入并应用。
- 旧扩展 v1～v3 仍可读取，且不会重置当前树宽度。缺少或越界的 v4 宽度会拒绝整份自有扩展，退回可识别的 Q-Dir 基础字段。
- 导出和导入前备份会携带当前树宽度。Q-Dir 原生 `TreeNsplit` 仍未映射，避免猜测其比例单位。
- 修正隔离预览打包遗漏：从现有 profile 复制 `NavigationTreeWidths.ini`，使新预览保留现有树宽度设置。

## 验证

- 聚焦 Q-Dir 导入导出 smoke：通过。
- 全量 Release `SettingsSmoke`：通过。
- App Release 构建：通过，0 警告、0 错误。
- `scripts/publish-smoke.ps1`：通过；该测试使用占位构建文件，不启动应用。
- 发布后核对 EXE/DLL SHA-256、产品版本和包内独立 profile。

没有启动此预览、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir。`.qdr` 导入预览的确认/取消、宽度的可见拖动与重启恢复、真实 Q-Dir 互操作仍待桌面验收。真实 Q-Dir `TreeNsplit` 单位未知，本版不会读取或写入该字段。
