# Explorer Dirs Preview 0.6.0 temp.83

## 本次变化

- F5、工具栏与窗格刷新按钮请求在 Shell 刷新期间保留最后绘制的列表帧；行数稳定或达到 1.5 秒检查阈值后恢复绘制。
- 目录监视、快速筛选、分组恢复、搜索返回等程序内部刷新不再抑制列表重绘，避免自动更新期间显示旧画面。
- F5 仍刷新当前 Shell 视图，不重建窗格、标签或 ExplorerBrowser。查看方式切换仍在当前视图上设置模式，不重新导航或刷新目录。

## 验收边界

- App Release 发布构建成功，源码提交 `63bc718313b764762c093c3a41b1b158a959ac6c`，`SourceTreeState=clean`，`BuildTags=none`。
- EXE SHA-256：`398F89C764A16A7C56CE3328DE9D1C9F386EAEC0717D78FA1C72EB98F1E379D0`。
- DLL SHA-256：`1AB654E54E39C72DFF5C41D5146367AF83B697B47E41AAF3773987752BFD4254`。
- 未启动 GUI。F5 的可见闪黑改善、保留旧画面时的点击/键盘命中一致性仍待集中验收；查看方式切换的空白仍由 Shell 原生重排控制，本次没有宣称已解决。
- 使用独立 profile，不覆盖正式 profile。
