# Explorer Dirs Preview 0.6.0 temp.86

## 2026-11-02 入口回归修正版

- F5 撤回目录广播通知，改为直接调用当前 Shell 视图 Refresh；保持原生绘制及选区/焦点恢复。实际速度仍待用户以 temp.80 对照验收。
- 地址下箭头恢复路径菜单，F4 保留目录树；收藏夹补初始子项以允许动态子菜单展开。
- 用户已确认此前 temp.86 查看方式切换快速且无明显延迟，本轮保留该实现。
- App Release 构建与定向刷新合同检查通过。完整 SettingsSmoke 曾因前台变化守卫中止，不记为通过；未启动 GUI。
- 同目录覆盖时保留现有 profile，旧载荷备份到 releases/.history。新包身份及哈希见包内 RELEASE-NOTES.md 和 app/SHA256.txt；下方构建身份属于首次发布历史。
- 本批仅交付入口回归修正；常用位置/两级收藏夹/标签行整理、Everything 数据源及空间分析仍在 temp.86 目标中，尚未完成。详见 ../temp86-goals.md。

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

## 首次发布构建身份（历史）

- 源提交：`2e6214e836eb321abd0d4228be2f23c5c6122448`。
- 配置：Release，`BuildTags=none`，隔离 profile。
- 程序集信息版本：`0.6.0-preview.temp.86+2e6214e836eb321abd0d4228be2f23c5c6122448`。
- Git 提交时间：`2026-11-02T10:10:39Z`。
- EXE SHA-256：`F1B23EC4ACAD3752A5EBA3401E184B44860E3EAD6A29909ACD76198FF7E1B06D`。
- App DLL SHA-256：`29D166C5EB4DC9A11B3D1E73EFE9A0487F78EF1A4FE83254C574FC7807685EC4`。
- Shell adapter SHA-256：`27AAF5F32B8085A533E066917EB81058EF41E13EC012FA5CB109B0A4E77C1AC1`。
- 三项哈希均与包内 `app/SHA256.txt` 一致；BuildIdentity smoke 通过。

2026-11-02 回归修正版已覆盖 temp.86：源提交 74335ebb559423246b3f886f99866d49374f3ed6，EXE SHA256 F97B8A1675F4051D2AAB573BD75B87C628F7DC194ABFAC8D9BDC134C80DDBD99，App DLL SHA256 7D08BB0C83FA4F46A38FF35614A3952A51BAC9DEBE9CEF50F331972EB9D3D7BB。BuildIdentity smoke 通过，profile 原样保留；旧包备份 releases/.history/temp86-before-74335eb-20261102-122914。未做 GUI 验收。