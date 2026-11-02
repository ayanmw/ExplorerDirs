# Explorer Dirs Preview 0.6.0 temp.86

## 相较 temp.85 的变化

- 推进独立递归大小列原型：按需后台扫描、有限缓存、多根目录/多窗口身份隔离、会话恢复，以及排序时选区与焦点恢复。开发者菜单中的原型仍以单独标签运行，不会默认替换普通文件夹视图。
- 补充 Q-Dir `.qdr` 原生视图流和 provider 路径身份的导入/导出验证，覆盖排序、列顺序、虚拟 Shell 位置及独立预览 profile 的身份改写。
- 加固 profile Shell namespace 的注册/清理生命周期；遇到外部注册表修改时只清理确认归本程序所有的内容。
- 增加四窗格大目录排序压力探针和低开销/高开销通知身份采集对照。近期对照显示开启诊断身份采集时出现一次排序未收敛；这属于诊断探针结果，尚不能归因于正式产品 adapter。

## 已验证

- App Release 构建通过，0 警告、0 错误。
- 完整 Release `SettingsSmoke` 通过。
- 发布隔离 smoke 通过；该脚本使用 fixture payload，不会覆盖真实 release，也不会启动 GUI。
- 使用独立 profile，从 temp.85 的 profile 副本生成；发布脚本会为本预览分配独立 Shell provider 身份，源 profile 不应被更改。

## 尚未验收与限制

- 未启动本预览 GUI。深浅主题、文件区菜单、F5/查看方式重绘、焦点及文件操作仍需后续集中可见验收。
- 递归大小列仍是独立实验原型，未接入普通物理文件夹浏览；4×1,025 行诊断 adapter 的排序稳定性仍待复测，当前不承诺大列表递归排序已完成。
- Q-Dir `.qdr` 的实际第三方 GUI 导入/导出互操作尚未验证。
- 未验证慢盘、网络/云目录、长期内存增长或产品 GUI 下多窗口会话恢复。

## 构建身份

- 源提交：`2e6214e836eb321abd0d4228be2f23c5c6122448`。
- 配置：Release，`BuildTags=none`，隔离 profile。
- 程序集信息版本：`0.6.0-preview.temp.86+2e6214e836eb321abd0d4228be2f23c5c6122448`。
- Git 提交时间：`2026-11-02T10:10:39Z`。
- EXE SHA-256：`F1B23EC4ACAD3752A5EBA3401E184B44860E3EAD6A29909ACD76198FF7E1B06D`。
- App DLL SHA-256：`29D166C5EB4DC9A11B3D1E73EFE9A0487F78EF1A4FE83254C574FC7807685EC4`。
- Shell adapter SHA-256：`27AAF5F32B8085A533E066917EB81058EF41E13EC012FA5CB109B0A4E77C1AC1`。
- 三项哈希均与包内 `app/SHA256.txt` 一致；BuildIdentity smoke 通过。
