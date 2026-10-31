# Explorer Dirs Preview 0.6.0 temp.85

## 本次变化

- 查看方式切换时，仅在同步原生 Shell 模式切换调用周围短暂暂停视图重绘，并在调用结束后立即恢复，让 Shell 对最终布局绘制。
- F5 刷新路径不变，仍由当前 Shell 视图自行重枚举和绘制；没有冻结异步刷新期间的旧画面。

## 构建身份

- 源提交：`a17639b69a9fd143c6f862cc5821bea73073c1f7`。
- 配置：Release，`BuildTags=none`，隔离 profile。
- EXE SHA-256：`D45CDF9320989A0002321A2C13047F6D0309BA19A0D7E15D387538B41AFF6564`。
- DLL SHA-256：`C1B99997D744574CD44EAB2A7DC8ED561CAECA2A1E4029BB4C6F85BEB6AE319F`。
- 包内 `app/SHA256.txt` 与上述哈希一致；BuildIdentity smoke 通过。

## 验收边界

- 针对性重绘源码合同、完整 Release SettingsSmoke 与 App Release 构建通过；构建 0 警告、0 错误。
- 未启动 GUI。查看方式切换的实际闪烁/空白是否改善仍待集中验收；本预览不宣称黑屏已修复。
- 使用独立 profile，不覆盖正式 profile。
