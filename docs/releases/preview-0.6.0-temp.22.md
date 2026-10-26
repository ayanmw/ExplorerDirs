# Explorer Dirs `preview-0.6.0-temp.22`

此版本基于当前源码生成，使用独立 profile；保留 temp.21，不覆盖日常 profile。

## 相较 temp.21 的变化

- “工具 → 打开崩溃诊断目录”可直接打开当前 profile 下的 `crash-logs` 文件夹，便于查看托管异常报告和上次异常退出记录。
- 菜单只访问本程序 profile 中已有的诊断目录；不会启用或修改 Windows Error Reporting 设置。

## 验证与边界

- App Release 构建和 Release `SettingsSmoke` 通过；本次未增加菜单行为 smoke。
- 本包使用 `app/profile/` 独立配置目录。未启动本预览、ExplorerBrowser 或 Q-Dir；实际菜单点击仍待桌面验收。
- 托管日志不记录异常消息、浏览路径或环境变量；异常退出标记不能判定退出原因。原生 Shell 扩展崩溃仍需独立 WER 转储或其他可分析证据，不能由此菜单功能解决。
