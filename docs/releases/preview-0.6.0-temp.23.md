# Explorer Dirs `preview-0.6.0-temp.23`

此版本基于当前源码生成，使用独立 profile；保留 temp.22，不覆盖日常 profile。

## 相较 temp.22 的变化

- Shell 搜索范围支持文件系统目录、已识别 Shell 别名及本程序接受的绝对 `::{GUID}` 文件夹解析名。搜索结果恢复时保留原始虚拟范围身份。
- 新增无窗口搜索范围探针。当前机器的 Desktop、This PC、Recycle Bin 及 This PC GUID 均可解析为搜索文件夹；探针仅请求每个范围的首项，不代表完整结果或可见界面。

## 验证与边界

- Release `SettingsSmoke`、NativeProbe 虚拟范围探针、App/NativeProbe Release 构建及发布隔离 smoke 通过。
- 本包使用 `app/profile/` 独立配置目录。未启动本预览、ExplorerBrowser 或 Q-Dir；真实搜索结果、第三方 Shell 提供程序和可见菜单/焦点行为仍待验收。
- Desktop 与 Recycle Bin 在本机首项枚举为空；This PC 别名及 GUID 各枚举到一个条目。探针不枚举完整结果集。
