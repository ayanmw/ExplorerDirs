# Explorer Dirs 0.6.0 临时预览版 61

此包相较 temp.60 改进地址栏位置菜单：在“此电脑”下列出当前逻辑驱动器，本地固定盘优先显示卷标；网络盘不查询远程卷标，避免打开菜单时等待网络。驱动器导航和图标继续交由 Windows Shell。

## 本版变化

- 地址栏位置下拉继续按目录层级展开；“此电脑”子菜单动态枚举固定盘、可移动盘和映射盘。
- 已命名且就绪的本地固定盘显示卷标，例如“Windows (C:)”；未命名/不可读取时按驱动器类型显示名称。映射网络驱动器不访问远程卷标。
- 包位于 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.61`，入口为 `app\ExplorerDirs.App.exe`，设置使用独立 `app\profile\`，不使用 `src` 调试输出或日常 profile。

## 验证与限制

- 产品版本：`0.6.0-preview.temp.61+1fcc8a4d3db9549ee108d719e6634287559ede48`。
- EXE SHA-256：`A18429AFB3E89595B7D1823E3DF21CB0C4EDF23744CBC072AAC15E9B24DFD84B`；DLL SHA-256：`5F0BD2AC09FA059B85DCBCE3F0153E62C3BD90B4799E7CC9B9F190849190F3EB`，与包内 `app\SHA256.txt` 一致。
- 地址菜单定向 smoke 和完整 Release `SettingsSmoke` 通过。
- App Release 构建 0 警告、0 错误；发布隔离 smoke 通过。
- 未启动程序窗口、未操作桌面或 Q-Dir。菜单弹出、真实驱动器导航、映射盘断开表现和 Q-Dir 实际互操作仍待可见验收。
- 本版是源码/构建验证后的预览包，不代表完整 GUI 验收或项目路线图完成。
