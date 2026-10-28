# 临时预览 temp.66

- 文件：`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.66\app\ExplorerDirs.App.exe`
- 产品版本：`0.6.0-preview.temp.66+aaa7b6a3f6e4234532313dad077b284119b18ffd`
- 本版修复：原生左侧目录树未提供自定义图标索引时，回调改为 `E_NOTIMPL`，交由 NamespaceTreeControl 使用默认 Shell 图标解析；增加目录树图标回归检查。
- EXE SHA-256：`EACFCAD4AD68F39D082DEF92D8C453D5BF782C70D39D19C83CC157CE35A70CDE`
- DLL SHA-256：`FD1193CBD1C56AD3936AE5A3C30C9853339966ADE8DB61FC34EEB85D776E1D62`
- 验证：完整 Release `SettingsSmoke`、App Release publish、发布隔离 smoke 通过；EXE/DLL 哈希与包内 `SHA256.txt` 一致。
- 验收边界：未启动预览程序；目录树图标可见效果、虚拟 Shell 节点和多窗格界面仍待实际预览确认。使用 `app/profile/` 内的独立 profile，不覆盖日常配置。
