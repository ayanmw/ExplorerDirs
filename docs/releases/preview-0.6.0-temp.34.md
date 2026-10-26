# Explorer Dirs 0.6.0 临时预览版 temp.34

本版基于当前源码，独立配置目录位于 `app/profile/`。

## 变化

- CSV 和制表符 TXT 导出会为公式样式单元格加前导 Tab，避免文件名或属性文本被电子表格软件当作公式执行。为适配 Excel，打开后单元格原始值可能包含额外的 Tab；XLS 导出仍将内容保存为字符串，不改原值。

## 验证

- 新回归先在旧实现上失败，修复后 Release SettingsSmoke 全套通过。
- App Release 构建通过，0 警告、0 错误。
- 发布隔离 smoke 通过；EXE SHA-256 为 `DD69BB7D4489F541DD8EADB0518CBE83AE05FD0E88C106B012B00878AC8141C7`，DLL SHA-256 为 `4CE90EC259DAD212CB875B0D6A01344B47A8A11FE28CDD02E9D9FA748E73AB3B`，与包内清单一致。产品版本 `0.6.0-preview.temp.34+77495f5d72a4739c2bb33a21d021328294f728c0`。
- 未启动 GUI 或 Excel。电子表格软件实际显示、保存后重开及其他应用的公式处理未验收。
