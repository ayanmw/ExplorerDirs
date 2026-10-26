# Explorer Dirs 0.6.0 临时预览版 59

本版是隔离试用包，供预览当前工具栏查看方式和地址位置下拉改动。项目整体路线图仍在进行中；此包未完成可见 GUI 或 Q-Dir 互操作验收。

## 本版变化

- 每个窗格导航工具栏增加查看方式按钮，复用现有 Shell 原生查看方式菜单；状态栏入口继续保留。
- 地址栏位置下拉列出当前路径及所有上级，再列桌面、用户文件夹、下载、文档、此电脑、库、网络、控制面板和回收站。地址历史保持独立按钮。
- 位置下拉进入窗格 Tab 顺序；现代地址栏保留独立编辑按钮，经典地址栏仍可点击编辑路径。
- 菜单路径模型和焦点顺序增加纯状态 smoke 覆盖。

## 验证范围与限制

- `--address-path-menu-smoke` 通过。
- `--focus-traversal-smoke` 通过。
- App Release build 通过，0 警告、0 错误。
- 首次完整 SettingsSmoke 在既有 Environment message smoke 的“隐藏消息目标不得改变前台窗口”断言处失败；随后该 smoke 定向复跑通过，完整 Release SettingsSmoke 再次运行也通过。首次失败未能复现，不能据此归因代码缺陷。
- `scripts/publish-smoke.ps1` 通过；该 smoke 使用虚构 profile/程序文件，不创建真实产品窗口。
- 发布后核对程序版本 `0.6.0-preview.temp.59+77495f5d72a4739c2bb33a21d021328294f728c0`；EXE SHA-256 `C561B3B50F509760F823B0C71789570905CEB0E91414310DD8D9EC459770A7A7`，DLL SHA-256 `BFA91F50B668D370C9D62786EA6350F62989941FC25ECC450F450B9432EF0486`，均与包内清单一致；确认独立 profile 与包内说明存在。
- 未启动应用、未操作 GUI。菜单布局、系统图标、路径导航、工具栏空间、多窗格可见焦点以及 Q-Dir 实际互操作仍待桌面验收。
- 项目路线图仍开放，包括自动刷新稳定性 P0、Shell 操作与焦点的真实窗口验收、Q-Dir `.qdr` 互操作及其他未完成差距。

从 `app/ExplorerDirs.App.exe` 启动。预览使用 `app/profile/` 独立配置，不写入日常 profile；不要把 `src/` 下的调试输出当作日常版本。
