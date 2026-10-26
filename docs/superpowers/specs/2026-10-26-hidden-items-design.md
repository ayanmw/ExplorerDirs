# 隐藏项目显示偏好设计

状态：按路线图实现中；尚未完成 ExplorerBrowser 可见验收。

## 目标与边界

在 Explorer Dirs 的所有窗格与标签中提供“跟随 Windows / 显示隐藏项目 / 隐藏项目”三态偏好。默认跟随 Windows；显式选择写入 profile 下独立便携 `HiddenItems.ini`。此设置是应用级，不改变 Windows 用户设置，也不包含 `SHCONTF_INCLUDESUPERHIDDEN` 保护系统项目。

## 方案

采用现有 `IFolderFilterSite`/`ShellFolderFilter.GetEnumFlags`，只在显式覆盖或快速筛选需要时挂载过滤器。跟随 Windows 时，快速筛选继续查询 `SHGetSettings(SSF_SHOWALLOBJECTS)`；显式显示/隐藏覆盖该视图的枚举标志。保留 ExplorerBrowser 原生视图与文件枚举，不做文件系统自建列表。

## 交互与持久化

“视图”菜单新增三项互斥选项，选择后应用到所有现存标签并刷新各视图。配置使用版本化 INI、外部修改保护及未来格式保护；默认值为跟随 Windows。独立发布包需按现有白名单原样复制该文件。

## 验收

纯逻辑测试覆盖配置往返/损坏保护/外部编辑冲突、系统默认解析、普通隐藏项目枚举和绝不请求受保护系统项目。Release `SettingsSmoke`、App build、发布 profile smoke 必须通过。可见 ExplorerBrowser 验收仍需确认文件系统目录下普通隐藏项显示/隐藏、受保护系统项始终不显示、筛选共存、导航/刷新和多标签一致性；纯逻辑与构建不能替代此验收。
