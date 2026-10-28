# 临时预览 temp.67

- 目标：修复 temp.66 中左侧 Shell 目录树图标空白。
- 修复内容：`OnGetDefaultIconIndex` 按 Shell 绝对解析名通过 `SHGetFileInfo` 返回 Windows 系统图像列表中的普通/展开图标索引；无法解析时保留 Shell 默认处理回退。
- 产品版本：`0.6.0-preview.temp.67+8dd46e8564f47fd1a82d25e9cfc9986b0ac5ecc2`。
- SHA-256：EXE `BA32C9F7BDF1E98718A5E83518B35499E438BEFFD51E02FD6E94AB5F3BFA600D`；DLL `987C1236F9B36C13D4A30CB6876E39B95DF2F99E34E0E4D5F98FA358B34365D3`；与包内 `SHA256.txt` 一致。
- 验证：完整 Release `SettingsSmoke` 通过；App Release build 通过，0 警告、0 错误；`scripts/publish-smoke.ps1` 通过（仅 fixture payload）。
- 验收边界：未启动 GUI。普通文件夹、驱动器和虚拟 Shell 项的图标绘制仍需打开此预览后检查。
- 程序与独立 profile 位于 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.67\app\`。
