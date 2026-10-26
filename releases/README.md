# 可使用版本

## 最新临时预览包（2026-10-26）

`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.67\app\ExplorerDirs.App.exe`，使用隔离 profile。针对 temp.66 左侧目录树图标空白，现由 Shell 回调显式提供系统关闭/展开图标索引。产品版本 `0.6.0-preview.temp.67+8dd46e8564f47fd1a82d25e9cfc9986b0ac5ecc2`，EXE SHA-256 `BA32C9F7BDF1E98718A5E83518B35499E438BEFFD51E02FD6E94AB5F3BFA600D`，DLL SHA-256 `987C1236F9B36C13D4A30CB6876E39B95DF2F99E34E0E4D5F98FA358B34365D3`，与包内清单一致。Release `SettingsSmoke`、App Release build 和发布隔离 smoke 通过。没有启动 GUI；请重点检查普通文件夹、驱动器和虚拟 Shell 节点的图标。详见[temp.67 说明](../docs/releases/preview-0.6.0-temp.67.md)。

## 最新临时预览包（2026-10-26）

`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.66\app\ExplorerDirs.App.exe`，使用隔离 profile。修复原生左侧目录树未提供自定义图标索引时的默认 Shell 图标回退。产品版本 `0.6.0-preview.temp.66+aaa7b6a3f6e4234532313dad077b284119b18ffd`，EXE SHA-256 `EACFCAD4AD68F39D082DEF92D8C453D5BF782C70D39D19C83CC157CE35A70CDE`，DLL SHA-256 `FD1193CBD1C56AD3936AE5A3C30C9853339966ADE8DB61FC34EEB85D776E1D62`，与包内清单一致。完整 Release `SettingsSmoke`、App Release publish 和发布隔离 smoke 通过。未启动 GUI；目录树图标可见效果、虚拟 Shell 节点及多窗格体验仍待预览验收。详见[temp.66 说明](../docs/releases/preview-0.6.0-temp.66.md)及包内 `RELEASE-NOTES.md`。

## 最新临时预览包（2026-10-26）

`preview-0.6.0-temp.64/app/ExplorerDirs.App.exe`，使用独立 profile。新增 System/Light/Dark 之外独立的自定义强调色输入，以及 Windows 11 DWM 标题栏、标题文字和边框主题同步；高对比度恢复系统默认色。产品版本 `0.6.0-preview.temp.64+b16b394881d3146c826b5ca1272cf188aa0a41bb`，EXE SHA-256 `AB14A78944F6762CCC087E6DE845A3E5CDC06907429AB350B419C64C1A279110`，DLL SHA-256 `7B1D7BC4543A76ACF95FA7D1A4DAB1742EEBB337A6D098EAA71CEDD49C80B61B`，与包内清单一致。完整 Release `SettingsSmoke`、App Release publish 与发布隔离 smoke 通过；profile 文件哈希与来源一致。没有启动 GUI；主题对话框、系统切色、标题栏外观、高对比度及 Shell 文件区深色仍待桌面验收。详见[temp.64 说明](../docs/releases/preview-0.6.0-temp.64.md)及包内 `RELEASE-NOTES.md`。

## 此前临时预览包（2026-10-26）

`preview-0.6.0-temp.63/app/ExplorerDirs.App.exe`，使用独立 profile。相较 temp.61，照片打印窗口改用 Windows 动态系统颜色，当前标签增加系统强调色底边；同时修复隔离 worktree 发布时未从主项目 profile 复制现有设置的问题。产品版本 `0.6.0-preview.temp.63+fb48e6c4cf819c8d79ce20ac09f44243edd01172`，EXE SHA-256 `D579B06A362D9E89E721EA4DD13B3EDBE9C5E2244471665330F88C5927105A5E`，DLL SHA-256 `04C833116CD902ACCAFD9D5316C5185624703986601ADA1DA29A636CBAB2C6C2`，与包内清单一致。全量 Release `SettingsSmoke`、App Release 构建与发布隔离 smoke 通过。未启动 GUI；多窗格视觉、系统主题变化、高对比度、Shell 文件区深色和 Q-Dir 颜色互操作仍待验收。

## 此前临时预览包（2026-10-26）

`preview-0.6.0-temp.62/app/ExplorerDirs.App.exe`，保留作较早的临时包。该包包含照片打印窗口系统色和当前标签强调色，但由隔离 worktree 发布时其 `app/profile/` 没有从主项目复制现存配置；请使用 temp.64。没有启动 GUI。

## 此前临时预览包（2026-10-26）

`preview-0.6.0-temp.61/app/ExplorerDirs.App.exe`，使用独立 profile。相较 temp.60，在“此电脑”地址菜单中动态列出逻辑驱动器，并为就绪的本地固定盘显示卷标；网络盘不会触发远程卷标读取。产品版本 `0.6.0-preview.temp.61+1fcc8a4d3db9549ee108d719e6634287559ede48`，EXE SHA-256 `A18429AFB3E89595B7D1823E3DF21CB0C4EDF23744CBC072AAC15E9B24DFD84B`，DLL SHA-256 `5F0BD2AC09FA059B85DCBCE3F0153E62C3BD90B4799E7CC9B9F190849190F3EB`，与包内清单一致。地址菜单定向 smoke、完整 Release `SettingsSmoke`、App Release 构建和发布隔离 smoke 通过。没有启动 GUI；真实菜单交互、驱动器导航和 Q-Dir 互操作仍待桌面验收。详见[第六十一版说明](../docs/releases/preview-0.6.0-temp.61.md)及包内 `RELEASE-NOTES.md`。

## 此前临时预览包（2026-10-26）

`preview-0.6.0-temp.60/app/ExplorerDirs.App.exe`，使用独立 profile。相较 temp.59，地址栏上级菜单改为可展开祖先目录树；增加 Windows 强调色基础接线和活动窗格强调色边框。产品版本 `0.6.0-preview.temp.60+80d2bbd147ac4d248e5554e509692c0f9c432f3b`，EXE SHA-256 `58BED64B44B2F66A8930A265B9200EC2DB78EE7EB41B5A33322974B2220DE258`，DLL SHA-256 `346D9CF4187750E28416F99E82338FB760D089D9D79DC843C253A867926A4F75`，与包内清单一致。Release SettingsSmoke、App Release 构建与发布隔离 smoke 通过。没有启动 GUI；菜单实际交互、系统切色和 Q-Dir 实际互操作仍待验收。详见[第六十版说明](../docs/releases/preview-0.6.0-temp.60.md)及包内 `RELEASE-NOTES.md`。

## 最新临时预览包（2026-10-26）

`preview-0.6.0-temp.59/app/ExplorerDirs.App.exe`，使用独立 profile。新增每窗格查看方式工具栏入口，以及列出当前路径上级和常用 Shell 位置的地址下拉；编辑与地址历史入口保持独立。产品版本 `0.6.0-preview.temp.59+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `C561B3B50F509760F823B0C71789570905CEB0E91414310DD8D9EC459770A7A7`，DLL SHA-256 `BFA91F50B668D370C9D62786EA6350F62989941FC25ECC450F450B9432EF0486`，与包内清单一致。路径菜单/焦点 smoke、App Release 构建、发布隔离 smoke 和重跑后的完整 Release SettingsSmoke 均通过；首次环境消息前台窗口断言失败未能复现。未启动 GUI；菜单和真实 Shell 导航仍待可见验收，路线图尚未全部完成。详见[第五十九版说明](../docs/releases/preview-0.6.0-temp.59.md)及包内 `RELEASE-NOTES.md`。

## 最新临时预览包（2026-10-26）

`preview-0.6.0-temp.58/app/ExplorerDirs.App.exe`，使用独立 profile。相较 temp.57，成功映射/断开网络驱动器后会通知 Shell 更新“此电脑”视图；取消/失败不发送通知。Release SettingsSmoke、App Release 构建及 `publish-smoke.ps1` 通过；产品版本 `0.6.0-preview.temp.58+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `66661E45FC3F981E4858EB016447F7C937E1321E0292DD75C82778E06B677A15`，DLL SHA-256 `FE0ECEEAF2F4CB12687D43AFC851333D2F2E757ACFBDE525955C95EFA79D5541`，与包内清单一致。没有启动发布程序或真实 GUI，Shell 可见刷新和其它交互仍待桌面验收。详见[第五十八版说明](../docs/releases/preview-0.6.0-temp.58.md)。

## 最新临时预览包（2026-10-26）

`preview-0.6.0-temp.57/app/ExplorerDirs.App.exe`，使用独立 profile。相较 temp.56，汇总 10 月 26 日的多窗格焦点/快捷键、标签与会话、Q-Dir `.qdr` 和 Program Starter 导入、目录监视器恢复等源码更新；颜色能力仍只在隔离 `IShellView` 原型中，不接入本版产品视图。Release SettingsSmoke、App Release 构建和 `publish-smoke.ps1` 均通过；程序集产品版本 `0.6.0-preview.temp.57+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `FF678780BE08FF6F2025B74E2B69984ED402ABD8CD50965F78C8B0F1266D4DF6`，DLL SHA-256 `561C146AC31EE995590801DF282EFEBD529F8CD6B07BA2A4DD1CFC921BE9BE07`，与包内清单一致。没有启动发布 EXE；文件操作、真实右键目标、焦点、布局和 Q-Dir 可见互操作仍待桌面验收。详见[第五十七版说明](../docs/releases/preview-0.6.0-temp.57.md)。

## 最新临时预览包（2026-10-25）

`preview-0.6.0-temp.56/app/ExplorerDirs.App.exe`，独立 profile。在 temp.55 基础上接入详细视图 `Ctrl+数字小键盘+` 原生列宽自适应转发；仅 Shell 加速器返回 `S_FALSE` 时交给原生 ListView。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；程序集产品版本 `0.6.0-preview.temp.56+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `F7AD0ADDC11C447239F3A7DAB3AA8CDB1BD3E7FBD833A15D2C7A6E6E9D106539`，DLL SHA-256 `E765F26FC3D451F42A774FE1B08D15C6F4B8B22D42503ADB3F7073B7AA6090B3`，与包内清单一致。没有启动发布 EXE；数字小键盘、可见列宽变化、焦点及 Shell 操作仍待桌面验收。详见[第五十六版说明](../docs/releases/preview-0.6.0-temp.56.md)。

## 最新临时预览包（2026-10-25）

`preview-0.6.0-temp.55/app/ExplorerDirs.App.exe`，独立 profile。新增全局大小显示单位/精度/千位分隔设置，以及 F9 递归汇总数量开关；发布脚本会将 `SizeDisplay.ini` 一并带入独立 profile。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；程序集产品版本 `0.6.0-preview.temp.55+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `1136B7419B1E0BC3C7B9772F89E580A732036D7A509AAE76EC0C90F631C97F51`，DLL SHA-256 `E999177E63C3446A9F86AB555BB98FAA197FC394F84E32533B23C6AC85531977`，与包内清单一致。没有启动 GUI；菜单、F9 状态栏和实际 Shell 行为仍待可见验收。详见[第五十五版说明](../docs/releases/preview-0.6.0-temp.55.md)。

## 最新临时预览包（2026-10-25）

`preview-0.6.0-temp.54/app/ExplorerDirs.App.exe`，独立 profile。F9 递归目录扫描会按 256 项采样进度，状态栏最多每 250 ms 更新一次；F10 打开主菜单栏“文件”菜单；隔离焦点 trace 增加 F2 箭头转发目标记录。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；产品版本 `0.6.0-preview.temp.54+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `CC1C17D1635E5B122886F539B5313BEC84E25CB3AFB8CB66246E13F78DFC8D6D`，DLL SHA-256 `55BFD200DD5912669A1311C0E9F81BDC8D23EA5212A077B7B0AFC0D99C79AB42`，与包内清单一致。没有启动 GUI；F9 进度/取消、F10 菜单焦点、多窗格 F2 和其它可见 Shell 行为仍待桌面验收。详见[第五十四版说明](../docs/releases/preview-0.6.0-temp.54.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.53/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.52，刷新回调连续读到空视图后，若列表恢复为稳定非零但计数仍不匹配，会等待短暂安定后按实际 Shell 数量重建基准，避免重复刷新循环。完整 Release SettingsSmoke、隔离生产文件操作宿主探针和发布隔离 smoke 通过；程序集产品版本 `0.6.0-preview.temp.53+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `EB58BADF253BD3B6D9D767DBB80922F4B86DC76B810193419202F64D1BBFEA20`，DLL SHA-256 `12261EBB94D9E46BEF1388AD9FBEE63F9D0AA68AACE9E289D148AFB425AB96DB`，与包内清单一致。没有启动 GUI；真实 Ctrl+V、可见刷新时序和物理键盘焦点仍待桌面验收。详见[第五十三版说明](../docs/releases/preview-0.6.0-temp.53.md)。

## 更早的临时预览包（2026-10-25）

`preview-0.6.0-temp.52/app/ExplorerDirs.App.exe`，独立 profile。焦点恢复会校验非空目录中的 Shell focused item；若无有效 focused item，会回退至有效 selection mark 并保持选中状态。完整 Release SettingsSmoke 和 App Release 构建通过；产品版本 `0.6.0-preview.temp.52+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL 哈希与包内清单一致。详见[第五十二版说明](../docs/releases/preview-0.6.0-temp.52.md)。

## 更早的临时预览包（2026-10-25）

`preview-0.6.0-temp.51/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.50，文件区原生列表命中测试不可用时，不再从可能覆盖整行的 UIA `ListItem` 边界猜测右键目标；目标未确认时保留完整 Shell 菜单且不插入自定义项。Release SettingsSmoke、App Release 构建及发布隔离 smoke 通过；程序集产品版本 `0.6.0-preview.temp.51+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `B09BED7F936E532278901393A6329CA00E61BE9C9F9DC455D0A4D6973EA01C59`，DLL SHA-256 `53CA21EA9613C5008BA073BF920DF1F87A807FDCB9321CC0CFCD1BC95BA27674`，与包内清单一致。没有启动 GUI；真实项目/空白处命中和菜单体验仍待桌面验收。详见[第五十一版说明](../docs/releases/preview-0.6.0-temp.51.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.50/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.49，更新当前源码中的 `.qdr` 查看方式边界与导出兼容处理、目录变化刷新协调和 Shell 操作焦点恢复等功能。Release SettingsSmoke、App Release 构建与发布隔离 smoke 通过；程序集产品版本 `0.6.0-preview.temp.50+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `29CF860E0149350494C6BEC11158DF0947378FE2E114B40C68548357A7CB475D`，DLL SHA-256 `DDB649CBEF8CEC468184357ABD9280FDF58402B758DD59A0452A3993B375E9DC`，与包内清单一致。没有启动 GUI；真实菜单、鼠标/键盘焦点、可见主题、自动刷新和 Q-Dir 互操作仍待桌面验收。详见[第五十版说明](../docs/releases/preview-0.6.0-temp.50.md)。

## 最新临时预览包（2026-10-25）

`preview-0.6.0-temp.49/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.48，预览打包现在会复制已存在的 `Theme.ini`，保留跟随系统/浅色/深色主题偏好；包含 temp.48 的 Ctrl+滚轮失败回退及 `ViewPresets.ini` 复制修正。发布隔离 smoke、App Release 构建通过，包内版本及 EXE/DLL SHA-256 已核对。没有启动 GUI；主题在原生 Shell 文件区、物理滚轮、焦点/选区和 Q-Dir 互操作仍待桌面验收。详见[第四十九版说明](../docs/releases/preview-0.6.0-temp.49.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.48/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.47，Ctrl+滚轮切换查看方式失败时会把已累计的完整刻度交回 Shell，避免输入被吞；隔离预览打包会复制已存在的 `ViewPresets.ini`。Release SettingsSmoke、App Release 构建、发布隔离 smoke 通过，包内版本及 EXE/DLL SHA-256 已核对。没有启动 GUI；物理鼠标路由、Shell 失败时后续行为、焦点/选区和 Q-Dir 互操作仍待桌面验收。详见[第四十八版说明](../docs/releases/preview-0.6.0-temp.48.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.47/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.46，加入文件操作后的焦点恢复门控，并用原生 `SysListView32` 命中标志区分文件项内容与行尾空白右键目标。Release SettingsSmoke、App/NativeProbe Release 构建、隔离原生选择恢复探针与发布隔离 smoke 通过；包内产品版本及 EXE/DLL SHA-256 已核对。没有启动 GUI、ExplorerBrowser 或 Q-Dir，真实文件区焦点、右键目标及 Q-Dir 互操作仍待桌面验收。详见[第四十七版说明](../docs/releases/preview-0.6.0-temp.47.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.46/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.45，状态栏统计还会在普通导航完成、搜索结果就绪和导航失败恢复视图后合并刷新；既有选区变化与标签切换更新继续保留。Release SettingsSmoke、App 发布构建和发布隔离 smoke 通过；未启动 GUI，导航/搜索后的真实显示仍待桌面验收。详见[第四十六版说明](../docs/releases/preview-0.6.0-temp.46.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.45/app/ExplorerDirs.App.exe`，独立 profile。状态栏在当前可见标签的 Shell 选区变化后会合并刷新项目数、选中数和可取得的选中项大小；标签切换也会排队刷新。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；未启动 GUI，真实选择后的状态更新仍待桌面验收。详见[第四十五版说明](../docs/releases/preview-0.6.0-temp.45.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.44/app/ExplorerDirs.App.exe`，独立 profile。新增 Ctrl+右键列标题打开 Q-Dir 风格命名视图预设菜单，普通 Shell 右键菜单仍保持原生路径。Release SettingsSmoke、发布隔离 smoke 和 App 发布构建通过；未启动 GUI，列标题命中、菜单交互及预设保存/应用仍待桌面验收。详见[第四十四版说明](../docs/releases/preview-0.6.0-temp.44.md)。
## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.43/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.42，包含当前源码中已完成的 Q-Dir 视图模式范围修正、导入字段提示、十二布局树映射回归、Shell 刷新与焦点/选区处理，以及地址栏和虚拟位置功能更新。Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建、隐藏颜色原型和发布隔离 smoke 通过，0 警告、0 错误。没有启动可见 GUI 或 Q-Dir；行着色、菜单与焦点体验及实际互操作仍待验收。程序集产品版本为 `0.6.0-preview.temp.43+77495f5d72a4739c2bb33a21d021328294f728c0`。详见[第四十三版说明](../docs/releases/preview-0.6.0-temp.43.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.42/app/ExplorerDirs.App.exe`，独立 profile。修正文件操作后列标题焦点被误判为文件内容焦点的问题，增加文件项/列标题/其它 Shell 子窗口的焦点策略回归。App、HostProbe、NativeProbe Release 构建、完整 Release SettingsSmoke 与发布隔离 smoke 通过，0 警告、0 错误。没有启动 ExplorerBrowser 宿主或可见 GUI；列标题焦点迁移、文件操作后方向键及多窗格体验仍待验收。详见[第四十二版说明](../docs/releases/preview-0.6.0-temp.42.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.41/app/ExplorerDirs.App.exe`，独立 profile。在 temp.40 的多选恢复修复上，额外旧选区的 replacement fallback 改为使用 `IFolderView2::GetSelectedItem` 逐项清除，避免 PIDL 到视图索引的全目录搜索。App/NativeProbe Release 构建、完整 Release SettingsSmoke、两轮原生探针与发布隔离 smoke 通过。未启动 ExplorerBrowser 生产宿主或 GUI；焦点、四窗格及大目录行为仍待验收。详见[第四十一版说明](../docs/releases/preview-0.6.0-temp.41.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.40/app/ExplorerDirs.App.exe`，独立 profile。此版修正 Shell 多选恢复批量标志及额外旧选区 fallback，并附隐藏默认 `IShellView` 回归探针。App/NativeProbe Release 构建、完整 Release SettingsSmoke 与两轮原生探针通过。没有运行 ExplorerBrowser 生产宿主或可见 GUI；焦点、四窗格和大目录行为仍待验收。详见[第四十版说明](../docs/releases/preview-0.6.0-temp.40.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.39/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.38，选区 PIDL 比较增加 `ILIsEqual` 二进制精确相等快路，非精确相等时保留 Shell canonical 比较；隐藏默认 `IShellView` 使用生产快照代码验证精确路径和跳过批量重选。App、NativeProbe、HostProbe Release 构建、完整 Release SettingsSmoke 与发布隔离 smoke 通过。没有运行 ExplorerBrowser 生产宿主或可见 GUI，未证明 50,000 项性能收益；隐藏视图未确认焦点恢复。程序集产品版本为 `0.6.0-preview.temp.39+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL SHA-256 分别为 `59E756D9B7E66FBE9EBDAB41816E096AB73AF2F3F7932291E48C32AE9FCD1C88` / `5913D4D0A221502AEE2309B37E12FD489D2F68E0241F0CBA5754EF61FD5D42FF`。详见[第三十九版说明](../docs/releases/preview-0.6.0-temp.39.md)。

## 上一临时预览包（2026-10-25）

`preview-0.6.0-temp.38/app/ExplorerDirs.App.exe`，独立 profile。当前源码新增原生列标题标志接入，意在让所有查看方式显示列头；Release App/NativeProbe/HostProbe 构建、完整 Release SettingsSmoke 和发布隔离 smoke 通过。没有运行 ExplorerBrowser 宿主探针或 GUI，实际列头、排序/排列及焦点保持待预览。程序集产品版本为 `0.6.0-preview.temp.38+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL SHA-256 分别为 `C3E355E7621D5EA480C10C7AD3D961905E3095BEA080DBB027F34EE13E5D7CE9` / `0824CA669A92EC8A5B41EE2DD369E4639952B0EBFBC5ECC76F55DBDBCA967FE5`，与包内清单一致。详见[第三十八版说明](../docs/releases/preview-0.6.0-temp.38.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.37/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.36，文件监视器对 Changed 事件按条目去重，并在静默期后发送 Shell `UPDATEITEM`；批次超限或路径过长仍回退整视图刷新。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；没有启动 GUI，元数据可见刷新、焦点/选区保持及 STA 响应时间待验收。产品版本 `0.6.0-preview.temp.37+77495f5d72a4739c2bb33a21d021328294f728c0`；EXE SHA-256 `AC2F0720FAD161B308248986079DE13A2D93EEF7FC90B2CA8C2F8455C0292C37`，DLL SHA-256 `C1273E7D86260EF185978278FB1A028805802915169D8BE0C48568334693566D`。详见[第三十七版说明](../docs/releases/preview-0.6.0-temp.37.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.36/app/ExplorerDirs.App.exe`，独立 profile。新增 Modern 地址栏 F4 两阶段原生 Shell 文件夹树；Classic F4 直接编辑路径，地址历史改由独立按钮打开。Release SettingsSmoke、App Release 构建、发布隔离 smoke 通过；没有启动 GUI，弹出位置、原生焦点、Escape 和虚拟位置可见导航仍待验收。产品版本为 `0.6.0-preview.temp.36+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL SHA-256 分别为 `4C6955831F510985098B056CAFAB90000E510BEAC9E04F93E2FC678CD2A94227` / `89E14C7891EE1E6A0FED8D4CF6CE89435FE29D1437499D5CB997ACDEFB928E08`。详见[第三十六版说明](../docs/releases/preview-0.6.0-temp.36.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.35/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.34，增加可选的右键 UIA 命中诊断，记录坐标、目标类别和控件类型链，不记录控件名称或文件路径；原生 Shell 菜单生成方式不变。App/SettingsSmoke Release 构建、右键目标专项 smoke、发布隔离 smoke 通过；没有启动 GUI。产品版本为 `0.6.0-preview.temp.35+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL SHA-256 分别为 `A97B5B6F53D78366C0C3F59552C98599885A09720ECDBCCDC34D97F9820AF053` / `C3A8A992F39FC06F345E14128E5280838DFD0F7852007E9DCB735B8D491DB189`。详见[第三十五版说明](../docs/releases/preview-0.6.0-temp.35.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.34/app/ExplorerDirs.App.exe`，独立 profile。CSV 与制表符 TXT 导出会为公式样式文本加前导 Tab，以降低电子表格公式注入风险；Excel 导入后的单元格原始值因此多一个 Tab，XLS 导出仍保留字符串原值。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；EXE/DLL 哈希和产品版本已核对。未启动 GUI 或 Excel。详见[第三十四版说明](../docs/releases/preview-0.6.0-temp.34.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.33/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.32，地址历史按钮在紧凑窗格下同步缩窄其布局占位；其余地址历史入口行为不变。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；没有启动 GUI。详见[第三十三版说明](../docs/releases/preview-0.6.0-temp.33.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.32/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.31，地址栏右侧新增历史下拉按钮，和 F4 打开相同的当前标签最近路径列表；地址编辑框的原生右键菜单保留。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；没有启动 GUI。详见[第三十二版说明](../docs/releases/preview-0.6.0-temp.32.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.31/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.30，新增 Classic 完整路径与 Modern 面包屑地址栏切换，并保存于独立 `AddressBar.ini`。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；没有启动 GUI。详见[第三十一版说明](../docs/releases/preview-0.6.0-temp.31.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.30/app/ExplorerDirs.App.exe`，独立 profile。地址栏绝对盘符/UNC 路径支持正斜杠与重复分隔符规范化；Shell 名称、URI、相对输入和设备路径保留原样。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；没有启动 GUI。详见[第三十版说明](../docs/releases/preview-0.6.0-temp.30.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.29/app/ExplorerDirs.App.exe`，独立 profile。增加 F4 当前标签最近访问目录列表与 F9 按需递归统计当前文件系统目录大小/文件数/子目录数。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；没有启动 GUI。详见[第二十九版说明](../docs/releases/preview-0.6.0-temp.29.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.28/app/ExplorerDirs.App.exe`，独立 profile。基于 temp.27 增加对指向“此电脑”等虚拟 Shell 文件夹的 `.lnk` PIDL 支持；已通过 Shell 文件夹属性校验后在活动窗格导航。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；没有启动 GUI。详见[第二十八版说明](../docs/releases/preview-0.6.0-temp.28.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.27/app/ExplorerDirs.App.exe`，独立 profile。新增独立 Q-Dir 风格 Quick Links 菜单和 profile 内 `.lnk` 管理，并让隔离预览包保留快速链接。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；菜单、图标及 Shell 实际启动行为尚未 GUI 验收。详见[第二十七版说明](../docs/releases/preview-0.6.0-temp.27.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.26/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.25，增加 Shell 文件命令状态查询上下文失效保护和大目录 Shell 计数停滞时提前刷新兜底；并保留已实现的任意可解析 Shell 文件夹 `.qdr` 导入支持。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；包内版本和 EXE/DLL 哈希已核对。未启动 GUI；可见菜单、自动刷新、虚拟位置导航及 Q-Dir 实际互操作仍待验收。详见[第二十六版说明](../docs/releases/preview-0.6.0-temp.26.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.25/app/ExplorerDirs.App.exe`，独立 profile。修复文件命令工具栏在鼠标停留时未随活动窗格选区、导航和标签变化刷新状态的问题。完整 Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；未启动 GUI。可见工具栏状态和第三方 Shell 扩展仍待验收。详见[第二十五版说明](../docs/releases/preview-0.6.0-temp.25.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.24/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.23，修复 64 位快速筛选 Shell 回调的 `STRRET` 接收缓冲区长度不足（264→272 字节）。Release `SettingsSmoke`、App/NativeProbe Release 构建和发布隔离 smoke 通过；包内产品版本和 EXE/DLL SHA-256 已核对。没有启动 GUI；真实筛选显示行为仍待验收。详见[第二十四版说明](../docs/releases/preview-0.6.0-temp.24.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.23/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.22，Shell 搜索范围扩展到已识别的虚拟文件夹及绝对 `::{GUID}` 身份。Release `SettingsSmoke`、NativeProbe 虚拟范围探针、App/NativeProbe Release 构建和发布隔离 smoke 通过；包内产品版本和 EXE/DLL SHA-256 已核对。没有启动 GUI；完整搜索结果和可见导航仍待验收。详见[第二十三版说明](../docs/releases/preview-0.6.0-temp.23.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.22/app/ExplorerDirs.App.exe`，独立 profile。新增“工具 → 打开崩溃诊断目录”，可查看当前 profile 下的托管崩溃报告和异常退出标记，不更改 WER 配置。App Release 构建与 Release SettingsSmoke 通过；产品版本和 EXE/DLL SHA-256 已核对。没有启动 GUI；实际菜单点击及原生 Shell 崩溃取证仍待验收。详见[第二十二版说明](../docs/releases/preview-0.6.0-temp.22.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.21/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.20，新增 Q-Dir `tree_mode` 基础字段映射、目录树按钮的 Tab/Shift+Tab 停靠点，以及键盘 Shell 上下文菜单坐标识别修正。App Release 构建、Release SettingsSmoke、发布隔离 smoke 和文件夹右键入口隔离 smoke 通过；包内版本和 EXE/DLL 哈希已核对。没有启动 GUI 或 Q-Dir；实际目录树焦点、键盘菜单及 Q-Dir 可见互操作仍待验收。详见[第二十一版说明](../docs/releases/preview-0.6.0-temp.21.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.20/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.19，修复环境变量刷新部分回滚失败时未能记录残留变量、后续刷新无法清理的问题；保留 temp.19 的目录树宽度扩展和隔离 profile 配置复制。Release SettingsSmoke、App Release 构建及发布隔离 smoke 通过；包内 EXE/DLL 哈希和产品版本已核对。未启动 GUI 或 Q-Dir；真实注册表变更通知和可见 Shell 交互仍待验收。详见[第二十版说明](../docs/releases/preview-0.6.0-temp.20.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.19/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.18，新增 Explorer Dirs `.qdr` 扩展 v4 的五路目录树宽度导入/导出，并修正隔离 profile 漏拷 `NavigationTreeWidths.ini`。全量 Release SettingsSmoke、App Release 构建、发布隔离 smoke 通过；包内 EXE/DLL 哈希和产品版本已核对。未启动 GUI 或 Q-Dir；导入对话框、取消/确认、重启恢复、可见拖动宽度及 Q-Dir 实际互操作仍待验收。详见[第十九版说明](../docs/releases/preview-0.6.0-temp.19.md)。

## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.18/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.17，新增标签栏空白处拖入单个文件系统文件夹/驱动器并创建新标签；普通 CF_HDROP 文件夹/文件/多项决策 smoke、原生 CIDA 虚拟 Shell 文件夹解析 smoke、完整 Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过，EXE/DLL 哈希与产品版本已核对。未启动 GUI；真实虚拟 Shell 拖放来源、标签栏命中和导航行为仍待桌面验收。详见[第十八版说明](../docs/releases/preview-0.6.0-temp.18.md)。
## 上一版临时预览包（2026-10-25）

`preview-0.6.0-temp.17/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.16，汇总目录 watcher 失效恢复和文件元数据/equal-count 更新处理、Q-Dir 预览字段及树模式扩展保存、Shell 重命名方向键路由和 Ctrl+Y 原生重做兜底。Release SettingsSmoke、Shell 位置/Q-Dir 导入专项、App/HostProbe/NativeProbe Release 构建和发布隔离 smoke 均通过。程序集产品版本与 EXE/DLL 哈希已核对；没有启动 GUI 或 Q-Dir，真实窗口与 Q-Dir 实际互操作仍待验收。详见[第十七版说明](../docs/releases/preview-0.6.0-temp.17.md)。

## 上一版临时预览包（2026-10-24）

`preview-0.6.0-temp.16/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.15，新增共享目录树/逐窗格树/关闭模式及布局切换后同步活动目录；会话与 `.qdr` 接受可解析并确认是文件夹的 Shell 绝对解析名，未确认的 Q-Dir 树模式/比例字段在导入预览中提示不应用。Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建与发布隔离 smoke 通过；EXE/DLL SHA-256 和产品版本已核对。未启动 GUI、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir；目录树可见行为、Shell 虚拟位置导航、自动刷新恢复、网络重连、跨窗格拖放与 Q-Dir 实际互操作仍待验收。详见[第十六版说明](../docs/releases/preview-0.6.0-temp.16.md)。

`preview-0.6.0-temp.15/app/ExplorerDirs.App.exe`，独立 profile。相较 temp.14，新增目录 watcher 首次创建失败或收到 Error 后自动重建，250 ms 起指数退避、最长 30 秒，成功后请求协调刷新；导航切换退休旧重试。详见[第十五版说明](../docs/releases/preview-0.6.0-temp.15.md)。

`preview-0.6.0-temp.14/app/ExplorerDirs.App.exe`，已核对 SHA-256 及产品版本；该版本修复 Q-Dir 状态导出遗漏目录树模式，以及明确无效 Shell 位置的导入提示。未启动任何 GUI，详见[第十四版说明](../docs/releases/preview-0.6.0-temp.14.md)。

`preview-0.6.0-temp.13/app/ExplorerDirs.App.exe`，当前源码预览，包内独立配置；汇总 Shell 文件视图、多窗格/标签/布局、`.qdr` 导入导出、查看方式及键盘路由、搜索/筛选、目录树、会话和状态栏工具。发布隔离 smoke、全量 Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建通过；EXE/DLL SHA-256 清单、DLL 产品版本和包内 profile 已核对。未启动 GUI、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir；真实窗口焦点/鼠标路由、导入颜色、自动刷新长尾、Shell 扩展及 Q-Dir 实际互操作仍待验收。详见[第十三版说明](../docs/releases/preview-0.6.0-temp.13.md)。

`preview-0.6.0-temp.12/app/ExplorerDirs.App.exe`，独立配置，从当前源码构建；新增任意已确认 Shell 文件夹解析名的 `.qdr`/会话支持、友好 Shell 位置名称、新建文件夹重命名焦点等待，以及状态栏可用容量百分比。发布隔离 smoke、Release SettingsSmoke 与 App Release 构建通过；EXE/DLL SHA-256 与包内清单一致。未启动 GUI、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir，Shell 虚拟位置可见导航、原生重命名、焦点、窄窗格布局和实际 `.qdr` 互操作仍待验收。详见[第十二版说明](../docs/releases/preview-0.6.0-temp.12.md)。

`preview-0.6.0-temp.11/app/ExplorerDirs.App.exe`，独立配置，从当前源码构建；目录 watcher 在 Shell 计数稳定前先缓存变更，慢枚举时继续降频等待；已监控的现有文件发生大小、修改时间、属性或创建时间变化时也会请求视图刷新。发布隔离 smoke、Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建均通过；EXE/DLL SHA-256 与包内清单一致。SettingsSmoke 用唯一临时目录验证 Windows `FileSystemWatcher` 的文件追加 `Changed` 事件。未启动 GUI，实际 ExplorerBrowser 元数据列更新、启动期事件与 Shell 的交互、连续写入和网络目录仍待验收。详见[第十一版说明](../docs/releases/preview-0.6.0-temp.11.md)。

## 上一版临时预览包（2026-10-24）

`preview-0.6.0-temp.10/app/ExplorerDirs.App.exe`，独立配置，从当前源码构建；包括 temp.9 后的状态栏查看入口键盘遍历、选中大小缓存节流及焦点恢复前台归属门控。发布隔离 smoke、Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建均通过；EXE/DLL SHA-256 与包内清单一致。未启动 GUI，仍须检查真实 Shell 键鼠行为及 Q-Dir `.qdr` 互操作。详见[第十版说明](../docs/releases/preview-0.6.0-temp.10.md)。

## 上一版临时预览包（2026-10-24）

`preview-0.6.0-temp.9/app/ExplorerDirs.App.exe`，独立配置，汇总当前源码中的 Shell 视图交互、Q-Dir `.qdr` 导入/导出、多窗口独立会话、当前视图导出/打印和目录实时更新等功能。App、HostProbe、NativeProbe Release 构建，Release SettingsSmoke 与发布隔离 smoke 均通过；EXE/DLL 哈希见包内 `app/SHA256.txt`。未启动 GUI，也未验证 Q-Dir 实际读取 `.qdr`；详细范围和待测项见[第九版说明](../docs/releases/preview-0.6.0-temp.9.md)。

## 最新临时试用包（2026-10-24）

`preview-0.6.0-temp.8/app/ExplorerDirs.App.exe`，独立配置，从当前源码构建；程序集信息版本、EXE/DLL SHA-256 已核对。主要变化是目录 watcher 提前启动、缓存基线建立期间事件，且慢枚举时降频继续等待。请结合[第八版说明](../docs/releases/preview-0.6.0-temp.8.md)与包内 `RELEASE-NOTES.md` 预览。未启动 GUI；慢目录真实变更、Shell 通知交互及可见选区/焦点仍待验收。旧包保留，不覆盖日常 profile。

## 上一版临时试用包（2026-10-24）

`preview-0.6.0-temp.7/app/ExplorerDirs.App.exe`，独立配置，从当前源码构建；程序集信息版本、EXE/DLL SHA-256 已核对。主要新增文件列表和目录树中键打开文件夹到新标签。请结合[第七版说明](../docs/releases/preview-0.6.0-temp.7.md)与包内 `RELEASE-NOTES.md` 预览。此包未启动 GUI；UI Automation 对真实文件行的命中、中键、焦点与可见新标签仍待验收。旧包保留，不覆盖日常 profile。

## 上一版临时试用包（2026-10-24）

`preview-0.6.0-temp.6/app/ExplorerDirs.App.exe`，独立配置，从当前源码构建；程序集信息版本、EXE/DLL SHA-256 已核对。请结合[第六版说明](../docs/releases/preview-0.6.0-temp.6.md)与包内 `RELEASE-NOTES.md` 预览。此包未启动 GUI；真实窗口体验、虚拟 Shell 导航、打印预览、自动刷新边界与 Q-Dir 实际互操作仍有待测项。旧包保留，不覆盖日常 profile。

## 上一版临时试用包（2026-10-24）

`preview-0.6.0-temp.5/app/ExplorerDirs.App.exe`，独立配置，使用当前源码构建；EXE/DLL 哈希和程序集产品版本已核对。请结合[第五版说明](../docs/releases/preview-0.6.0-temp.5.md)与包内 `RELEASE-NOTES.md` 预览。此包未做真实 GUI 启动验收；自动刷新、右键目标/混合菜单、多窗口可见恢复、Ctrl+滚轮物理输入、F2 多窗格编辑及 Q-Dir 实际互操作仍有明确待测项。旧包保留，不覆盖日常 profile。

## 临时试用包（2026-10-19）

用户要求提前预览的 `preview-0.6.0-temp.1/app/ExplorerDirs.App.exe` 已打包，**完整 GUI 验收未完成**，日常版仍保留 0.5.2。临时包使用自己的 `app/profile/`，打包时复制现有 INI，此后的修改不会写回日常版配置。启动入口比旧版多一层 `app`；完整变化及待测项见 [临时版说明](../docs/releases/preview-0.6.0-temp.1.md)，包内也附 `RELEASE-NOTES.md`。

临时隔离包使用 `scripts/publish.ps1 -Version preview-0.6.0-temp.1 -IsolatedProfile -ReleaseNotes docs/releases/preview-0.6.0-temp.1.md`。正常版本的发布路径和共享配置规则不变。校验清单同时覆盖 EXE 和 DLL；系统入口仍须另行验收和显式启用。

从隔离 worktree 发布临时预览时，可通过 `-ProfileRoot D:\projects\explorer-dirs\profile` 指向主项目现有 profile；脚本只复制其允许列表内的配置文件、已登记窗口会话侧车及顶层工作区/快速链接，不修改来源。省略参数时仍默认从当前源码仓库的 `profile/` 复制。

2026-10-23 发布脚本补齐隔离配置复制：会话、目录收藏、固定标签、紧凑选项、目录树和布局比例的独立 INI，以及 `Workspaces` 顶层 `.qdr` 收藏。仅复制原文，不解析或执行，不回写源 profile；不递归复制其他目录。此修正只影响之后打包的版本，现有包未改动。`pwsh -NoProfile -File scripts/publish-smoke.ps1` 用独立 fixture 仓库和占位构建文件检查复制、源文件不变、同名版本拒绝及普通发布行为；它不启动应用，也不代替实际发布构建或 GUI 验收。

2026-10-24 修正隔离预览漏拷贝 `QuickFilterFavorites.ini`：新打包的独立 profile 会连同用户保存的筛选预设一起复制。发布 smoke 先因该文件缺失失败，修复后通过；不会修改现有 release 或日常 profile。

2026-10-24 发布脚本按 `preview-X.Y.Z-temp.N` 路径生成 `X.Y.Z-preview.temp.N` 程序版本，`preview-X.Y.Z` 生成 `X.Y.Z-preview`；隔离 smoke 验证传给 `dotnet publish` 的版本参数。temp.4 的实际 DLL 产品版本、EXE/DLL 哈希、包内独立配置及发布说明已逐项核对；未启动可执行文件。

## 日常预览版

每个版本放在独立子目录中。当前供预览的版本为 `releases\preview-0.5.2\ExplorerDirs.App.exe`。这些是完整发布输出，不依赖 `src` 下的编译结果；请从这里启动日常使用的版本。

当前预览版是依赖 .NET 10 Windows Desktop Runtime 的 Windows 桌面程序。`SHA256.txt` 记录主程序校验值。旧版本保留供回退；不要在程序运行时覆盖其目录。

`preview-0.5.2` 增加逐窗格的 Tab / Shift+Tab 正反向焦点顺序，包括详细信息列标题，并修复粘贴后虽然窗口焦点回到文件区、方向键却失效的情况。隔离测试目录中已验证四窗格跨窗格反向焦点、复制粘贴后直接使用方向键，以及本机无确认框的 Shift+Delete；有确认框时仍待用户复测。发布版配置在项目根目录 `profile\ExplorerDirs.ini`，与 `src` 调试配置隔离；旧 `session.json` 首次运行时仅迁移布局与路径，旧文件仍保留。原生菜单、查看/排序/分组和第三方扩展仍待手工验收；保留 `preview-0.5.1` 供回退。

发布新版本：在项目根目录执行 `powershell.exe -File .\scripts\publish.ps1 -Version preview-X.Y.Z`。脚本为每次构建使用独立暂存目录；同名版本已存在或发布期间出现时会拒绝完成，失败的暂存目录保留供检查。

未来版本的发布脚本会随 exe 写入 `FolderRoutingCapability.ini`，记录单目录参数能力与主程序 SHA-256，供可选文件夹右键入口核对。现有 `preview-0.5.2` 没有此清单，不能作为该入口的目标；新版本也须先完成真实窗口验收，再显式启用右键入口。发布脚本和入口脚本都不会自动更改 Windows 默认打开动作或 Win+E。
