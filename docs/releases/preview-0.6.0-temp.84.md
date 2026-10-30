# Explorer Dirs Preview 0.6.0 temp.84

## 本次变化

- F5、工具栏及窗格刷新按钮保留最后绘制的列表帧；目录监视、快速筛选、分组恢复、搜索返回等内部刷新不冻结旧画面。
- 查看方式切换后恢复文件视图焦点时，若 Shell 已报告原项目仍有焦点，跳过对同一项目的重复 `SelectItem`；焦点丢失或落在其它项目时仍执行修复。
- F5 与查看方式切换仍作用于当前 Shell 视图；F5 不重建窗格/标签/ExplorerBrowser，查看方式切换不重新导航或刷新目录。

## 验收边界

- App Release 发布构建成功，源码提交 `fc8d5e4cfd03086e88d37d568dc1835f6ad8cd52`，`SourceTreeState=clean`，`BuildTags=none`。
- EXE SHA-256：`393EB05C1317FD504A923E1690AB9883C0A269656BA47449BADD2DB0C16AB80B`。
- DLL SHA-256：`EAEB6758B41089C287F0D6B319B2E6371E5A010A0F601EDCECDFF77D3EFFF37F`。
- 未启动 GUI。F5 可见闪黑改善、保帧期间点击/键盘命中一致性、以及查看方式切换后的重绘和焦点体验仍待集中验收；本预览不宣称黑屏已修复。
- 使用独立 profile，不覆盖正式 profile。
